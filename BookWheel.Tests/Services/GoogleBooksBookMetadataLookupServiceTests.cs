using System.Net;
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookWheel.Tests.Services;

public sealed class GoogleBooksBookMetadataLookupServiceTests
{
    [Fact]
    public async Task LookupByIsbnAsync_Returns_Metadata_When_Book_Is_Found()
    {
        const string responseJson = """
        {
          "items": [
            {
              "volumeInfo": {
                "title": "Effective Java",
                "authors": ["Joshua Bloch"],
                "imageLinks": { "thumbnail": "http://books.google.com/thumb.jpg" }
              }
            }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Effective Java", result!.Title);
        Assert.Equal("Joshua Bloch", result.Author);
        Assert.Equal("9780134685991", result.Isbn);
        Assert.Equal("https://books.google.com/thumb.jpg", result.CoverUrl);
        Assert.Equal(2, result.ProviderId);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Joins_Multiple_Authors()
    {
        const string responseJson = """
        {
          "items": [
            { "volumeInfo": { "title": "Effective Java", "authors": ["Joshua Bloch", "Someone Else"] } }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Equal("Joshua Bloch, Someone Else", result!.Author);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_When_No_Items_Match()
    {
        var service = CreateService("""{ "totalItems": 0 }""");

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
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Simulated network failure."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var service = new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Matches_With_Best_Isbn_Preferring_Isbn13()
    {
        const string responseJson = """
        {
          "items": [
            {
              "volumeInfo": {
                "title": "Foundation",
                "authors": ["Isaac Asimov"],
                "industryIdentifiers": [
                  { "type": "ISBN_10", "identifier": "0553293354" },
                  { "type": "ISBN_13", "identifier": "9780553293357" }
                ],
                "imageLinks": { "thumbnail": "http://books.google.com/foundation.jpg" }
              }
            }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var results = await service.LookupByTitleAsync("Foundation", 10, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Foundation", result.Title);
        Assert.Equal("Isaac Asimov", result.Author);
        Assert.Equal("9780553293357", result.Isbn);
        Assert.Equal("https://books.google.com/foundation.jpg", result.CoverUrl);
        Assert.Equal(2, result.ProviderId);
    }

    [Fact]
    public async Task LookupByTitleAsync_Caps_Results_At_MaxResults_Even_If_The_Api_Returns_More()
    {
        const string responseJson = """
        {
          "items": [
            { "volumeInfo": { "title": "Foundation", "authors": ["Author One"] } },
            { "volumeInfo": { "title": "Foundation", "authors": ["Author Two"] } },
            { "volumeInfo": { "title": "Foundation", "authors": ["Author Three"] } }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var results = await service.LookupByTitleAsync("Foundation", 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Empty_When_No_Items_Match()
    {
        var service = CreateService("""{ "totalItems": 0 }""");

        var results = await service.LookupByTitleAsync("Some Nonexistent Title Xyz", 10, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task LookupByTitleAsync_Passes_MaxResults_As_The_MaxResults_Query_Parameter()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "items": [] }""") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var service = new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        await service.LookupByTitleAsync("Foundation", 7, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains("maxResults=7", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_Append_Api_Key_When_Configured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "items": [] }""") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var options = Options.Create(new BookMetadataOptions { GoogleBooks = new GoogleBooksOptions { ApiKey = "test-key" } });
        var service = new GoogleBooksBookMetadataLookupService(httpClient, options, NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains("key=test-key", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_Omit_Key_Parameter_When_Not_Configured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "items": [] }""") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var service = new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.DoesNotContain("key=", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    private static GoogleBooksBookMetadataLookupService CreateService(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson)
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        return new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);
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
