using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdatePreferencesRequest
{
    [RegularExpression("^(system|dark|light|high-contrast)$", ErrorMessage = "Theme must be a valid value.")]
    public string? Theme { get; set; }

    public bool AnalyticsConsentOptedOut { get; set; }

    // GH #224: validated against the book_info_providers table in PreferencesController.
    public int? PreferredBookInfoProviderId { get; set; }

    [SupportedLocale]
    public string? Locale { get; set; }
}
