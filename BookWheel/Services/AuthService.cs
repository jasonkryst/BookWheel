using System.Collections.Concurrent;
using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class AuthService
{
    private readonly CredentialStore _credentialStore;
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

    public AuthService(CredentialStore credentialStore, IOptions<SecurityOptions> securityOptions)
    {
        _credentialStore = credentialStore;
        _securityOptions = securityOptions.Value;
    }

    public Task<bool> HasAccountAsync()
    {
        return _credentialStore.HasAccountAsync();
    }

    public async Task<AuthenticatedUser> CreateAccountAsync(string username, string password)
    {
        var user = await _credentialStore.CreateInitialAccountAsync(username, password);
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

        var user = await _credentialStore.ValidateCredentialsAsync(username, password);
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

    public Task<string> CompletePasswordResetAsync(string token, string newPassword)
    {
        return _credentialStore.CompletePasswordResetAsync(token, newPassword);
    }

    public Task<PasswordResetTokenValidationResult> ValidatePasswordResetTokenAsync(string token)
    {
        return _credentialStore.ValidatePasswordResetTokenAsync(token);
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
