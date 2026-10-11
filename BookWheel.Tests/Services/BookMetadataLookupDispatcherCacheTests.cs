using BookWheel.Models;
using BookWheel.Services;
using Microsoft.Extensions.Caching.Memory;

namespace BookWheel.Tests.Services;

public sealed class BookMetadataLookupDispatcherCacheTests
{
    private static MemoryCache NewCache() => new(new MemoryCacheOptions { SizeLimit = 2048 });

    [Fact]
    public async Task LookupByIsbnAsync_Caches_Positive_Result()
    {
        var openLibrary = new CountingProvider(isbnResult: new BookMetadataResult { Title = "Cached", ProviderId = 1 });
        var googleBooks = new CountingProvider();
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks, NewCache());

        var first = await dispatcher.LookupByIsbnAsync("9780134685991", null, CancellationToken.None);
        var second = await dispatcher.LookupByIsbnAsync("9780134685991", null, CancellationToken.None);

        Assert.Equal("Cached", first!.Title);
        Assert.Equal("Cached", second!.Title);
        Assert.Equal(1, openLibrary.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Caches_Negative_Result()
    {
        var openLibrary = new CountingProvider();
        var googleBooks = new CountingProvider();
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks, NewCache());

        Assert.Null(await dispatcher.LookupByIsbnAsync("9780134685991", null, CancellationToken.None));
        Assert.Null(await dispatcher.LookupByIsbnAsync("9780134685991", null, CancellationToken.None));

        Assert.Equal(1, openLibrary.IsbnCallCount);
        Assert.Equal(1, googleBooks.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Different_Keys_Do_Not_Collide()
    {
        var openLibrary = new CountingProvider(isbnResult: new BookMetadataResult { Title = "X", ProviderId = 1 });
        var googleBooks = new CountingProvider(isbnResult: new BookMetadataResult { Title = "Y", ProviderId = 2 });
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks, NewCache());

        await dispatcher.LookupByIsbnAsync("9780134685991", null, CancellationToken.None);
        await dispatcher.LookupByIsbnAsync("9780441013593", null, CancellationToken.None);
        var preferred = await dispatcher.LookupByIsbnAsync("9780134685991", 2, CancellationToken.None);

        Assert.Equal(2, openLibrary.IsbnCallCount);
        Assert.Equal(1, googleBooks.IsbnCallCount);
        Assert.Equal("Y", preferred!.Title);
    }

    [Fact]
    public async Task LookupByTitleAsync_Caches_Positive_Result_Case_And_Whitespace_Insensitive()
    {
        var openLibrary = new CountingProvider(titleResults: [new BookMetadataResult { Title = "Dune", ProviderId = 1 }]);
        var googleBooks = new CountingProvider();
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks, NewCache());

        var first = await dispatcher.LookupByTitleAsync("Dune", 5, null, CancellationToken.None);
        var second = await dispatcher.LookupByTitleAsync("  dune ", 5, null, CancellationToken.None);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, openLibrary.TitleCallCount);
    }

    [Fact]
    public async Task LookupByTitleAsync_Caches_Negative_Result()
    {
        var openLibrary = new CountingProvider();
        var googleBooks = new CountingProvider();
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks, NewCache());

        Assert.Empty(await dispatcher.LookupByTitleAsync("Nothing", 5, null, CancellationToken.None));
        Assert.Empty(await dispatcher.LookupByTitleAsync("Nothing", 5, null, CancellationToken.None));

        Assert.Equal(1, openLibrary.TitleCallCount);
        Assert.Equal(1, googleBooks.TitleCallCount);
    }

    [Fact]
    public async Task LookupByTitleAsync_Different_MaxResults_Do_Not_Collide()
    {
        var openLibrary = new CountingProvider(titleResults: [new BookMetadataResult { Title = "Dune", ProviderId = 1 }]);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, new CountingProvider(), NewCache());

        await dispatcher.LookupByTitleAsync("Dune", 5, null, CancellationToken.None);
        await dispatcher.LookupByTitleAsync("Dune", 10, null, CancellationToken.None);

        Assert.Equal(2, openLibrary.TitleCallCount);
    }

    private sealed class CountingProvider : IBookMetadataLookupService
    {
        private readonly BookMetadataResult? _isbnResult;
        private readonly IReadOnlyList<BookMetadataResult> _titleResults;

        public int IsbnCallCount { get; private set; }
        public int TitleCallCount { get; private set; }

        public CountingProvider(BookMetadataResult? isbnResult = null, IReadOnlyList<BookMetadataResult>? titleResults = null)
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
