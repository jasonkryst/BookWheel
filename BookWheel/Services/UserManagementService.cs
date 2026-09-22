using BookWheel.Models;
using BookWheel.Storage.Postgres;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Services;

public sealed class UserManagementService
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public UserManagementService(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // Deletes the user and all their data in a single transaction. Throws
    // InvalidOperationException if the user is not found or is the first account.
    // Returns the deleted user summary and the count of books removed.
    public async Task<(UserAccountSummary DeletedUser, int RemovedBooks)> DeleteUserWithDataAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var firstUserId = await context.Users
            .OrderBy(u => u.CreatedAtUtc)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (firstUserId == userId)
            throw new InvalidOperationException("The first account cannot be removed.");

        var books = await context.Books.IgnoreQueryFilters().Where(b => b.UserId == userId).ToListAsync();
        if (books.Count > 0)
            context.Books.RemoveRange(books);

        var selections = await context.SpinSelections.Where(s => s.UserId == userId).ToListAsync();
        if (selections.Count > 0)
            context.SpinSelections.RemoveRange(selections);

        context.Users.Remove(entity);

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return (ToSummary(entity), books.Count);
    }

    private static UserAccountSummary ToSummary(UserEntity entity) => new()
    {
        UserId = entity.Id,
        Username = entity.Username,
        Email = entity.Email,
        IsAdmin = entity.IsAdmin,
        IsDisabled = entity.IsDisabled,
        ForcePasswordReset = entity.ForcePasswordReset,
        IsLocked = entity.IsLocked,
        LockedUntilUtc = entity.LockedUntilUtc,
        CreatedAtUtc = entity.CreatedAtUtc
    };
}
