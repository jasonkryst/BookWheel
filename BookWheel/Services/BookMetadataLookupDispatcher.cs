using BookWheel.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BookWheel.Services;

public sealed class BookMetadataLookupDispatcher
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(7);

    private readonly Dictionary<int, IBookMetadataLookupService> _providers;
    private readonly IMemoryCache? _cache;

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
        if (_cache is not null && _cache.TryGetValue(key, out BookMetadataResult? cached) && cached is not null)
        {
            return cached;
        }

        var found = await LookupByIsbnUncachedAsync(isbn, preferredProviderId, cancellationToken);
        CachePositive(key, found);
        return found;
    }

    public async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, int? preferredProviderId, CancellationToken cancellationToken)
    {
        var key = $"lookup:title:{preferredProviderId ?? 0}:{maxResults}:{title.Trim().ToLowerInvariant()}";
        if (_cache is not null && _cache.TryGetValue(key, out IReadOnlyList<BookMetadataResult>? cached) && cached is not null)
        {
            return cached;
        }

        var found = await LookupByTitleUncachedAsync(title, maxResults, preferredProviderId, cancellationToken);
        if (found.Count > 0)
        {
            CachePositive(key, found);
        }
        return found;
    }

    // Cache positive results only. A provider returns null / an empty list for BOTH a genuine
    // no-match AND a transient failure (timeout, 5xx, network error — see each provider's catch),
    // and the cache is shared across all users, so negative-caching would let one caller's
    // momentary failure mark a real book "not found" for an hour for everyone. Repeated misses
    // are instead bounded by the per-user lookup rate limit (see Program.cs).
    private void CachePositive<T>(string key, T? value) where T : class
    {
        if (value is null)
        {
            return;
        }

        _cache?.Set(key, value, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = PositiveTtl
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
