using System.Security.Cryptography;
using System.Text;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresPasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresPasswordResetTokenRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var expiredOrExisting = await context.PasswordResetTokens
            .Where(t => t.ExpiresAtUtc <= now || t.UserId == userId)
            .ToListAsync();
        context.PasswordResetTokens.RemoveRange(expiredOrExisting);

        var rawToken = GenerateResetToken();
        var expiresAtUtc = now.AddHours(24);
        context.PasswordResetTokens.Add(new PasswordResetTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        });

        await context.SaveChangesAsync();
        return (rawToken, expiresAtUtc);
    }

    public async Task<PasswordResetTokenLookup> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PasswordResetTokenLookup { IsValid = false };
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var expired = await context.PasswordResetTokens.Where(t => t.ExpiresAtUtc <= now).ToListAsync();
        context.PasswordResetTokens.RemoveRange(expired);
        await context.SaveChangesAsync();

        var tokenHash = HashToken(token.Trim());
        var match = await context.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        return match is null
            ? new PasswordResetTokenLookup { IsValid = false }
            : new PasswordResetTokenLookup { IsValid = true, UserId = match.UserId, ExpiresAtUtc = match.ExpiresAtUtc };
    }

    public async Task<Guid> CompleteAsync(string token)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var expired = await context.PasswordResetTokens.Where(t => t.ExpiresAtUtc <= now).ToListAsync();
        context.PasswordResetTokens.RemoveRange(expired);

        var tokenHash = HashToken((token ?? string.Empty).Trim());
        var match = await context.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash)
            ?? throw new InvalidOperationException("The password reset link is invalid or has expired.");

        context.PasswordResetTokens.Remove(match);
        await context.SaveChangesAsync();
        return match.UserId;
    }

    private static string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
