namespace BookWheel.Models;

public sealed class UserAccountSummary
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool ForcePasswordReset { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
