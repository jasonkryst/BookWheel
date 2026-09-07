using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresUserPreferencesRepository : IUserPreferencesRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresUserPreferencesRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UserPreferences> GetAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");
        return ToPreferences(entity);
    }

    public async Task<UserPreferences> UpdateAsync(Guid userId, string? theme, bool analyticsConsentOptedOut, int? preferredBookInfoProviderId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        entity.Theme = theme;
        entity.AnalyticsConsentOptedOut = analyticsConsentOptedOut;
        entity.PreferredBookInfoProviderId = preferredBookInfoProviderId;
        await context.SaveChangesAsync();

        return ToPreferences(entity);
    }

    private static UserPreferences ToPreferences(UserEntity entity) => new()
    {
        Theme = entity.Theme,
        AnalyticsConsentOptedOut = entity.AnalyticsConsentOptedOut,
        PreferredBookInfoProviderId = entity.PreferredBookInfoProviderId
    };
}
