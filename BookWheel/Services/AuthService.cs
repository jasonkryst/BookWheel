using System.Collections.Concurrent;

namespace BookWheel.Services;

public sealed class AuthService
{
    private readonly CredentialStore _credentialStore;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();
    private readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);

    public AuthService(CredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public Task<bool> HasAccountAsync()
    {
        return _credentialStore.HasAccountAsync();
    }

    public Task CreateAccountAsync(string username, string password)
    {
        return _credentialStore.CreateAccountAsync(username, password);
    }

    public Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        return _credentialStore.ValidateCredentialsAsync(username, password);
    }

    public string CreateSession()
    {
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        _sessions[token] = DateTimeOffset.UtcNow.Add(_sessionLifetime);
        return token;
    }

    public bool IsAuthenticated(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue("BookWheel.Auth", out var token))
        {
            return false;
        }

        if (!_sessions.TryGetValue(token, out var expiresAt))
        {
            return false;
        }

        if (expiresAt < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        _sessions[token] = DateTimeOffset.UtcNow.Add(_sessionLifetime);

        return true;
    }

    public void SignOut(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue("BookWheel.Auth", out var token))
        {
            _sessions.TryRemove(token, out _);
        }

        context.Response.Cookies.Delete("BookWheel.Auth", new CookieOptions { Path = "/" });
    }
}
