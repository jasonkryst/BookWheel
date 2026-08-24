namespace BookWheel.Models;

public sealed class BookMetadataOptions
{
    public const string SectionName = "BookMetadata";

    public int TitleSearchResultLimit { get; set; } = 10;
}
