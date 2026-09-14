using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class BookSearchLinksServiceTests
{
    private readonly BookSearchLinksService _sut = new();

    // ── ISBN-based links ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_GoogleBooks_UsesIsbnVid(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://books.google.com/books?vid=ISBN{isbn}", links.GoogleBooks);
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_OpenLibrary_UsesIsbnPath(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://openlibrary.org/isbn/{isbn}", links.OpenLibrary);
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_Goodreads_UsesIsbn(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://www.goodreads.com/search?q={isbn}", links.Goodreads);
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_BarnesAndNoble_UsesIsbn(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://www.barnesandnoble.com/s/{isbn}", links.BarnesAndNoble);
    }

    [Theory]
    [InlineData("9780132350884")]
    [InlineData("0201558025")]
    public void BuildLinks_WithIsbn_Amazon_UsesIsbn(string isbn)
    {
        var links = _sut.BuildLinks(isbn, title: null, author: null);

        Assert.Equal($"https://www.amazon.com/s?k={isbn}", links.Amazon);
    }

    // ── Title + author text-search links ─────────────────────────────────────

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_GoogleBooks_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://books.google.com/books?q=Clean%20Code%20Robert%20C.%20Martin", links.GoogleBooks);
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_OpenLibrary_UsesSeparateParams()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://openlibrary.org/search?q=Clean%20Code&author=Robert%20C.%20Martin", links.OpenLibrary);
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_Goodreads_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.goodreads.com/search?q=Clean%20Code%20Robert%20C.%20Martin", links.Goodreads);
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_BarnesAndNoble_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.barnesandnoble.com/s/Clean%20Code%20Robert%20C.%20Martin", links.BarnesAndNoble);
    }

    [Fact]
    public void BuildLinks_WithTitleAndAuthor_Amazon_UsesCombinedQuery()
    {
        var links = _sut.BuildLinks(isbn: null, "Clean Code", "Robert C. Martin");

        Assert.Equal("https://www.amazon.com/s?k=Clean%20Code%20Robert%20C.%20Martin", links.Amazon);
    }

    // ── Title-only ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildLinks_WithTitleOnly_OpenLibrary_OmitsAuthorParam()
    {
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null);

        Assert.Equal("https://openlibrary.org/search?q=Foundation", links.OpenLibrary);
        Assert.DoesNotContain("author=", links.OpenLibrary);
    }

    [Fact]
    public void BuildLinks_WithTitleOnly_GoogleBooks_UsesTitleOnly()
    {
        var links = _sut.BuildLinks(isbn: null, "Foundation", author: null);

        Assert.Equal("https://books.google.com/books?q=Foundation", links.GoogleBooks);
    }

    // ── ISBN takes priority over title/author ────────────────────────────────

    [Fact]
    public void BuildLinks_WithIsbnAndTitle_GoogleBooks_PrefersIsbn()
    {
        var links = _sut.BuildLinks("9780132350884", "Clean Code", "Robert C. Martin");

        Assert.Equal("https://books.google.com/books?vid=ISBN9780132350884", links.GoogleBooks);
    }

    [Fact]
    public void BuildLinks_WithIsbnAndTitle_OpenLibrary_PrefersIsbn()
    {
        var links = _sut.BuildLinks("9780132350884", "Clean Code", "Robert C. Martin");

        Assert.Equal("https://openlibrary.org/isbn/9780132350884", links.OpenLibrary);
    }

    // ── Special characters are URL-encoded ───────────────────────────────────

    [Fact]
    public void BuildLinks_WithTitleContainingSpecialChars_UrlEncodesTitle()
    {
        var links = _sut.BuildLinks(isbn: null, "C# in Depth", author: null);

        Assert.Contains("C%23%20in%20Depth", links.GoogleBooks);
        Assert.Contains("C%23%20in%20Depth", links.Goodreads);
    }

    // ── Whitespace is trimmed ─────────────────────────────────────────────────

    [Fact]
    public void BuildLinks_WithPaddedTitle_TrimsBeforeEncoding()
    {
        var links = _sut.BuildLinks(isbn: null, "  Foundation  ", "  Isaac Asimov  ");

        Assert.Equal("https://books.google.com/books?q=Foundation%20Isaac%20Asimov", links.GoogleBooks);
    }

    // ── Null/empty inputs produce safe fallback URLs ──────────────────────────

    [Fact]
    public void BuildLinks_WithAllNullInputs_ReturnsEmptyQueryUrls()
    {
        var links = _sut.BuildLinks(isbn: null, title: null, author: null);

        Assert.StartsWith("https://books.google.com/books?q=", links.GoogleBooks);
        Assert.StartsWith("https://openlibrary.org/search?q=", links.OpenLibrary);
        Assert.StartsWith("https://www.goodreads.com/search?q=", links.Goodreads);
        Assert.StartsWith("https://www.barnesandnoble.com/s/", links.BarnesAndNoble);
        Assert.StartsWith("https://www.amazon.com/s?k=", links.Amazon);
    }

    [Fact]
    public void BuildLinks_WithEmptyStringIsbn_FallsBackToTextSearch()
    {
        var links = _sut.BuildLinks(isbn: "", "Foundation", author: null);

        Assert.Equal("https://books.google.com/books?q=Foundation", links.GoogleBooks);
        Assert.DoesNotContain("vid=ISBN", links.GoogleBooks);
    }

    [Fact]
    public void BuildLinks_WithWhitespaceOnlyIsbn_FallsBackToTextSearch()
    {
        var links = _sut.BuildLinks(isbn: "   ", "Foundation", author: null);

        Assert.DoesNotContain("vid=ISBN", links.GoogleBooks);
    }
}
