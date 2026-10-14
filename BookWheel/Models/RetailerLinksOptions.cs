namespace BookWheel.Models;

public sealed class RetailerLinksOptions
{
    public const string SectionName = "RetailerLinks";

    // Defaults mirror what the app shipped with before this was configurable,
    // plus locale-aware regional alternatives (GH #208). Configuration binds
    // array entries by index, so an env var or appsettings override targets
    // one entry at a time (e.g. "RetailerLinks__Retailers__6__Enabled=false"
    // disables just that one) rather than replacing the whole list — to ship
    // a fully custom list, edit this array directly instead.
    public List<RetailerOptions> Retailers { get; set; } =
    [
        new()
        {
            Name = "Google Books",
            IsbnUrlTemplate = "https://books.google.com/books?vid=ISBN{isbn}",
            TextUrlTemplate = "https://books.google.com/books?q={query}"
        },
        new()
        {
            Name = "Open Library",
            IsbnUrlTemplate = "https://openlibrary.org/isbn/{isbn}",
            TextUrlTemplate = "https://openlibrary.org/search?q={query}"
        },
        new()
        {
            Name = "Goodreads",
            TextUrlTemplate = "https://www.goodreads.com/search?q={query}"
        },
        new()
        {
            Name = "Amazon",
            TextUrlTemplate = "https://www.amazon.com/s?k={query}",
            AffiliateParam = "tag"
        },
        new()
        {
            Name = "Amazon",
            TextUrlTemplate = "https://www.amazon.es/s?k={query}",
            AffiliateParam = "tag",
            Locales = ["es"]
        },
        new()
        {
            Name = "Amazon",
            TextUrlTemplate = "https://www.amazon.pl/s?k={query}",
            AffiliateParam = "tag",
            Locales = ["pl"]
        },
        new()
        {
            Name = "Barnes & Noble",
            TextUrlTemplate = "https://www.barnesandnoble.com/s/{query}",
            Locales = ["en"]
        },
        new()
        {
            Name = "Bookshop.org",
            TextUrlTemplate = "https://bookshop.org/beta-search?keywords={query}",
            Locales = ["es", "pl"]
        }
    ];
}

public sealed class RetailerOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary>URL template used when an ISBN is available; "{isbn}" is replaced with it. Optional — falls back to <see cref="TextUrlTemplate"/> when unset.</summary>
    public string? IsbnUrlTemplate { get; set; }

    /// <summary>URL template used otherwise; "{query}" is replaced with the ISBN (if present but no <see cref="IsbnUrlTemplate"/>) or the title/author text.</summary>
    public string? TextUrlTemplate { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Optional affiliate identifier. Empty by default (no monetization scaffolding is wired up out of the box) — set alongside <see cref="AffiliateParam"/> to opt in.</summary>
    public string? AffiliateTag { get; set; }

    /// <summary>Query-string parameter name the affiliate tag is appended under (e.g. "tag" for Amazon Associates).</summary>
    public string? AffiliateParam { get; set; }

    /// <summary>Locale codes this entry applies to. Empty means "default" — used when no locale-specific entry sharing this <see cref="Name"/> matches the current request's locale.</summary>
    public List<string> Locales { get; set; } = [];
}
