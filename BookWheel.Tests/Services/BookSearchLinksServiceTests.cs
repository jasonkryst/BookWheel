using BookWheel.Models;
using BookWheel.Services;
using Microsoft.Extensions.Options;

namespace BookWheel.Tests.Services;

public sealed class BookSearchLinksServiceTests
{
    private readonly BookSearchLinksService _sut = new(Options.Create(new RetailerLinksOptions()));

    private static string LinkFor(List<BookSearchLink> links, string name) =>
        Assert.Single(links, l => l.Name == name).Url;

    // ── ISBN-based links (default config, "en" locale) ─────────────────────────

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_GoogleBooks_UsesIsbnVid(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://books.google.com/books?vid=ISBN{isbn}", LinkFor(links, "Google Books"));
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_OpenLibrary_UsesIsbnPath(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://openlibrary.org/isbn/{isbn}", LinkFor(links, "Open Library"));
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_Goodreads_UsesIsbn(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://www.goodreads.com/search?q={isbn}", LinkFor(links, "Goodreads"));
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_BarnesAndNoble_UsesIsbn(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://www.barnesandnoble.com/s/{isbn}", LinkFor(links, "Barnes & Noble"));
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_Amazon_UsesIsbn(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://www.amazon.com/s?k={isbn}", LinkFor(links, "Amazon"));
    }

    // ── Title + author text-search links ─────────────────────────────────────

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_GoogleBooks_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://books.google.com/books?q=Clean%20Code%20Robert%20C.%20Martin", LinkFor(links, "Google Books"));
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_OpenLibrary_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://openlibrary.org/search?q=Clean%20Code%20Robert%20C.%20Martin", LinkFor(links, "Open Library"));
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_Goodreads_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.goodreads.com/search?q=Clean%20Code%20Robert%20C.%20Martin", LinkFor(links, "Goodreads"));
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_BarnesAndNoble_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.barnesandnoble.com/s/Clean%20Code%20Robert%20C.%20Martin", LinkFor(links, "Barnes & Noble"));
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_Amazon_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.amazon.com/s?k=Clean%20Code%20Robert%20C.%20Martin", LinkFor(links, "Amazon"));
    }

    // ── ISBN takes priority over title/author ────────────────────────────────

    [Fact]
    public void BuildLinks_WithIsbnAndTitle_GoogleBooks_PrefersIsbn()
    {
        var links = _sut.BuildLinks("9780132350884", "Clean Code", "Robert C. Martin");

        Assert.Equal("https://books.google.com/books?vid=ISBN9780132350884", LinkFor(links, "Google Books"));
    }

    [Fact]
    public void BuildLinks_WithIsbnAndTitle_OpenLibrary_PrefersIsbn()
    {
        var links = _sut.BuildLinks("9780132350884", "Clean Code", "Robert C. Martin");

        Assert.Equal("https://openlibrary.org/isbn/9780132350884", LinkFor(links, "Open Library"));
    }

    [Fact]
    public void BuildLinks_WithIsbnAndTitle_Goodreads_PrefersIsbnOverTextQuery()
    {
        // Negative: Goodreads has no dedicated ISBN template, but the ISBN
        // must still win as the search term over the title/author text.
        var links = _sut.BuildLinks("9780132350884", "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.goodreads.com/search?q=9780132350884", LinkFor(links, "Goodreads"));
    }

    // ── Special characters are URL-encoded ───────────────────────────────────

    [Fact]
    public void BuildLinks_WithTitleContainingSpecialChars_UrlEncodesTitle()
    {
        var links = _sut.BuildLinks(isbn: null, "C# in Depth", author: null);

        Assert.Contains("C%23%20in%20Depth", LinkFor(links, "Google Books"));
        Assert.Contains("C%23%20in%20Depth", LinkFor(links, "Goodreads"));
    }

    // ── Whitespace is trimmed ─────────────────────────────────────────────────

    [Fact]
    public void BuildLinks_WithPaddedTitle_TrimsBeforeEncoding()
    {
        var links = _sut.BuildLinks(isbn: null, "  Foundation  ", "  Isaac Asimov  ");

        Assert.Equal("https://books.google.com/books?q=Foundation%20Isaac%20Asimov", LinkFor(links, "Google Books"));
    }

    // ── Null/empty inputs produce safe fallback URLs ──────────────────────────

    [Fact]
    public void BuildLinks_WithAllNullInputs_ReturnsEmptyQueryUrlsForAllFiveDefaultRetailers()
    {
        var links = _sut.BuildLinks(isbn: null, title: null, author: null);

        Assert.StartsWith("https://books.google.com/books?q=", LinkFor(links, "Google Books"));
        Assert.StartsWith("https://openlibrary.org/search?q=", LinkFor(links, "Open Library"));
        Assert.StartsWith("https://www.goodreads.com/search?q=", LinkFor(links, "Goodreads"));
        Assert.StartsWith("https://www.barnesandnoble.com/s/", LinkFor(links, "Barnes & Noble"));
        Assert.StartsWith("https://www.amazon.com/s?k=", LinkFor(links, "Amazon"));
        Assert.Equal(5, links.Count);
    }

    [Fact]
    public void BuildLinks_WithEmptyStringIsbn_FallsBackToTextSearch()
    {
        var links = _sut.BuildLinks(isbn: "", "Foundation", author: null);

        Assert.Equal("https://books.google.com/books?q=Foundation", LinkFor(links, "Google Books"));
        Assert.DoesNotContain("vid=ISBN", LinkFor(links, "Google Books"));
    }

    [Fact]
    public void BuildLinks_WithWhitespaceOnlyIsbn_FallsBackToTextSearch()
    {
        var links = _sut.BuildLinks(isbn: "   ", "Foundation", author: null);

        Assert.DoesNotContain("vid=ISBN", LinkFor(links, "Google Books"));
    }

    // ── GH #208: locale picks the regional store ────────────────────────────

    [Theory]
    [InlineData("en", "https://www.amazon.com/s?k=Foundation")]
    [InlineData("es", "https://www.amazon.es/s?k=Foundation")]
    [InlineData("pl", "https://www.amazon.pl/s?k=Foundation")]
    public void BuildLinks_PicksTheRegionalAmazonDomainForTheRequestLocale(string locale, string expectedUrl)
    {
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null, locale);

        Assert.Equal(expectedUrl, LinkFor(links, "Amazon"));
        // Negative: only one Amazon entry is returned per request, not all three locale variants.
        Assert.Single(links, l => l.Name == "Amazon");
    }

    [Fact]
    public void BuildLinks_ForEnglishLocale_ShowsBarnesAndNoble_NotBookshop()
    {
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null, "en");

        Assert.Contains(links, l => l.Name == "Barnes & Noble");
        Assert.DoesNotContain(links, l => l.Name == "Bookshop.org");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("pl")]
    public void BuildLinks_ForSpanishOrPolishLocale_ShowsBookshop_NotBarnesAndNoble(string locale)
    {
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null, locale);

        Assert.Contains(links, l => l.Name == "Bookshop.org");
        Assert.DoesNotContain(links, l => l.Name == "Barnes & Noble");
    }

