using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

/// <summary>
/// Validates that a nullable locale field is one of <see cref="SupportedLocales.Values"/>.
/// Replaces a hard-coded regex of locale codes so adding a language only
/// requires editing <c>wwwroot/i18n/locales.json</c>.
/// </summary>
public sealed class SupportedLocaleAttribute : ValidationAttribute
{
    public SupportedLocaleAttribute() : base("Locale must be a supported language.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string locale && SupportedLocales.IsSupported(locale));
}
