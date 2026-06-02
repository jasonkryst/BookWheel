namespace BookWheel.Models;

public sealed class LoginValidationResult
{
    public AuthenticatedUser? User { get; set; }
    public bool IsLockedOut { get; set; }
    public bool IsDisabled { get; set; }
    public bool RequiresPasswordReset { get; set; }
    public bool IsInvalidCredentials { get; set; }
    public bool LockoutTriggered { get; set; }
    public DateTimeOffset? LockoutEndsAtUtc { get; set; }
}
