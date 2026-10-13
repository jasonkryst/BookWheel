using System.Collections.Concurrent;
using BookWheel.Models;
using BookWheel.Storage;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class AuthService
{
    private readonly ICredentialRepository _credentialRepository;
    private readonly IPasswordResetTokenRepository _resetTokenRepository;
    private readonly SecurityOptions _securityOptions;
    private readonly ILoginThrottleRepository _loginThrottleRepository;
    // Deferred follow-up: persisting these reset/forgot-username rate-limit windows (and
    // distributed per-IP rate limiting) is not done yet; they remain per-instance in-memory.
    private readonly ConcurrentDictionary<string, RateLimitRecord> _passwordResetRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RateLimitRecord> _forgottenUsernameRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly AccountEmailService _accountEmailService;

    private sealed class RateLimitRecord
    {
        public int Count { get; set; }
        public DateTimeOffset WindowStartUtc { get; set; }
    }

    public AuthService(ICredentialRepository credentialRepository, IPasswordResetTokenRepository resetTokenRepository, ILoginThrottleRepository loginThrottleRepository, IOptions<SecurityOptions> securityOptions, AccountEmailService accountEmailService)
    {
        _credentialRepository = credentialRepository;
        _resetTokenRepository = resetTokenRepository;
        _loginThrottleRepository = loginThrottleRepository;
        _securityOptions = securityOptions.Value;
        _accountEmailService = accountEmailService;
    }

    public Task<bool> HasAccountAsync()
    {
        return _credentialRepository.HasAccountAsync();
    }

    public async Task<AuthenticatedUser> CreateAccountAsync(string username, string password, string email)
    {
        var user = await _credentialRepository.CreateInitialAccountAsync(username, password, email);
        return ToAuthenticatedUser(user);
    }

    public async Task<LoginValidationResult> ValidateCredentialsAsync(string username, string password)
    {
        // Derive the lockout key from the resolved account when the username matches one, so
        // every spelling that maps to the same account shares a single lockout bucket. Keying
        // on the raw submitted string would let an attacker split attempts across buckets using
        // inputs that Postgres citext treats as the same account but C# case-folding does not
        // (a parser differential → lockout bypass). Unknown usernames fall back to the submitted
        // value and still lock, preserving the anti-enumeration behaviour.
        var existingAccount = await _credentialRepository.FindByUsernameAsync(username);
        var lockoutKey = existingAccount?.Username ?? username;

        var lockedUntil = await _loginThrottleRepository.GetActiveLockoutAsync(lockoutKey);
        if (lockedUntil.HasValue)
        {
            return new LoginValidationResult
            {
                IsLockedOut = true,
                LockoutEndsAtUtc = lockedUntil
            };
        }

        var user = await _credentialRepository.ValidateCredentialsAsync(username, password);
        if (user is null)
        {
            var threshold = Math.Max(2, _securityOptions.UsernameLockoutThreshold);
            var lockoutDuration = TimeSpan.FromMinutes(Math.Max(1, _securityOptions.UsernameLockoutMinutes));
            var outcome = await _loginThrottleRepository.RegisterFailedAttemptAsync(lockoutKey, threshold, lockoutDuration);
            if (outcome.IsLockedOut)
            {
                return new LoginValidationResult
                {
                    IsLockedOut = true,
                    IsInvalidCredentials = true,
                    LockoutTriggered = true,
                    LockoutEndsAtUtc = outcome.LockedUntilUtc
                };
            }

            return new LoginValidationResult { IsInvalidCredentials = true };
        }

        await _loginThrottleRepository.ClearAsync(lockoutKey);

        if (user.IsDisabled)
        {
            return new LoginValidationResult { IsDisabled = true };
        }

        if (user.IsLocked && user.LockedUntilUtc.GetValueOrDefault(DateTimeOffset.MaxValue) > DateTimeOffset.UtcNow)
        {
            return new LoginValidationResult
            {
                IsLockedOut = true,
                LockoutEndsAtUtc = user.LockedUntilUtc
            };
        }

        if (user.ForcePasswordReset)
        {
            return new LoginValidationResult { RequiresPasswordReset = true };
        }

        return new LoginValidationResult { User = ToAuthenticatedUser(user) };
    }

    private const int RateLimitSweepBatchSize = 20;

    private static bool TryConsumeRateLimit(ConcurrentDictionary<string, RateLimitRecord> store, string key)
    {
        const int maxRequestsPerWindow = 3;
        var window = TimeSpan.FromMinutes(15);
        var now = DateTimeOffset.UtcNow;
        var normalizedKey = key.Trim();

        var record = store.AddOrUpdate(
            normalizedKey,
            _ => new RateLimitRecord { Count = 1, WindowStartUtc = now },
            (_, existing) =>
            {
                if (now - existing.WindowStartUtc > window)
                {
                    existing.WindowStartUtc = now;
                    existing.Count = 1;
                }
                else
                {
                    existing.Count += 1;
                }

                return existing;
            });

        SweepExpiredEntries(store, window, now);

        return record.Count <= maxRequestsPerWindow;
    }

    // These dictionaries have no other eviction mechanism (they live for the lifetime
    // of the singleton AuthService), so every distinct username/email ever probed would
    // otherwise accumulate forever. This is a small self-hosted app rather than an
    // internet-scale target, so a bounded, opportunistic sweep on every call — checking
    // only a small batch of entries rather than the whole dictionary — is enough to keep
    // memory bounded without needing a background timer.
    private static void SweepExpiredEntries(ConcurrentDictionary<string, RateLimitRecord> store, TimeSpan window, DateTimeOffset now)
    {
        var staleThreshold = window + window;
        foreach (var entry in store.Take(RateLimitSweepBatchSize))
        {
            if (now - entry.Value.WindowStartUtc > staleThreshold)
            {
                store.TryRemove(entry.Key, out _);
            }
        }
    }

    public async Task RequestPasswordResetAsync(string username, string appBaseUrl)
    {
        if (!TryConsumeRateLimit(_passwordResetRequests, username))
        {
            return;
        }

        var account = await _credentialRepository.FindByUsernameAsync(username);
        if (account is null || account.IsDisabled || account.IsLocked || string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(appBaseUrl))
        {
            return;
        }

        // Deliberately does NOT call CreatePasswordResetLinkAsync/MarkForPasswordResetAsync:
        // that would set ForcePasswordReset (killing the account's current password
        // immediately, before the caller even opens the email) and clear IsLocked
        // (silently undoing an administrator's deliberate lock) — both unauthenticated,
        // anonymous-caller side effects that a plain "I forgot my password" request has
        // no business triggering. The reset token itself is the security gate here
        // (validated in CompletePasswordResetAsync); ForcePasswordReset is a separate,
        // admin-only signal reserved for the admin-triggered flow in UsersController.
        var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(account.UserId);
        var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
        var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";

        // Fire-and-forget for the same anti-enumeration timing reason documented on
        // CreatePasswordResetLinkAsync below.
        _ = _accountEmailService.SendPasswordResetEmailAsync(account.Email, account.Username, resetLink, expiresAtUtc, CancellationToken.None);
    }

    public async Task RequestForgottenUsernameAsync(string email)
    {
        if (!TryConsumeRateLimit(_forgottenUsernameRequests, email))
        {
            return;
        }

        var username = await _credentialRepository.FindUsernameByEmailAsync(email);
        if (username is null)
        {
            return;
        }

        // Fire-and-forget for the same timing reason as CreatePasswordResetLinkAsync below.
        _ = _accountEmailService.SendForgottenUsernameEmailAsync(email, username, CancellationToken.None);
    }

    public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username, bool EmailQueued)> CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl, PasswordLinkPurpose purpose = PasswordLinkPurpose.Reset, string? inviterUsername = null)
    {
        var user = await _credentialRepository.MarkForPasswordResetAsync(userId);
        var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(userId);

        var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
        var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";

        var emailQueued = !string.IsNullOrWhiteSpace(user.Email);
        if (emailQueued)
        {
            // Fire-and-forget: awaiting a live SMTP round trip here would make "account
            // exists and has an email" measurably slower than every other outcome for
            // RequestPasswordResetAsync's self-service caller, defeating the anti-
            // enumeration guarantee that method exists to provide. SendAsync never
            // throws (see SmtpEmailSender), so there's nothing to await for error
            // handling, and AccountEmailService/IEmailSender are both registered as
            // singletons, so this detached task safely outlives the HTTP request.
            // CancellationToken.None is deliberate — the send must not be cut short
            // by the request's own lifetime.
            if (purpose == PasswordLinkPurpose.Invitation)
            {
                _ = _accountEmailService.SendInvitationEmailAsync(user.Email!, user.Username, inviterUsername ?? "An administrator", resetLink, expiresAtUtc, CancellationToken.None);
            }
            else
            {
                _ = _accountEmailService.SendPasswordResetEmailAsync(user.Email!, user.Username, resetLink, expiresAtUtc, CancellationToken.None);
            }
        }

        return (resetLink, expiresAtUtc, user.Username, emailQueued);
    }

    public async Task<string> CompletePasswordResetAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
        {
            throw new InvalidOperationException("A valid token and password are required.");
        }

        var lookup = await _resetTokenRepository.ValidateAsync(token);
        if (!lookup.IsValid)
        {
            throw new InvalidOperationException("The password reset link is invalid or has expired.");
        }

        var username = await _credentialRepository.SetPasswordAsync(lookup.UserId, newPassword);
        await _resetTokenRepository.CompleteAsync(token);
        return username;
    }

    public async Task<AuthenticatedUser> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var account = await _credentialRepository.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        // Subject the current-password check to the same lockout as login. Otherwise a
        // stolen session cookie (session but not password) could brute-force the current
        // password here, since this endpoint sits behind only a rate limit. Keyed by the
        // resolved account username, consistent with the login throttle.
        var lockedUntil = await _loginThrottleRepository.GetActiveLockoutAsync(account.Username);
        if (lockedUntil.HasValue)
        {
            throw new InvalidOperationException("Too many failed attempts. Please try again later.");
        }

        var verified = await _credentialRepository.ValidateCredentialsAsync(account.Username, currentPassword);
        if (verified is null)
        {
            var threshold = Math.Max(2, _securityOptions.UsernameLockoutThreshold);
            var lockoutDuration = TimeSpan.FromMinutes(Math.Max(1, _securityOptions.UsernameLockoutMinutes));
            await _loginThrottleRepository.RegisterFailedAttemptAsync(account.Username, threshold, lockoutDuration);
            throw new InvalidOperationException("The current password is incorrect.");
        }

        await _loginThrottleRepository.ClearAsync(account.Username);

        if (string.Equals(newPassword, currentPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The new password must be different from your current password.");
        }

        // Rotates the security stamp, which invalidates every existing session cookie.
        // The caller re-issues the current session's cookie with the fresh stamp.
        await _credentialRepository.SetPasswordAsync(userId, newPassword);

        var updated = await _credentialRepository.FindByIdAsync(userId);
        return ToAuthenticatedUser(updated!);
    }

    public async Task<PasswordResetTokenValidationResult> ValidatePasswordResetTokenAsync(string token)
    {
        var lookup = await _resetTokenRepository.ValidateAsync(token);
        if (!lookup.IsValid)
        {
            return new PasswordResetTokenValidationResult { IsValid = false };
        }

        var username = await _credentialRepository.GetUsernameAsync(lookup.UserId);
        if (username is null)
        {
            return new PasswordResetTokenValidationResult { IsValid = false };
        }

        return new PasswordResetTokenValidationResult
        {
            IsValid = true,
            Username = username,
            ExpiresAtUtc = lookup.ExpiresAtUtc
        };
    }

    private static AuthenticatedUser ToAuthenticatedUser(CredentialRecord credential)
    {
        return new AuthenticatedUser
        {
            UserId = credential.UserId,
            Username = credential.Username,
            IsAdmin = credential.IsAdmin,
            Email = credential.Email,
            SecurityStamp = credential.SecurityStamp
        };
    }
}
