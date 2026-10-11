namespace BookWheel.Storage.Postgres.Entities;

// Keyed by an arbitrary normalized username string (deliberately NOT a FK to users):
// unknown usernames must lock out too, so lockout behaviour cannot be used to
// distinguish real accounts from made-up ones.
public sealed class LoginThrottleEntity
{
    public string NormalizedUsername { get; set; } = string.Empty;
    public int FailedCount { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
