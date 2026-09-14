namespace BookWheel.Models;

public sealed class BookInfoResult
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Isbn { get; set; }
    public string? CoverUrl { get; set; }
    public int? ProviderId { get; set; }
    public BookSearchLinks Links { get; set; } = new();
}
