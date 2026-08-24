namespace BookWheel.Models;

public sealed class BookRecord
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public string? Author { get; set; }
    public string? CoverUrl { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public bool AddedByScanner { get; set; }
    public int BookTypeId { get; set; }
}
