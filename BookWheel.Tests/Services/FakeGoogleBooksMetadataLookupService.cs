using BookWheel.Models;
using BookWheel.Services;

namespace BookWheel.Tests.Services;

/// <summary>
/// Deterministic stand-in for <see cref="GoogleBooksBookMetadataLookupService"/> used by
/// integration tests so they never depend on reaching the real Google Books API.
/// </summary>
public sealed class FakeGoogleBooksMetadataLookupService : IBookMetadataLookupService
{
    public const string KnownIsbn = "9780441013593";
    public const string KnownIsbnTitle = "Dune";
    public const string KnownIsbnAuthor = "Frank Herbert";
    public const string KnownIsbnCoverUrl = "https://books.google.com/dune.jpg";

    public const string KnownTitle = "Neuromancer";
    public const string KnownTitleIsbn = "9780441569595";
    public const string KnownTitleAuthor = "William Gibson";
    public const string KnownTitleCoverUrl = "https://books.google.com/neuromancer.jpg";

    public Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.Equals(isbn, KnownIsbn, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BookMetadataResult?>(new BookMetadataResult
            {
                Title = KnownIsbnTitle,
                Author = KnownIsbnAuthor,
                Isbn = KnownIsbn,
                CoverUrl = KnownIsbnCoverUrl,
                ProviderId = 2
            });
        }

        return Task.FromResult<BookMetadataResult?>(null);
    }

    public Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
    {
        if (string.Equals(title, KnownTitle, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<BookMetadataResult> singleMatch =
            [
                new BookMetadataResult { Title = KnownTitle, Author = KnownTitleAuthor, Isbn = KnownTitleIsbn, CoverUrl = KnownTitleCoverUrl, ProviderId = 2 }
            ];
            return Task.FromResult(singleMatch);
        }

        return Task.FromResult<IReadOnlyList<BookMetadataResult>>(Array.Empty<BookMetadataResult>());
    }
}
