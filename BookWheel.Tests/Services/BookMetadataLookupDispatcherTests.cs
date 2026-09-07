using BookWheel.Models;
using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class BookMetadataLookupDispatcherTests
{
    [Fact]
    public async Task LookupByIsbnAsync_Uses_Preferred_Provider_When_It_Succeeds()
    {
        var openLibrary = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Open Library", ProviderId = 1 });
        var googleBooks = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Google Books", ProviderId = 2 });
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: 2, CancellationToken.None);

        Assert.Equal("From Google Books", result!.Title);
        Assert.Equal(2, result.ProviderId);
        Assert.Equal(1, googleBooks.IsbnCallCount);
        Assert.Equal(0, openLibrary.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Defaults_To_Provider_One_When_No_Preference_Set()
    {
        var openLibrary = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Open Library", ProviderId = 1 });
        var googleBooks = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Google Books", ProviderId = 2 });
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: null, CancellationToken.None);

        Assert.Equal("From Open Library", result!.Title);
        Assert.Equal(1, openLibrary.IsbnCallCount);
        Assert.Equal(0, googleBooks.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Falls_Back_When_Preferred_Provider_Returns_Null()
    {
        var openLibrary = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Open Library", ProviderId = 1 });
        var googleBooks = new StubProvider(isbnResult: null);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: 2, CancellationToken.None);

        Assert.Equal("From Open Library", result!.Title);
        Assert.Equal(1, googleBooks.IsbnCallCount);
        Assert.Equal(1, openLibrary.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_When_Both_Providers_Return_Null()
    {
        var openLibrary = new StubProvider(isbnResult: null);
        var googleBooks = new StubProvider(isbnResult: null);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: 1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByTitleAsync_Falls_Back_When_Preferred_Provider_Returns_Empty()
    {
        IReadOnlyList<BookMetadataResult> fallbackResults = [new BookMetadataResult { Title = "From Google Books", ProviderId = 2 }];
        var openLibrary = new StubProvider(titleResults: []);
        var googleBooks = new StubProvider(titleResults: fallbackResults);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var results = await dispatcher.LookupByTitleAsync("Foundation", 10, preferredProviderId: 1, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("From Google Books", result.Title);
        Assert.Equal(1, openLibrary.TitleCallCount);
        Assert.Equal(1, googleBooks.TitleCallCount);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Empty_When_Both_Providers_Return_Empty()
    {
        var openLibrary = new StubProvider(titleResults: []);
        var googleBooks = new StubProvider(titleResults: []);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var results = await dispatcher.LookupByTitleAsync("Foundation", 10, preferredProviderId: null, CancellationToken.None);

        Assert.Empty(results);
    }

    private sealed class StubProvider : IBookMetadataLookupService
    {
        private readonly BookMetadataResult? _isbnResult;
        private readonly IReadOnlyList<BookMetadataResult> _titleResults;

        public int IsbnCallCount { get; private set; }
        public int TitleCallCount { get; private set; }

        public StubProvider(BookMetadataResult? isbnResult = null, IReadOnlyList<BookMetadataResult>? titleResults = null)
        {
            _isbnResult = isbnResult;
            _titleResults = titleResults ?? [];
        }

        public Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
        {
            IsbnCallCount++;
            return Task.FromResult(_isbnResult);
        }

        public Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
        {
            TitleCallCount++;
            return Task.FromResult(_titleResults);
        }
    }
}
