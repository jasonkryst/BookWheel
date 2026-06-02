namespace BookWheel.Models;

public sealed class MetricsSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; set; }
    public long LoginFailureCount { get; set; }
    public long LoginLockoutCount { get; set; }
    public long SuccessfulLoginCount { get; set; }
    public long SpinCount { get; set; }
    public int TotalBookCount { get; set; }
}
