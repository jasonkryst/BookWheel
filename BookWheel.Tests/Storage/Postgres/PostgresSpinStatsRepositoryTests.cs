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

        var entry = Assert.Single(stats.TopBooks);
        Assert.Equal("(deleted)", entry.Title);
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
}
