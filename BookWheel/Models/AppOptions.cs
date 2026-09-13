namespace BookWheel.Models;

public sealed class AppOptions
{
    public const string SectionName = "App";
    public string BaseUrl { get; set; } = string.Empty;
}
