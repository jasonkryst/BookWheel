using BookWheel.Models;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresBookInfoProviderRepository : IBookInfoProviderRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresBookInfoProviderRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<BookInfoProviderRecord>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BookInfoProviders
            .OrderBy(p => p.Id)
            .Select(p => new BookInfoProviderRecord { Id = p.Id, Name = p.Name })
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BookInfoProviders.AnyAsync(p => p.Id == id);
    }
}
