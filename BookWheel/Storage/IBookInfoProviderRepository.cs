using BookWheel.Models;

namespace BookWheel.Storage;

public interface IBookInfoProviderRepository
{
    Task<IReadOnlyList<BookInfoProviderRecord>> GetAllAsync();
    Task<bool> ExistsAsync(int id);
}
