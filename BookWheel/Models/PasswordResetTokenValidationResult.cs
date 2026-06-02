namespace BookWheel.Models;

public sealed class PasswordResetTokenValidationResult
{
    public bool IsValid { get; set; }
    public string? Username { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
