using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class BookSearchLinksService
{
    private readonly RetailerLinksOptions _options;

    public BookSearchLinksService(IOptions<RetailerLinksOptions> options)
    {
        _options = options.Value;
    }

    // Retailers sharing a Name (e.g. three "Amazon" entries for .com/.es/.pl)
    // are locale variants of the same slot: the one whose Locales contains the
    // request's locale wins, falling back to the entry with an empty Locales
    // list (the "default" variant). A retailer with no empty-Locales entry at
    // all (e.g. Bookshop.org, scoped to ["es", "pl"]) simply doesn't appear
    // for locales outside that list.
    public List<BookSearchLink> BuildLinks(string? isbn, string? title, string? author, string locale = "en")
    {
        var textQuery = BuildTextQuery(title, author);
        var links = new List<BookSearchLink>();

        foreach (var group in _options.Retailers.Where(r => r.Enabled).GroupBy(r => r.Name, StringComparer.Ordinal))
        {
            var retailer = group.FirstOrDefault(r => r.Locales.Contains(locale, StringComparer.OrdinalIgnoreCase))
                ?? group.FirstOrDefault(r => r.Locales.Count == 0);

            var url = BuildUrl(retailer, isbn, textQuery);
            if (retailer is null || url is null)
                continue;

            links.Add(new BookSearchLink
            {
                Name = retailer.Name,
                Url = url,
                IsAffiliate = !string.IsNullOrWhiteSpace(retailer.AffiliateTag) && !string.IsNullOrWhiteSpace(retailer.AffiliateParam)
            });
        }

        return links;
    }

    private static string? BuildUrl(RetailerOptions? retailer, string? isbn, string textQuery)
    {
        if (retailer is null)
            return null;

        string url;
        if (!string.IsNullOrWhiteSpace(isbn) && !string.IsNullOrWhiteSpace(retailer.IsbnUrlTemplate))
        {
            url = retailer.IsbnUrlTemplate.Replace("{isbn}", Uri.EscapeDataString(isbn));
        }
        else if (!string.IsNullOrWhiteSpace(retailer.TextUrlTemplate))
        {
            var query = !string.IsNullOrWhiteSpace(isbn) ? isbn : textQuery;
            url = retailer.TextUrlTemplate.Replace("{query}", Uri.EscapeDataString(query));
        }
        else
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(retailer.AffiliateTag) && !string.IsNullOrWhiteSpace(retailer.AffiliateParam))
        {
            var separator = url.Contains('?') ? "&" : "?";
            url += $"{separator}{Uri.EscapeDataString(retailer.AffiliateParam)}={Uri.EscapeDataString(retailer.AffiliateTag)}";
        }

        return url;
    }

    private static string BuildTextQuery(string? title, string? author)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title.Trim());
        if (!string.IsNullOrWhiteSpace(author)) parts.Add(author.Trim());
        return string.Join(" ", parts);
    }
}
