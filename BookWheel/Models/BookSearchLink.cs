namespace BookWheel.Models;

public sealed class BookSearchLink
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsAffiliate { get; set; }
}
