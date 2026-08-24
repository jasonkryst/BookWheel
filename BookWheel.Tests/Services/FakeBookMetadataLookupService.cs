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

    public const string AmbiguousTitle = "Foundation";
    public const string AmbiguousTitleFirstAuthor = "Isaac Asimov";
    public const string AmbiguousTitleSecondAuthor = "Someone Else";
    public const string AmbiguousTitleThirdAuthor = "A Third Author";

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

    public Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
    {
        if (string.Equals(title, KnownTitle, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<BookMetadataResult> singleMatch =
            [
                new BookMetadataResult { Title = KnownTitle, Author = KnownTitleAuthor, Isbn = KnownTitleIsbn, CoverUrl = KnownTitleCoverUrl }
            ];
            return Task.FromResult(singleMatch);
        }

        if (string.Equals(title, AmbiguousTitle, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<BookMetadataResult> candidates =
            [
                new BookMetadataResult { Title = AmbiguousTitle, Author = AmbiguousTitleFirstAuthor, Isbn = "9780553293357", CoverUrl = "https://covers.openlibrary.org/b/id/11111-L.jpg" },
                new BookMetadataResult { Title = AmbiguousTitle, Author = AmbiguousTitleSecondAuthor, Isbn = "9780000000002", CoverUrl = "https://covers.openlibrary.org/b/id/22222-L.jpg" },
                new BookMetadataResult { Title = AmbiguousTitle, Author = AmbiguousTitleThirdAuthor, Isbn = "9780000000003", CoverUrl = null }
            ];
            return Task.FromResult<IReadOnlyList<BookMetadataResult>>(candidates.Take(maxResults).ToList());
        }

        return Task.FromResult<IReadOnlyList<BookMetadataResult>>(Array.Empty<BookMetadataResult>());
    }
}
