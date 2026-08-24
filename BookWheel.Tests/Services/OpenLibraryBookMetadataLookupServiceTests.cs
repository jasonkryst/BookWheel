using System.Net;
using BookWheel.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookWheel.Tests.Services;

public sealed class OpenLibraryBookMetadataLookupServiceTests
{
    [Fact]
    public async Task LookupByIsbnAsync_Returns_Metadata_When_Book_Is_Found()
    {
        const string responseJson = """
        {
          "ISBN:9780134685991": {
            "title": "Effective Java",
            "authors": [{ "name": "Joshua Bloch" }],
            "cover": { "small": "small.jpg", "medium": "medium.jpg", "large": "large.jpg" }
          }
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Effective Java", result!.Title);
        Assert.Equal("Joshua Bloch", result.Author);
        Assert.Equal("large.jpg", result.CoverUrl);
        Assert.Equal("9780134685991", result.Isbn);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Joins_Multiple_Authors()
    {
        const string responseJson = """
        {
          "ISBN:9780134685991": {
            "title": "Effective Java",
            "authors": [{ "name": "Joshua Bloch" }, { "name": "Someone Else" }]
          }
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Equal("Joshua Bloch, Someone Else", result!.Author);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_When_Isbn_Is_Not_Found()
    {
        var service = CreateService("{}");

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_On_Http_Error_Status()
    {
        var service = CreateService("{}", HttpStatusCode.InternalServerError);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_On_Malformed_Json()
    {
        var service = CreateService("{ not valid json");

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_On_Network_Failure()
    {
        var service = CreateFailingService();

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Metadata_For_Best_Match()
    {
        const string responseJson = """
        {
          "docs": [
            {
              "title": "Effective Java",
              "author_name": ["Joshua Bloch"],
              "isbn": ["0134685997", "9780134685991"],
              "cover_i": 12345
            }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByTitleAsync("Effective Java", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Effective Java", result!.Title);
        Assert.Equal("Joshua Bloch", result.Author);
        Assert.Equal("9780134685991", result.Isbn);
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", result.CoverUrl);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Null_When_No_Docs_Match()
    {
        var service = CreateService("""{ "docs": [] }""");

        var result = await service.LookupByTitleAsync("Some Nonexistent Title Xyz", CancellationToken.None);

        Assert.Null(result);
    }

    private static OpenLibraryBookMetadataLookupService CreateService(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson)
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org/") };
        return new OpenLibraryBookMetadataLookupService(httpClient, NullLogger<OpenLibraryBookMetadataLookupService>.Instance);
    }

    private static OpenLibraryBookMetadataLookupService CreateFailingService()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Simulated network failure."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org/") };
        return new OpenLibraryBookMetadataLookupService(httpClient, NullLogger<OpenLibraryBookMetadataLookupService>.Instance);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request, cancellationToken));
        }
    }
}
