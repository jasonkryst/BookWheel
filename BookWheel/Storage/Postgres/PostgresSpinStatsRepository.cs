using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresSpinStatsRepository : ISpinStatsRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresSpinStatsRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SpinStatsRecord> GetForUserAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var totalSpins = await context.SpinSelections
            .Where(s => s.UserId == userId)
            .CountAsync();

        var uniqueBooksSpun = await context.SpinSelections
            .Where(s => s.UserId == userId)
            .Select(s => s.BookId)
            .Distinct()
            .CountAsync();

        // Active books only for wheel-duration stats
        var activeBooks = await context.Books
            .Where(b => b.UserId == userId)
            .Select(b => new { b.Id, b.Title, b.CreatedAtUtc })
            .ToListAsync();

        var neverSpunIds = await context.SpinSelections
            .Where(s => s.UserId == userId)
            .Select(s => s.BookId)
            .Distinct()
            .ToListAsync();

        var neverSpunSet = neverSpunIds.ToHashSet();
        var neverSpunCount = activeBooks.Count(b => !neverSpunSet.Contains(b.Id));

        WheelDurationRecord? longestOnWheel = null;
        WheelDurationRecord? shortestOnWheel = null;

        if (activeBooks.Count > 0)
        {
            var oldest = activeBooks.MinBy(b => b.CreatedAtUtc)!;
            longestOnWheel = new WheelDurationRecord
            {
                BookId = oldest.Id,
                Title = oldest.Title,
                DaysOnWheel = (int)(now - oldest.CreatedAtUtc).TotalDays
            };

            var newest = activeBooks.MaxBy(b => b.CreatedAtUtc)!;
            shortestOnWheel = new WheelDurationRecord
            {
                BookId = newest.Id,
                Title = newest.Title,
                DaysOnWheel = (int)(now - newest.CreatedAtUtc).TotalDays
            };
        }

        // Spin counts per book (including deleted books so history stays intact)
        var spinCounts = await (
            from s in context.SpinSelections.Where(s => s.UserId == userId)
            join b in context.Books.IgnoreQueryFilters() on s.BookId equals b.Id into books
            from b in books.DefaultIfEmpty()
            group s by new { s.BookId, Title = b != null ? b.Title : null } into g
            select new { g.Key.BookId, g.Key.Title, Count = g.Count() }
        ).OrderByDescending(x => x.Count).ToListAsync();

        var topBooks = spinCounts.Select(x => new BookSpinCountRecord
        {
            BookId = x.BookId,
            Title = x.Title ?? "(deleted)",
            SpinCount = x.Count,
            Percentage = totalSpins > 0 ? Math.Round(x.Count * 100.0 / totalSpins, 1) : 0
        }).ToList();

        return new SpinStatsRecord
        {
            TotalSpins = totalSpins,
            UniqueBooksSpun = uniqueBooksSpun,
            NeverSpunCount = neverSpunCount,
            LongestOnWheel = longestOnWheel,
            ShortestOnWheel = shortestOnWheel,
            TopBooks = topBooks
        };
    }

    public async Task<AdminSpinStatsRecord> GetAggregateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var totalSpins = await context.SpinSelections.CountAsync();
        var activeUserCount = await context.Users.CountAsync();

        var topUsers = await (
            from s in context.SpinSelections
            join u in context.Users on s.UserId equals u.Id
            group s by new { s.UserId, u.Username } into g
            orderby g.Count() descending
            select new { g.Key.UserId, g.Key.Username, SpinCount = g.Count() }
        ).Take(10).ToListAsync();

        return new AdminSpinStatsRecord
        {
            TotalSpinsAllUsers = totalSpins,
            ActiveUserCount = activeUserCount,
            TopUsers = topUsers.Select(u => new UserSpinCountRecord
            {
                UserId = u.UserId,
                Username = u.Username,
                SpinCount = u.SpinCount
            }).ToList()
        };
    }
}
