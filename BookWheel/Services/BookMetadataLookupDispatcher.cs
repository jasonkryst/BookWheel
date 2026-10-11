using BookWheel.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BookWheel.Services;

public sealed class BookMetadataLookupDispatcher
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromHours(1);

    private readonly Dictionary<int, IBookMetadataLookupService> _providers;
    private readonly IMemoryCache? _cache;

    // Cached value wrapper so a cached miss is distinguishable from "not cached".
    private sealed record CacheEntry<T>(T? Value);

    public BookMetadataLookupDispatcher(IBookMetadataLookupService openLibraryProvider, IBookMetadataLookupService googleBooksProvider, IMemoryCache? cache = null)
    {
        _cache = cache;
        _providers = new Dictionary<int, IBookMetadataLookupService>
        {
            [1] = openLibraryProvider,
            [2] = googleBooksProvider
        };
    }

    public async Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, int? preferredProviderId, CancellationToken cancellationToken)
    {
        var key = $"lookup:isbn:{preferredProviderId ?? 0}:{isbn}";
        if (_cache is not null && _cache.TryGetValue(key, out CacheEntry<BookMetadataResult>? cached) && cached is not null)
        {
            return cached.Value;
        }

        var found = await LookupByIsbnUncachedAsync(isbn, preferredProviderId, cancellationToken);
        Store(key, found, found is not null);
        return found;
    }

    public async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, int? preferredProviderId, CancellationToken cancellationToken)
    {
        var key = $"lookup:title:{preferredProviderId ?? 0}:{maxResults}:{title.Trim().ToLowerInvariant()}";
        if (_cache is not null && _cache.TryGetValue(key, out CacheEntry<IReadOnlyList<BookMetadataResult>>? cached) && cached?.Value is not null)
        {
            return cached.Value;
        }

        var found = await LookupByTitleUncachedAsync(title, maxResults, preferredProviderId, cancellationToken);
        Store<IReadOnlyList<BookMetadataResult>>(key, found, found.Count > 0);
        return found;
    }

    private void Store<T>(string key, T? value, bool positive)
    {
        _cache?.Set(key, new CacheEntry<T>(value), new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = positive ? PositiveTtl : NegativeTtl
        });
    }

    private async Task<BookMetadataResult?> LookupByIsbnUncachedAsync(string isbn, int? preferredProviderId, CancellationToken cancellationToken)
    {
        foreach (var provider in GetProviderOrder(preferredProviderId))
        {
            var result = await provider.LookupByIsbnAsync(isbn, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleUncachedAsync(string title, int maxResults, int? preferredProviderId, CancellationToken cancellationToken)
    {
        foreach (var provider in GetProviderOrder(preferredProviderId))
        {
            var results = await provider.LookupByTitleAsync(title, maxResults, cancellationToken);
            if (results.Count > 0)
            {
                return results;
            }
        }

        return [];
    }

    private IEnumerable<IBookMetadataLookupService> GetProviderOrder(int? preferredProviderId)
    {
        var preferredId = _providers.ContainsKey(preferredProviderId ?? 1) ? preferredProviderId ?? 1 : 1;
        yield return _providers[preferredId];

        foreach (var (id, provider) in _providers)
        {
            if (id != preferredId)
            {
                yield return provider;
            }
        }
    }
}
