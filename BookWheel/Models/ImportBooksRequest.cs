namespace BookWheel.Models;

public sealed class ImportBooksRequest
{
    public List<ImportBookItem> Books { get; set; } = [];
}

public sealed class ImportBookItem
{
    public string? Title { get; set; }
    public string? Isbn { get; set; }
    public string? Author { get; set; }
    public string? CoverUrl { get; set; }
}
