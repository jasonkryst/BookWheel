using BookWheel.Models;

namespace BookWheel.Storage;

public interface IBookRepository
{
    Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId);
    Task<BookRecord> AddAsync(Guid userId, string title);
    Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title);
    Task<BookRecord> RemoveAsync(Guid userId, Guid id);
    Task<BookRecord> SelectRandomAsync(Guid userId);
    Task<int> RemoveUserDataAsync(Guid userId);
    Task<int> GetTotalBookCountAsync();
}
