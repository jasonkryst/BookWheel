using BookWheel.Models;
using BookWheel.Services;

namespace BookWheel.Tests.Services;

/// <summary>
/// Deterministic stand-in for <see cref="OpenLibraryBookMetadataLookupService"/> used by
/// integration tests so they never depend on reaching the real Open Library API.
/// </summary>
public sealed class FakeBookMetadataLookupService : IBookMetadataLookupService
{
    public const string KnownIsbn = "9780134685991";
    public const string KnownIsbnTitle = "Effective Java";
    public const string KnownIsbnAuthor = "Joshua Bloch";
    public const string KnownIsbnCoverUrl = "https://covers.openlibrary.org/b/id/12345-L.jpg";

    public const string KnownTitle = "Dune";
    public const string KnownTitleIsbn = "9780441013593";
    public const string KnownTitleAuthor = "Frank Herbert";
    public const string KnownTitleCoverUrl = "https://covers.openlibrary.org/b/id/54321-L.jpg";

    public Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.Equals(isbn, KnownIsbn, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BookMetadataResult?>(new BookMetadataResult
            {
                Title = KnownIsbnTitle,
                Author = KnownIsbnAuthor,
                Isbn = KnownIsbn,
                CoverUrl = KnownIsbnCoverUrl
            });
        }

        return Task.FromResult<BookMetadataResult?>(null);
    }

    public Task<BookMetadataResult?> LookupByTitleAsync(string title, CancellationToken cancellationToken)
    {
        if (string.Equals(title, KnownTitle, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BookMetadataResult?>(new BookMetadataResult
            {
                Title = KnownTitle,
                Author = KnownTitleAuthor,
                Isbn = KnownTitleIsbn,
                CoverUrl = KnownTitleCoverUrl
            });
        }

        return Task.FromResult<BookMetadataResult?>(null);
    }
}
