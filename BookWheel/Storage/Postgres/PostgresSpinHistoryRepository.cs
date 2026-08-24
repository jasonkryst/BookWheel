using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresSpinHistoryRepository : ISpinHistoryRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresSpinHistoryRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task RecordAsync(Guid userId, Guid bookId, DateTimeOffset selectedAtUtc)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.SpinSelections.Add(new SpinSelectionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BookId = bookId,
            SelectedAtUtc = selectedAtUtc
        });
        await context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<SpinHistoryRecord>> GetForUserAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query =
            from selection in context.SpinSelections
            where selection.UserId == userId
            join book in context.Books.IgnoreQueryFilters() on selection.BookId equals book.Id into books
            from book in books.DefaultIfEmpty()
            orderby selection.SelectedAtUtc descending
            select new SpinHistoryRecord
            {
                BookId = selection.BookId,
                Title = book != null ? book.Title : null,
                SelectedAtUtc = selection.SelectedAtUtc
            };

        return await query.ToListAsync();
    }

    public async Task<int> RemoveUserDataAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var selections = await context.SpinSelections.Where(s => s.UserId == userId).ToListAsync();
        if (selections.Count == 0)
        {
            return 0;
        }

        context.SpinSelections.RemoveRange(selections);
        await context.SaveChangesAsync();
        return selections.Count;
    }
}
