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
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, FailedLoginRecord> _failedLogins = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);

    private sealed class FailedLoginRecord
    {
        public int Count { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
    }

    private sealed class SessionRecord
    {
        public AuthenticatedUser User { get; set; } = new();
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    public AuthService(ICredentialRepository credentialRepository, IPasswordResetTokenRepository resetTokenRepository, IOptions<SecurityOptions> securityOptions)
    {
        _credentialRepository = credentialRepository;
        _resetTokenRepository = resetTokenRepository;
        _securityOptions = securityOptions.Value;
    }

    public Task<bool> HasAccountAsync()
    {
        return _credentialRepository.HasAccountAsync();
    }

    public async Task<AuthenticatedUser> CreateAccountAsync(string username, string password)
    {
        var user = await _credentialRepository.CreateInitialAccountAsync(username, password);
        return ToAuthenticatedUser(user);
    }

    public async Task<LoginValidationResult> ValidateCredentialsAsync(string username, string password)
    {
        var normalizedUsername = username.Trim();
        var failedRecord = _failedLogins.GetOrAdd(normalizedUsername, _ => new FailedLoginRecord());
        if (failedRecord.LockedUntilUtc.HasValue && failedRecord.LockedUntilUtc.Value > DateTimeOffset.UtcNow)
        {
            return new LoginValidationResult
            {
                IsLockedOut = true,
                LockoutEndsAtUtc = failedRecord.LockedUntilUtc
            };
        }

        var user = await _credentialRepository.ValidateCredentialsAsync(username, password);
        if (user is null)
        {
            failedRecord.Count += 1;
            var count = failedRecord.Count;
            var threshold = Math.Max(2, _securityOptions.UsernameLockoutThreshold);
            if (count >= threshold)
            {
                var lockoutDuration = TimeSpan.FromMinutes(Math.Max(1, _securityOptions.UsernameLockoutMinutes));
                failedRecord.LockedUntilUtc = DateTimeOffset.UtcNow.Add(lockoutDuration);
                failedRecord.Count = 0;
                return new LoginValidationResult
                {
                    IsLockedOut = true,
                    IsInvalidCredentials = true,
                    LockoutTriggered = true,
                    LockoutEndsAtUtc = failedRecord.LockedUntilUtc
                };
            }

            return new LoginValidationResult { IsInvalidCredentials = true };
        }

        _failedLogins.TryRemove(normalizedUsername, out _);

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

    public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)> CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl)
    {
        var user = await _credentialRepository.MarkForPasswordResetAsync(userId);
        var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(userId);

        var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
        var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";
        return (resetLink, expiresAtUtc, user.Username);
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

    public string CreateSession(AuthenticatedUser user)
    {
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        _sessions[token] = new SessionRecord
        {
            User = user,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_sessionLifetime)
        };
        return token;
    }

    public bool IsAuthenticated(HttpContext context)
    {
        return GetAuthenticatedUser(context) is not null;
    }

    public bool IsAdmin(HttpContext context)
    {
        return GetAuthenticatedUser(context)?.IsAdmin == true;
    }

    public AuthenticatedUser? GetAuthenticatedUser(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue("BookWheel.Auth", out var token))
        {
            return null;
        }

        if (!_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (session.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        session.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_sessionLifetime);
        _sessions[token] = session;
        return session.User;
    }

    public void SignOut(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue("BookWheel.Auth", out var token))
        {
            _sessions.TryRemove(token, out _);
        }

        context.Response.Cookies.Delete("BookWheel.Auth", new CookieOptions { Path = "/" });
    }

    public void RemoveSessionsForUser(Guid userId)
    {
        var sessionTokens = _sessions
            .Where(entry => entry.Value.User.UserId == userId)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var token in sessionTokens)
        {
            _sessions.TryRemove(token, out _);
        }
    }

    private static AuthenticatedUser ToAuthenticatedUser(CredentialRecord credential)
    {
        return new AuthenticatedUser
        {
            UserId = credential.UserId,
            Username = credential.Username,
            IsAdmin = credential.IsAdmin
        };
    }
}
