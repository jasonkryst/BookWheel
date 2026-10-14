using BookWheel.Models;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresBookTypeRepository : IBookTypeRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresBookTypeRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<BookTypeRecord>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BookTypes
            .OrderBy(t => t.Id)
            .Select(t => new BookTypeRecord { Id = t.Id, Name = t.Name })
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BookTypes.AnyAsync(t => t.Id == id);
    }
}
