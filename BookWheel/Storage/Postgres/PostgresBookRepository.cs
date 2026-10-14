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
        var rows = await context.Books
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id)
            .Select(b => new
            {
                Entity = b,
                LastSpunAtUtc = context.SpinSelections
                    .Where(s => s.UserId == userId && s.BookId == b.Id)
                    .Max(s => (DateTimeOffset?)s.SelectedAtUtc)
            })
            .ToListAsync();
        return rows.Select(row =>
        {
            var record = ToRecord(row.Entity);
            record.LastSpunAtUtc = row.LastSpunAtUtc;
            return record;
        }).ToList();
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllForExportAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.Books
            .IgnoreQueryFilters()
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Id)
            .ToListAsync();
        return entities.Select(ToRecord).ToList();
    }

    public async Task<BookRecord> AddAsync(Guid userId, string title, string? isbn = null, string? author = null, string? coverUrl = null, bool addedByScanner = false, int bookTypeId = 1, Guid? createdByUserId = null, int? bookInfoProviderId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = new BookEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title.Trim(),
            Isbn = isbn,
            Author = author,
            CoverUrl = coverUrl,
            AddedByScanner = addedByScanner,
            BookTypeId = bookTypeId,
            BookInfoProviderId = bookInfoProviderId,
            CreatedByUserId = createdByUserId ?? userId
        };
        context.Books.Add(entity);
        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title, string? isbn = null, string? author = null, string? coverUrl = null, int bookTypeId = 1, Guid? updatedByUserId = null, int? bookInfoProviderId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id)
            ?? throw new InvalidOperationException("Book not found.");
        entity.Title = title.Trim();
        entity.Isbn = isbn;
        entity.Author = author;
        entity.CoverUrl = coverUrl;
        entity.BookTypeId = bookTypeId;
        entity.BookInfoProviderId = bookInfoProviderId;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.LastUpdatedByUserId = updatedByUserId ?? userId;
        await context.SaveChangesAsync();
        return ToRecord(entity);
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
        return ToRecord(selected);
    }

    public async Task<BookRecord> RemoveAsync(Guid userId, Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id)
            ?? throw new InvalidOperationException("Book not found.");
        entity.DeletedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<IReadOnlyList<BookRecord>> GetDeletedAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.Books
            .IgnoreQueryFilters()
            .Where(b => b.UserId == userId && b.DeletedAtUtc != null)
            .OrderByDescending(b => b.DeletedAtUtc)
            .ToListAsync();
        return entities.Select(ToRecord).ToList();
    }

    public async Task<BookRecord> RestoreAsync(Guid userId, Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id && b.DeletedAtUtc != null)
            ?? throw new InvalidOperationException("Book not found.");
        entity.DeletedAtUtc = null;
        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<BookRecord?> FindActiveByTitleOrIsbnAsync(Guid userId, string normalizedTitle, string? normalizedIsbn)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books
            .Where(b => b.UserId == userId
                && (b.Title.Trim().ToLower() == normalizedTitle
                    || (normalizedIsbn != null && b.Isbn == normalizedIsbn)))
            .OrderBy(b => b.CreatedAtUtc)
            .FirstOrDefaultAsync();
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<BookRecord?> FindDeletedByTitleOrIsbnAsync(Guid userId, string normalizedTitle, string? normalizedIsbn)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books
            .IgnoreQueryFilters()
            .Where(b => b.UserId == userId && b.DeletedAtUtc != null
                && (b.Title.Trim().ToLower() == normalizedTitle
                    || (normalizedIsbn != null && b.Isbn == normalizedIsbn)))
            .OrderByDescending(b => b.DeletedAtUtc)
            .FirstOrDefaultAsync();
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<int> RemoveUserDataAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var books = await context.Books.IgnoreQueryFilters().Where(b => b.UserId == userId).ToListAsync();
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

    private static BookRecord ToRecord(BookEntity entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Isbn = entity.Isbn,
        Author = entity.Author,
        CoverUrl = entity.CoverUrl,
        DeletedAtUtc = entity.DeletedAtUtc,
        AddedByScanner = entity.AddedByScanner,
        BookTypeId = entity.BookTypeId,
        BookInfoProviderId = entity.BookInfoProviderId,
        CreatedAtUtc = entity.CreatedAtUtc,
        CreatedByUserId = entity.CreatedByUserId,
        UpdatedAtUtc = entity.UpdatedAtUtc,
        LastUpdatedByUserId = entity.LastUpdatedByUserId
    };
}
