using System.Text.Json;
using System.Xml.Linq;

namespace BookWheel.Tests;

/// <summary>
/// GH #226: locale catalogs now live as separate files (wwwroot/i18n/*.json,
/// Resources/SharedErrors*.resx) instead of one embedded i18n.js object, so
/// nothing forces them to stay in sync. These are the CI gate: a key added to
/// one locale and missed in another fails the build instead of shipping a
/// silent gap that falls back to raw keys or English error text.
/// </summary>
public sealed class I18nCatalogConsistencyTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void Locales_Json_Lists_Every_Frontend_Catalog_File()
    {
        var locales = ReadLocalesJson();

        Assert.NotEmpty(locales);
        foreach (var locale in locales)
        {
            var path = Path.Combine(RepoRoot, "BookWheel", "wwwroot", "i18n", $"{locale}.json");
            Assert.True(File.Exists(path), $"Expected a catalog file at {path} for locale '{locale}'.");
        }
    }

    [Fact]
    public void Every_Frontend_Locale_Catalog_Has_The_Same_Keys_As_The_Default_Locale()
    {
        var locales = ReadLocalesJson();
        var defaultLocale = locales[0];
        var referenceKeys = FlattenKeys(ReadCatalog(defaultLocale)).ToHashSet(StringComparer.Ordinal);

        foreach (var locale in locales.Skip(1))
        {
            var keys = FlattenKeys(ReadCatalog(locale)).ToHashSet(StringComparer.Ordinal);

            var missing = referenceKeys.Except(keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = keys.Except(referenceKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(missing.Count == 0,
                $"i18n/{locale}.json is missing keys present in i18n/{defaultLocale}.json: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0,
                $"i18n/{locale}.json has keys not present in i18n/{defaultLocale}.json: {string.Join(", ", extra)}");
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("pl")]
    public void SharedErrors_Resx_Has_The_Same_Keys_As_The_Default_Catalog(string locale)
    {
        var defaultKeys = ReadResxKeys("SharedErrors.resx");
        var localeKeys = ReadResxKeys($"SharedErrors.{locale}.resx");

        var missing = defaultKeys.Except(localeKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var extra = localeKeys.Except(defaultKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            $"SharedErrors.{locale}.resx is missing keys present in SharedErrors.resx: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0,
            $"SharedErrors.{locale}.resx has keys not present in SharedErrors.resx: {string.Join(", ", extra)}");
    }

    private static List<string> ReadLocalesJson()
    {
        var path = Path.Combine(RepoRoot, "BookWheel", "wwwroot", "i18n", "locales.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static JsonElement ReadCatalog(string locale)
    {
        var path = Path.Combine(RepoRoot, "BookWheel", "wwwroot", "i18n", $"{locale}.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static IEnumerable<string> FlattenKeys(JsonElement element, string prefix = "")
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in FlattenKeys(property.Value, key))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return key;
            }
        }
    }

    private static HashSet<string> ReadResxKeys(string fileName)
    {
        var path = Path.Combine(RepoRoot, "BookWheel", "Resources", fileName);
        var document = XDocument.Load(path);
        return document.Root!.Elements("data")
            .Select(e => e.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookWheel.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repo root (BookWheel.slnx) from {AppContext.BaseDirectory}.");
    }
}
