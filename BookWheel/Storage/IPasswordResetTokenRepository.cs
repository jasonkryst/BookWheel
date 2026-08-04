namespace BookWheel.Storage;

public interface IPasswordResetTokenRepository
{
    Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId);
    Task<PasswordResetTokenLookup> ValidateAsync(string token);
    Task<Guid> CompleteAsync(string token);
}

public sealed class PasswordResetTokenLookup
{
    public bool IsValid { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
