namespace BookWheel.Models;

public sealed class BookMetadataOptions
{
    public const string SectionName = "BookMetadata";

    public int TitleSearchResultLimit { get; set; } = 10;
    public GoogleBooksOptions GoogleBooks { get; set; } = new();
}

public sealed class GoogleBooksOptions
{
    public string? ApiKey { get; set; }
}
