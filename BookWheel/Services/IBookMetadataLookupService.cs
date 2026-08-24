using BookWheel.Models;

namespace BookWheel.Services;

public interface IBookMetadataLookupService
{
    Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken);
    Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken);
}
