using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdatePreferencesRequest
{
    [RegularExpression("^(system|dark|light|high-contrast)$", ErrorMessage = "Theme must be a valid value.")]
    public string? Theme { get; set; }

    public bool AnalyticsConsentOptedOut { get; set; }

    [Range(1, 2, ErrorMessage = "Book info provider must be a valid provider.")]
    public int? PreferredBookInfoProviderId { get; set; }
}
