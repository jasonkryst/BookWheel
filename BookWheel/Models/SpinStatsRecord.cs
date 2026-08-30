namespace BookWheel.Models;

public sealed class SpinStatsRecord
{
    public int TotalSpins { get; set; }
    public int UniqueBooksSpun { get; set; }
    public int NeverSpunCount { get; set; }
    public WheelDurationRecord? LongestOnWheel { get; set; }
    public WheelDurationRecord? ShortestOnWheel { get; set; }
    public IReadOnlyList<BookSpinCountRecord> TopBooks { get; set; } = [];
}

public sealed class WheelDurationRecord
{
    public Guid BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DaysOnWheel { get; set; }
}

public sealed class BookSpinCountRecord
{
    public Guid BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SpinCount { get; set; }
    public double Percentage { get; set; }
}
