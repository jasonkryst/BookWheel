namespace BookWheel.Models;

public sealed class AdminSpinStatsRecord
{
    public int TotalSpinsAllUsers { get; set; }
    public int ActiveUserCount { get; set; }
    public IReadOnlyList<UserSpinCountRecord> TopUsers { get; set; } = [];
}

public sealed class UserSpinCountRecord
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int SpinCount { get; set; }
}
