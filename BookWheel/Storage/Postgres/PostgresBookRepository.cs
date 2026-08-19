using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresBookRepository : IBookRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresBookRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Books
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Id)
            .Select(b => new BookRecord { Id = b.Id, Title = b.Title })
            .ToListAsync();
    }

    public async Task<BookRecord> AddAsync(Guid userId, string title)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = new BookEntity { Id = Guid.NewGuid(), UserId = userId, Title = title.Trim() };
        context.Books.Add(entity);
        await context.SaveChangesAsync();
        return new BookRecord { Id = entity.Id, Title = entity.Title };
    }

    public async Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id)
            ?? throw new InvalidOperationException("Book not found.");
        entity.Title = title.Trim();
        await context.SaveChangesAsync();
        return new BookRecord { Id = entity.Id, Title = entity.Title };
    }

    public async Task<BookRecord> SelectRandomAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var books = await context.Books.Where(b => b.UserId == userId).ToListAsync();
        if (books.Count == 0)
        {
            throw new InvalidOperationException("No books are available in the wheel.");
        }

        var selected = books[Random.Shared.Next(books.Count)];
        return new BookRecord { Id = selected.Id, Title = selected.Title };
    }

    public async Task<BookRecord> RemoveAsync(Guid userId, Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id)
            ?? throw new InvalidOperationException("Book not found.");
        context.Books.Remove(entity);
        await context.SaveChangesAsync();
        return new BookRecord { Id = entity.Id, Title = entity.Title };
    }

    public async Task<int> RemoveUserDataAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var books = await context.Books.Where(b => b.UserId == userId).ToListAsync();
        if (books.Count == 0)
        {
            return 0;
        }

        context.Books.RemoveRange(books);
        await context.SaveChangesAsync();
        return books.Count;
    }

    public async Task<int> GetTotalBookCountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Books.CountAsync();
    }
}
