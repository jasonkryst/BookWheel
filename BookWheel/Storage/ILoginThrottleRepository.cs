namespace BookWheel.Storage;

public sealed class LoginLockoutOutcome
{
    public bool IsLockedOut { get; init; }
    public DateTimeOffset? LockedUntilUtc { get; init; }
}

public static class LoginThrottleKey
{
    // Single source of truth for how usernames are keyed in the throttle store.
    // Matches the previous in-memory dictionary (trimmed, case-insensitive).
    public static string Normalize(string username) => (username ?? string.Empty).Trim().ToLowerInvariant();
}

public interface ILoginThrottleRepository
{
    /// <summary>Returns the lockout end time if the username is currently locked out, otherwise null.</summary>
    Task<DateTimeOffset?> GetActiveLockoutAsync(string username);

    /// <summary>
    /// Records a failed attempt. When the failure count reaches <paramref name="threshold"/>,
    /// the username is locked for <paramref name="lockoutDuration"/> and the count resets.
    /// </summary>
    Task<LoginLockoutOutcome> RegisterFailedAttemptAsync(string username, int threshold, TimeSpan lockoutDuration);

    /// <summary>Removes any throttle record for the username (called on successful credential validation).</summary>
    Task ClearAsync(string username);

    /// <summary>Deletes rows not updated since the cutoff. Returns the number of rows removed.</summary>
    Task<int> DeleteStaleAsync(DateTimeOffset olderThanUpdatedAtUtc);
}
