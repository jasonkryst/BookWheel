namespace BookWheel.Models;

public sealed class UserPreferences
{
    public string? Theme { get; set; }
    public bool AnalyticsConsentOptedOut { get; set; }
    public int? PreferredBookInfoProviderId { get; set; }
}
