using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresSpinStatsRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresSpinStatsRepository _statsRepository = null!;
    private PostgresBookRepository _bookRepository = null!;
    private PostgresSpinHistoryRepository _spinHistoryRepository = null!;
    private PostgresCredentialRepository _credentialRepository = null!;

    public PostgresSpinStatsRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _statsRepository = new PostgresSpinStatsRepository(contextFactory);
        _bookRepository = new PostgresBookRepository(contextFactory);
        _spinHistoryRepository = new PostgresSpinHistoryRepository(contextFactory);
        _credentialRepository = new PostgresCredentialRepository(contextFactory);

        // Every aggregate test needs at least one account to exist before CreateUserAsync
        // is usable; a non-admin test subject is never also the forced-admin seed account.
        await _credentialRepository.CreateInitialAccountAsync("seed-admin", "correct-password-1", "seed-admin@example.com");
    }

    private async Task<Guid> CreateUserAsync(string username, bool disabled = false, bool locked = false)
    {
        var user = await _credentialRepository.CreateUserAsync(username, isAdmin: false, email: $"{username}@example.com");
        if (disabled || locked)
        {
            await _credentialRepository.UpdateUserAsync(user.UserId, username, isAdmin: false, isDisabled: disabled, forcePasswordReset: false, isLocked: locked, email: null);
        }

        return user.UserId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetForUserAsync_WithNoBooks_ReturnsZeroStats()
    {
        var userId = Guid.NewGuid();

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(0, stats.TotalSpins);
        Assert.Equal(0, stats.UniqueBooksSpun);
        Assert.Equal(0, stats.NeverSpunCount);
        Assert.Empty(stats.TopBooks);
        Assert.Empty(stats.NeverSpunBooks);
        Assert.Null(stats.LongestOnWheel);
        Assert.Null(stats.ShortestOnWheel);
    }

    [Fact]
    public async Task GetForUserAsync_WithNoBooksSpun_AllActiveBooksAreNeverSpun()
    {
        var userId = Guid.NewGuid();
        var bookA = await _bookRepository.AddAsync(userId, "Alpha");
        var bookB = await _bookRepository.AddAsync(userId, "Beta");

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(0, stats.TotalSpins);
        Assert.Equal(2, stats.NeverSpunCount);
        Assert.Equal(2, stats.NeverSpunBooks.Count);
        var titles = stats.NeverSpunBooks.Select(b => b.Title).ToList();
        Assert.Contains("Alpha", titles);
        Assert.Contains("Beta", titles);
        Assert.Equal(new[] { bookA.Id, bookB.Id }.OrderBy(id => id), stats.NeverSpunBooks.Select(b => b.BookId).OrderBy(id => id));
    }

    [Fact]
    public async Task GetForUserAsync_NeverSpunBooks_AreAlphabeticallyOrdered()
    {
        var userId = Guid.NewGuid();
        await _bookRepository.AddAsync(userId, "Zebra Book");
        await _bookRepository.AddAsync(userId, "Apple Book");
        await _bookRepository.AddAsync(userId, "Mango Book");

        var stats = await _statsRepository.GetForUserAsync(userId);

        var titles = stats.NeverSpunBooks.Select(b => b.Title).ToList();
        Assert.Equal(["Apple Book", "Mango Book", "Zebra Book"], titles);
    }

    [Fact]
    public async Task GetForUserAsync_WithAllBooksSpun_NeverSpunBooksIsEmpty()
    {
        var userId = Guid.NewGuid();
        var bookA = await _bookRepository.AddAsync(userId, "Spun Once");
        var bookB = await _bookRepository.AddAsync(userId, "Spun Twice");
        await _spinHistoryRepository.RecordAsync(userId, bookA.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(0, stats.NeverSpunCount);
        Assert.Empty(stats.NeverSpunBooks);
        Assert.Equal(2, stats.UniqueBooksSpun);
    }

    [Fact]
    public async Task GetForUserAsync_NeverSpunBooks_ExcludesSoftDeletedBooks()
    {
        var userId = Guid.NewGuid();
        var active = await _bookRepository.AddAsync(userId, "Active Book");
        var deleted = await _bookRepository.AddAsync(userId, "Deleted Book");
        await _bookRepository.RemoveAsync(userId, deleted.Id);

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(1, stats.NeverSpunCount);
        var entry = Assert.Single(stats.NeverSpunBooks);
        Assert.Equal("Active Book", entry.Title);
        Assert.Equal(active.Id, entry.BookId);
    }

    [Fact]
    public async Task GetForUserAsync_TopBooks_IncludesSpinCountsForSoftDeletedBooks()
    {
        var userId = Guid.NewGuid();
        var book = await _bookRepository.AddAsync(userId, "Later Removed");
        await _spinHistoryRepository.RecordAsync(userId, book.Id, DateTimeOffset.UtcNow);
        await _bookRepository.RemoveAsync(userId, book.Id);

        var stats = await _statsRepository.GetForUserAsync(userId);

        // Soft-delete keeps the row in the DB; IgnoreQueryFilters() in the join still finds it,
        // so the title is preserved rather than falling back to "(deleted)".
        var entry = Assert.Single(stats.TopBooks);
        Assert.Equal("Later Removed", entry.Title);
        Assert.Equal(1, entry.SpinCount);
    }

    [Fact]
    public async Task GetForUserAsync_TopBooks_OrderedBySpinCountDescending()
    {
        var userId = Guid.NewGuid();
        var bookA = await _bookRepository.AddAsync(userId, "Rarely Spun");
        var bookB = await _bookRepository.AddAsync(userId, "Often Spun");
        await _spinHistoryRepository.RecordAsync(userId, bookA.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(2, stats.TopBooks.Count);
        Assert.Equal("Often Spun", stats.TopBooks[0].Title);
        Assert.Equal(3, stats.TopBooks[0].SpinCount);
        Assert.Equal("Rarely Spun", stats.TopBooks[1].Title);
        Assert.Equal(1, stats.TopBooks[1].SpinCount);
    }

    [Fact]
    public async Task GetForUserAsync_PercentageIsCorrect()
    {
        var userId = Guid.NewGuid();
        var bookA = await _bookRepository.AddAsync(userId, "One");
        var bookB = await _bookRepository.AddAsync(userId, "Three");
        await _spinHistoryRepository.RecordAsync(userId, bookA.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, bookB.Id, DateTimeOffset.UtcNow);

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(4, stats.TotalSpins);
        var three = stats.TopBooks.First(b => b.Title == "Three");
        var one = stats.TopBooks.First(b => b.Title == "One");
        Assert.Equal(75.0, three.Percentage);
        Assert.Equal(25.0, one.Percentage);
    }

    [Fact]
    public async Task GetForUserAsync_StatsAreIsolatedPerUser()
    {
        var userOne = Guid.NewGuid();
        var userTwo = Guid.NewGuid();
        var bookOne = await _bookRepository.AddAsync(userOne, "User One Book");
        var bookTwo = await _bookRepository.AddAsync(userTwo, "User Two Book");
        await _spinHistoryRepository.RecordAsync(userOne, bookOne.Id, DateTimeOffset.UtcNow);

        var statsOne = await _statsRepository.GetForUserAsync(userOne);
        var statsTwo = await _statsRepository.GetForUserAsync(userTwo);

        Assert.Equal(1, statsOne.TotalSpins);
        Assert.Empty(statsOne.NeverSpunBooks);
        Assert.Equal(0, statsTwo.TotalSpins);
        Assert.Single(statsTwo.NeverSpunBooks);
        Assert.Equal("User Two Book", statsTwo.NeverSpunBooks[0].Title);
    }

    [Fact]
    public async Task GetForUserAsync_TypeBreakdown_CountsSpinsByBookType()
    {
        var userId = Guid.NewGuid();
        var physical = await _bookRepository.AddAsync(userId, "Physical Book", bookTypeId: 1);
        var digital1 = await _bookRepository.AddAsync(userId, "Digital Book 1", bookTypeId: 2);
        var digital2 = await _bookRepository.AddAsync(userId, "Digital Book 2", bookTypeId: 2);
        await _spinHistoryRepository.RecordAsync(userId, physical.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, digital1.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, digital2.Id, DateTimeOffset.UtcNow);
        await _spinHistoryRepository.RecordAsync(userId, digital2.Id, DateTimeOffset.UtcNow);

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(2, stats.TypeBreakdown.Count);
        var physicalEntry = stats.TypeBreakdown.Single(x => x.BookTypeId == 1);
        Assert.Equal(1, physicalEntry.SpinCount);
        var digitalEntry = stats.TypeBreakdown.Single(x => x.BookTypeId == 2);
        Assert.Equal(3, digitalEntry.SpinCount);
    }

    [Fact]
    public async Task GetForUserAsync_TypeBreakdown_EmptyWhenNoSpins()
    {
        var userId = Guid.NewGuid();
        await _bookRepository.AddAsync(userId, "Unspun Book");

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Empty(stats.TypeBreakdown);
    }

    [Fact]
    public async Task GetForUserAsync_MixedSpunAndNeverSpun_CorrectSplit()
    {
        var userId = Guid.NewGuid();
        var spunBook = await _bookRepository.AddAsync(userId, "Was Spun");
        await _bookRepository.AddAsync(userId, "Never Touched");
        await _spinHistoryRepository.RecordAsync(userId, spunBook.Id, DateTimeOffset.UtcNow);

        var stats = await _statsRepository.GetForUserAsync(userId);

        Assert.Equal(1, stats.TotalSpins);
        Assert.Equal(1, stats.UniqueBooksSpun);
        Assert.Equal(1, stats.NeverSpunCount);
        var neverSpun = Assert.Single(stats.NeverSpunBooks);
        Assert.Equal("Never Touched", neverSpun.Title);
        var topBook = Assert.Single(stats.TopBooks);
        Assert.Equal("Was Spun", topBook.Title);
    }

    [Fact]
    public async Task GetAggregateAsync_TotalUserCount_CountsEveryAccountRegardlessOfStatus()
    {
        // seed-admin (from InitializeAsync) + two more, one of them disabled.
        await CreateUserAsync("total-count-active");
        await CreateUserAsync("total-count-disabled", disabled: true);

        var aggregate = await _statsRepository.GetAggregateAsync();

        Assert.Equal(3, aggregate.TotalUserCount);
    }

    [Fact]
    public async Task GetAggregateAsync_ActiveUserCount_CountsUserWithRecentLoginAndNoSpins()
    {
        var userId = await CreateUserAsync("recent-login-no-spins");
        await _credentialRepository.RecordLoginAsync(userId, DateTimeOffset.UtcNow);

        var aggregate = await _statsRepository.GetAggregateAsync();

        Assert.Equal(1, aggregate.ActiveUserCount);
    }

    [Fact]
    public async Task GetAggregateAsync_ActiveUserCount_CountsUserWithRecentSpinAndNoLogin()
    {
        var userId = await CreateUserAsync("recent-spin-no-login");
        var book = await _bookRepository.AddAsync(userId, "Spun Recently");
        await _spinHistoryRepository.RecordAsync(userId, book.Id, DateTimeOffset.UtcNow);

        var aggregate = await _statsRepository.GetAggregateAsync();

        Assert.Equal(1, aggregate.ActiveUserCount);
    }

    [Fact]
    public async Task GetAggregateAsync_ActiveUserCount_ExcludesDisabledUserDespiteRecentActivity()
    {
        var userId = await CreateUserAsync("disabled-but-recent", disabled: true);
        await _credentialRepository.RecordLoginAsync(userId, DateTimeOffset.UtcNow);
        var book = await _bookRepository.AddAsync(userId, "Spun By Disabled User");
        await _spinHistoryRepository.RecordAsync(userId, book.Id, DateTimeOffset.UtcNow);

        var aggregate = await _statsRepository.GetAggregateAsync();

        Assert.Equal(0, aggregate.ActiveUserCount);
        Assert.Equal(2, aggregate.TotalUserCount);
    }

    [Fact]
    public async Task GetAggregateAsync_ActiveUserCount_ExcludesLockedUserDespiteRecentLogin()
    {
        var userId = await CreateUserAsync("locked-but-recent", locked: true);
        await _credentialRepository.RecordLoginAsync(userId, DateTimeOffset.UtcNow);

        var aggregate = await _statsRepository.GetAggregateAsync();

        Assert.Equal(0, aggregate.ActiveUserCount);
    }

    [Fact]
    public async Task GetAggregateAsync_ActiveUserCount_ExcludesActivityOlderThan30Days()
    {
        var staleLoginUser = await CreateUserAsync("stale-login");
        await _credentialRepository.RecordLoginAsync(staleLoginUser, DateTimeOffset.UtcNow.AddDays(-31));

        var staleSpinUser = await CreateUserAsync("stale-spin");
        var book = await _bookRepository.AddAsync(staleSpinUser, "Spun Long Ago");
        await _spinHistoryRepository.RecordAsync(staleSpinUser, book.Id, DateTimeOffset.UtcNow.AddDays(-31));

        var aggregate = await _statsRepository.GetAggregateAsync();

        Assert.Equal(0, aggregate.ActiveUserCount);
        Assert.Equal(3, aggregate.TotalUserCount);
    }

    [Fact]
    public async Task GetAggregateAsync_ActiveUserCount_MixedUsers_CountsOnlyQualifyingOnes()
    {
        await CreateUserAsync("never-active");
        var active = await CreateUserAsync("active-user");
        await _credentialRepository.RecordLoginAsync(active, DateTimeOffset.UtcNow.AddDays(-1));
        var disabledActive = await CreateUserAsync("disabled-active-user", disabled: true);
        await _credentialRepository.RecordLoginAsync(disabledActive, DateTimeOffset.UtcNow);
        var staleUser = await CreateUserAsync("stale-user");
        await _credentialRepository.RecordLoginAsync(staleUser, DateTimeOffset.UtcNow.AddDays(-45));

        var aggregate = await _statsRepository.GetAggregateAsync();

        // seed-admin + 4 created users = 5 total; only "active-user" qualifies as active.
        Assert.Equal(5, aggregate.TotalUserCount);
        Assert.Equal(1, aggregate.ActiveUserCount);
    }
}