    [Fact]
    public void BuildLinks_LocaleMatchingIsCaseInsensitive()
    {
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null, "ES");

        Assert.Equal("https://www.amazon.es/s?k=Foundation", LinkFor(links, "Amazon"));
    }

    // ── GH #208: retailers are configurable (enabled flag) ──────────────────

    [Fact]
    public void BuildLinks_DisabledRetailer_IsOmitted()
    {
        var options = new RetailerLinksOptions
        {
            Retailers =
            [
                new RetailerOptions { Name = "Google Books", TextUrlTemplate = "https://books.google.com/books?q={query}", Enabled = true },
                new RetailerOptions { Name = "Amazon", TextUrlTemplate = "https://www.amazon.com/s?k={query}", Enabled = false }
            ]
        };
        var sut = new BookSearchLinksService(Options.Create(options));

        var links = sut.BuildLinks(isbn: null, "Foundation", author: null);

        Assert.Single(links);
        Assert.DoesNotContain(links, l => l.Name == "Amazon");
    }

    [Fact]
    public void BuildLinks_WithNoConfiguredRetailers_ReturnsEmptyList()
    {
        var sut = new BookSearchLinksService(Options.Create(new RetailerLinksOptions { Retailers = [] }));

        var links = sut.BuildLinks(isbn: null, "Foundation", author: null);

        Assert.Empty(links);
    }

    // ── GH #208: affiliate tag/disclosure scaffolding ───────────────────────

    [Fact]
    public void BuildLinks_WithoutAffiliateTagConfigured_IsNotMarkedAffiliate()
    {
        // Positive: the shipped defaults have no affiliate tags configured
        // (scaffolding only, per design) — no link should ever claim to be
        // an affiliate link out of the box.
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null);

        Assert.All(links, l => Assert.False(l.IsAffiliate));
    }

    [Fact]
    public void BuildLinks_WithAffiliateTagConfigured_MarksLinkAffiliateAndAppendsParam()
    {
        var options = new RetailerLinksOptions
        {
            Retailers =
            [
                new RetailerOptions
                {
                    Name = "Amazon",
                    TextUrlTemplate = "https://www.amazon.com/s?k={query}",
                    AffiliateTag = "my-tag-20",
                    AffiliateParam = "tag"
                }
            ]
        };
        var sut = new BookSearchLinksService(Options.Create(options));

        var links = sut.BuildLinks(isbn: null, "Foundation", author: null);
        var amazon = Assert.Single(links);

        Assert.True(amazon.IsAffiliate);
        Assert.Equal("https://www.amazon.com/s?k=Foundation&tag=my-tag-20", amazon.Url);
    }

    [Fact]
    public void BuildLinks_WithAffiliateTagButNoAffiliateParam_IsNotMarkedAffiliate()
    {
        // Negative: a tag alone, with no query-param name to attach it under,
        // can't actually be wired into the URL — must not be falsely
        // disclosed as an affiliate link in that half-configured state.
        var options = new RetailerLinksOptions
        {
            Retailers =
            [
                new RetailerOptions
                {
                    Name = "Amazon",
                    TextUrlTemplate = "https://www.amazon.com/s?k={query}",
                    AffiliateTag = "my-tag-20",
                    AffiliateParam = null
                }
            ]
        };
        var sut = new BookSearchLinksService(Options.Create(options));

        var links = sut.BuildLinks(isbn: null, "Foundation", author: null);
        var amazon = Assert.Single(links);

        Assert.False(amazon.IsAffiliate);
        Assert.DoesNotContain("tag=", amazon.Url);
    }

    [Fact]
    public void BuildLinks_AffiliateParam_AppendsWithQuestionMarkWhenUrlHasNoExistingQueryString()
    {
        var options = new RetailerLinksOptions
        {
            Retailers =
            [
                new RetailerOptions
                {
                    Name = "Barnes & Noble",
                    TextUrlTemplate = "https://www.barnesandnoble.com/s/{query}",
                    AffiliateTag = "abc123",
                    AffiliateParam = "ref"
                }
            ]
        };
        var sut = new BookSearchLinksService(Options.Create(options));

        var links = sut.BuildLinks(isbn: null, "Foundation", author: null);

        Assert.Equal("https://www.barnesandnoble.com/s/Foundation?ref=abc123", Assert.Single(links).Url);
    }
}
