using System.Text.Json;

namespace BookWheel.Models;

/// <summary>
/// The app's supported locale codes, loaded once at startup from
/// <c>wwwroot/i18n/locales.json</c> — the same file the frontend's i18n.js
/// fetches — so there is a single source of truth shared by
/// <c>RequestLocalizationOptions</c>, <see cref="SupportedLocaleAttribute"/>,
/// and the client.
/// </summary>
public static class SupportedLocales
{
    private static IReadOnlyList<string>? _values;

    public static IReadOnlyList<string> Values =>
        _values ?? throw new InvalidOperationException($"{nameof(SupportedLocales)}.{nameof(Initialize)} must be called during startup.");

    public static string Default => Values[0];

    public static void Initialize(string webRootPath)
    {
        var path = Path.Combine(webRootPath, "i18n", "locales.json");
        var json = File.ReadAllText(path);
        var codes = JsonSerializer.Deserialize<string[]>(json);
        if (codes is null || codes.Length == 0)
        {
            throw new InvalidOperationException($"'{path}' must contain a non-empty JSON array of locale codes.");
        }

        _values = codes;
    }

    public static bool IsSupported(string? locale) =>
        locale is not null && _values is not null && _values.Contains(locale, StringComparer.Ordinal);
}
