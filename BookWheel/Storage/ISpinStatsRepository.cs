using BookWheel.Models;

namespace BookWheel.Storage;

public interface ISpinStatsRepository
{
    Task<SpinStatsRecord> GetForUserAsync(Guid userId);
    Task<AdminSpinStatsRecord> GetAggregateAsync();
}
