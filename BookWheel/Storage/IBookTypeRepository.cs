using BookWheel.Models;

namespace BookWheel.Storage;

public interface IBookTypeRepository
{
    Task<IReadOnlyList<BookTypeRecord>> GetAllAsync();
    Task<bool> ExistsAsync(int id);
}
