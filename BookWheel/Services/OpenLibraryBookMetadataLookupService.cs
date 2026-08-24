using System.Text.Json;
using BookWheel.Models;

namespace BookWheel.Services;

public sealed class OpenLibraryBookMetadataLookupService : IBookMetadataLookupService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenLibraryBookMetadataLookupService> _logger;

    public OpenLibraryBookMetadataLookupService(HttpClient httpClient, ILogger<OpenLibraryBookMetadataLookupService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"api/books?bibkeys=ISBN:{Uri.EscapeDataString(isbn)}&format=json&jscmd=data";
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty($"ISBN:{isbn}", out var bookElement))
            {
                return null;
            }

            var title = bookElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var author = ExtractAuthors(bookElement);
            var coverUrl = ExtractCoverUrl(bookElement);

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(coverUrl))
            {
                return null;
            }

            return new BookMetadataResult { Title = title, Author = author, Isbn = isbn, CoverUrl = coverUrl };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "ISBN metadata lookup failed for {Isbn}.", isbn);
            return null;
        }
    }

    public async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"search.json?q={Uri.EscapeDataString(title)}&limit={maxResults}";
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("docs", out var docsElement) || docsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<BookMetadataResult>();
            foreach (var doc in docsElement.EnumerateArray())
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                var resultTitle = doc.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(resultTitle))
                {
                    continue;
                }

                var author = ExtractSearchAuthors(doc);
                var isbn = ExtractBestIsbn(doc);
                var coverUrl = ExtractSearchCoverUrl(doc);

                results.Add(new BookMetadataResult { Title = resultTitle, Author = author, Isbn = isbn, CoverUrl = coverUrl });
            }

            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Title metadata lookup failed for {Title}.", title);
            return [];
        }
    }

    private static string? ExtractAuthors(JsonElement bookElement)
    {
        if (!bookElement.TryGetProperty("authors", out var authorsElement) || authorsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = authorsElement.EnumerateArray()
            .Select(a => a.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? ExtractCoverUrl(JsonElement bookElement)
    {
        if (!bookElement.TryGetProperty("cover", out var coverElement))
        {
            return null;
        }

        foreach (var size in new[] { "large", "medium", "small" })
        {
            if (coverElement.TryGetProperty(size, out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                return urlElement.GetString();
            }
        }

        return null;
    }

    private static string? ExtractSearchAuthors(JsonElement doc)
    {
        if (!doc.TryGetProperty("author_name", out var authorNameElement) || authorNameElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = authorNameElement.EnumerateArray()
            .Select(x => x.GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? ExtractSearchCoverUrl(JsonElement doc)
    {
        return doc.TryGetProperty("cover_i", out var coverIdElement) && coverIdElement.ValueKind == JsonValueKind.Number
            ? $"https://covers.openlibrary.org/b/id/{coverIdElement.GetInt64()}-L.jpg"
            : null;
    }

    private static string? ExtractBestIsbn(JsonElement doc)
    {
        if (!doc.TryGetProperty("isbn", out var isbnElement) || isbnElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var isbns = isbnElement.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return isbns.FirstOrDefault(x => x!.Length == 13) ?? isbns.FirstOrDefault();
    }
}
