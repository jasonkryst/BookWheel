using BookWheel.Models;

namespace BookWheel.Storage;

public interface ISpinHistoryRepository
{
    Task RecordAsync(Guid userId, Guid bookId, DateTimeOffset selectedAtUtc);
    Task<IReadOnlyList<SpinHistoryRecord>> GetForUserAsync(Guid userId);
    Task<int> RemoveUserDataAsync(Guid userId);
}
