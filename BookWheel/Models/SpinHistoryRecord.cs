namespace BookWheel.Models;

public sealed class SpinHistoryRecord
{
    public Guid BookId { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset SelectedAtUtc { get; set; }
}
