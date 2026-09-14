using BookWheel.Models;

namespace BookWheel.Services;

public sealed class BookSearchLinksService
{
    public BookSearchLinks BuildLinks(string? isbn, string? title, string? author)
    {
        return new BookSearchLinks
        {
            GoogleBooks = BuildGoogleBooksUrl(isbn, title, author),
            OpenLibrary = BuildOpenLibraryUrl(isbn, title, author),
            Goodreads = BuildGoodreadsUrl(isbn, title, author),
            BarnesAndNoble = BuildBarnesAndNobleUrl(isbn, title, author),
            Amazon = BuildAmazonUrl(isbn, title, author)
        };
    }

    private static string BuildGoogleBooksUrl(string? isbn, string? title, string? author)
    {
        if (!string.IsNullOrWhiteSpace(isbn))
            return $"https://books.google.com/books?vid=ISBN{Uri.EscapeDataString(isbn)}";

        return $"https://books.google.com/books?q={Uri.EscapeDataString(BuildTextQuery(title, author))}";
    }

    private static string BuildOpenLibraryUrl(string? isbn, string? title, string? author)
    {
        if (!string.IsNullOrWhiteSpace(isbn))
            return $"https://openlibrary.org/isbn/{Uri.EscapeDataString(isbn)}";

        var url = $"https://openlibrary.org/search?q={Uri.EscapeDataString(title?.Trim() ?? string.Empty)}";
        if (!string.IsNullOrWhiteSpace(author))
            url += $"&author={Uri.EscapeDataString(author.Trim())}";
        return url;
    }

    private static string BuildGoodreadsUrl(string? isbn, string? title, string? author)
    {
        var query = !string.IsNullOrWhiteSpace(isbn) ? isbn : BuildTextQuery(title, author);
        return $"https://www.goodreads.com/search?q={Uri.EscapeDataString(query)}";
    }

    private static string BuildBarnesAndNobleUrl(string? isbn, string? title, string? author)
    {
        if (!string.IsNullOrWhiteSpace(isbn))
            return $"https://www.barnesandnoble.com/s/{Uri.EscapeDataString(isbn)}";

        return $"https://www.barnesandnoble.com/s/{Uri.EscapeDataString(BuildTextQuery(title, author))}";
    }

    private static string BuildAmazonUrl(string? isbn, string? title, string? author)
    {
        var query = !string.IsNullOrWhiteSpace(isbn) ? isbn : BuildTextQuery(title, author);
        return $"https://www.amazon.com/s?k={Uri.EscapeDataString(query)}";
    }

    private static string BuildTextQuery(string? title, string? author)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title.Trim());
        if (!string.IsNullOrWhiteSpace(author)) parts.Add(author.Trim());
        return string.Join(" ", parts);
    }
}
