using BookWheel.Models;

namespace BookWheel.Services;

public sealed class BookMetadataLookupDispatcher
{
    private readonly Dictionary<int, IBookMetadataLookupService> _providers;

    public BookMetadataLookupDispatcher(IBookMetadataLookupService openLibraryProvider, IBookMetadataLookupService googleBooksProvider)
    {
        _providers = new Dictionary<int, IBookMetadataLookupService>
        {
            [1] = openLibraryProvider,
            [2] = googleBooksProvider
        };
    }

    public async Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, int? preferredProviderId, CancellationToken cancellationToken)
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

    public async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, int? preferredProviderId, CancellationToken cancellationToken)
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
