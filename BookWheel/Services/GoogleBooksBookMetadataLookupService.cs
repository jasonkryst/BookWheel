using System.Text.Json;
using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class GoogleBooksBookMetadataLookupService : IBookMetadataLookupService
{
    private const int ProviderId = 2;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<GoogleBooksBookMetadataLookupService> _logger;

    public GoogleBooksBookMetadataLookupService(HttpClient httpClient, IOptions<BookMetadataOptions> options, ILogger<GoogleBooksBookMetadataLookupService> logger)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.GoogleBooks.ApiKey;
        _logger = logger;
    }

    public async Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = AppendApiKey($"volumes?q=isbn:{Uri.EscapeDataString(isbn)}");
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!TryGetFirstVolumeInfo(document.RootElement, out var volumeInfo))
            {
                return null;
            }

            var title = volumeInfo.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var author = ExtractAuthors(volumeInfo);
            var coverUrl = ExtractCoverUrl(volumeInfo);

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(coverUrl))
            {
                return null;
            }

            return new BookMetadataResult { Title = title, Author = author, Isbn = isbn, CoverUrl = coverUrl, ProviderId = ProviderId };
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
            var requestUri = AppendApiKey($"volumes?q=intitle:{Uri.EscapeDataString(title)}&maxResults={maxResults}");
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<BookMetadataResult>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                if (!item.TryGetProperty("volumeInfo", out var volumeInfo))
                {
                    continue;
                }

                var resultTitle = volumeInfo.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(resultTitle))
                {
                    continue;
                }

                var author = ExtractAuthors(volumeInfo);
                var isbn = ExtractBestIsbn(volumeInfo);
                var coverUrl = ExtractCoverUrl(volumeInfo);

                results.Add(new BookMetadataResult { Title = resultTitle, Author = author, Isbn = isbn, CoverUrl = coverUrl, ProviderId = ProviderId });
            }

            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Title metadata lookup failed for {Title}.", title);
            return [];
        }
    }

    private string AppendApiKey(string requestUri)
    {
        return string.IsNullOrWhiteSpace(_apiKey) ? requestUri : $"{requestUri}&key={Uri.EscapeDataString(_apiKey)}";
    }

    private static bool TryGetFirstVolumeInfo(JsonElement root, out JsonElement volumeInfo)
    {
        volumeInfo = default;
        if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array || itemsElement.GetArrayLength() == 0)
        {
            return false;
        }

        return itemsElement[0].TryGetProperty("volumeInfo", out volumeInfo);
    }

    private static string? ExtractAuthors(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("authors", out var authorsElement) || authorsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = authorsElement.EnumerateArray()
            .Select(a => a.GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? ExtractCoverUrl(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("imageLinks", out var imageLinks))
        {
            return null;
        }

        foreach (var size in new[] { "thumbnail", "smallThumbnail" })
        {
            if (imageLinks.TryGetProperty(size, out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                var url = urlElement.GetString();
                return url is null ? null : url.Replace("http://", "https://", StringComparison.Ordinal);
            }
        }

        return null;
    }

    private static string? ExtractBestIsbn(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("industryIdentifiers", out var identifiersElement) || identifiersElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var identifiers = identifiersElement.EnumerateArray()
            .Select(i => i.TryGetProperty("identifier", out var idElement) ? idElement.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        return identifiers.FirstOrDefault(id => id!.Length == 13) ?? identifiers.FirstOrDefault();
    }
}
