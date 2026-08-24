using BookWheel.Models;

namespace BookWheel.Services;

public interface IBookMetadataLookupService
{
    Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken);
    Task<BookMetadataResult?> LookupByTitleAsync(string title, CancellationToken cancellationToken);
}
