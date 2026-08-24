using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresSpinHistoryRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresSpinHistoryRepository _repository = null!;
    private PostgresBookRepository _bookRepository = null!;

    public PostgresSpinHistoryRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresSpinHistoryRepository(contextFactory);
        _bookRepository = new PostgresBookRepository(contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordAsync_Then_GetForUserAsync_Returns_The_Recorded_Selection()
    {
        var userId = Guid.NewGuid();
        var book = await _bookRepository.AddAsync(userId, "Dune");
        var selectedAt = DateTimeOffset.UtcNow;

        await _repository.RecordAsync(userId, book.Id, selectedAt);

        var history = await _repository.GetForUserAsync(userId);
        var entry = Assert.Single(history);
        Assert.Equal(book.Id, entry.BookId);
        Assert.Equal("Dune", entry.Title);
        Assert.True((selectedAt - entry.SelectedAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetForUserAsync_Orders_Newest_Selection_First()
    {
        var userId = Guid.NewGuid();
        var book = await _bookRepository.AddAsync(userId, "Book");
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        var later = DateTimeOffset.UtcNow;

        await _repository.RecordAsync(userId, book.Id, earlier);
        await _repository.RecordAsync(userId, book.Id, later);

        var history = await _repository.GetForUserAsync(userId);
        Assert.Equal(2, history.Count);
        Assert.True((later - history[0].SelectedAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.True((earlier - history[1].SelectedAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetForUserAsync_Is_Isolated_Per_User()
    {
        var userOne = Guid.NewGuid();
        var userTwo = Guid.NewGuid();
        var bookOne = await _bookRepository.AddAsync(userOne, "User One Book");
        var bookTwo = await _bookRepository.AddAsync(userTwo, "User Two Book");
        await _repository.RecordAsync(userOne, bookOne.Id, DateTimeOffset.UtcNow);
        await _repository.RecordAsync(userTwo, bookTwo.Id, DateTimeOffset.UtcNow);

        var historyOne = await _repository.GetForUserAsync(userOne);
        var historyTwo = await _repository.GetForUserAsync(userTwo);

        Assert.Single(historyOne);
        Assert.Equal(bookOne.Id, historyOne[0].BookId);
        Assert.Single(historyTwo);
        Assert.Equal(bookTwo.Id, historyTwo[0].BookId);
    }

    [Fact]
    public async Task GetForUserAsync_With_No_History_Returns_Empty()
    {
        var userId = Guid.NewGuid();

        var history = await _repository.GetForUserAsync(userId);

        Assert.Empty(history);
    }

    [Fact]
    public async Task GetForUserAsync_Still_Includes_Title_After_Book_Is_SoftDeleted()
    {
        var userId = Guid.NewGuid();
        var book = await _bookRepository.AddAsync(userId, "Later Removed");
        await _repository.RecordAsync(userId, book.Id, DateTimeOffset.UtcNow);

        await _bookRepository.RemoveAsync(userId, book.Id);

        var history = await _repository.GetForUserAsync(userId);
        var entry = Assert.Single(history);
        Assert.Equal("Later Removed", entry.Title);
    }

    [Fact]
    public async Task RemoveUserDataAsync_Removes_History_For_The_Given_User_Only()
    {
        var userOne = Guid.NewGuid();
        var userTwo = Guid.NewGuid();
        var bookOne = await _bookRepository.AddAsync(userOne, "User One Book");
        var bookTwo = await _bookRepository.AddAsync(userTwo, "User Two Book");
        await _repository.RecordAsync(userOne, bookOne.Id, DateTimeOffset.UtcNow);
        await _repository.RecordAsync(userTwo, bookTwo.Id, DateTimeOffset.UtcNow);

        var removedCount = await _repository.RemoveUserDataAsync(userOne);

        Assert.Equal(1, removedCount);
        Assert.Empty(await _repository.GetForUserAsync(userOne));
        Assert.Single(await _repository.GetForUserAsync(userTwo));
    }

    [Fact]
    public async Task RemoveUserDataAsync_With_No_History_Returns_Zero()
    {
        var userId = Guid.NewGuid();

        var removedCount = await _repository.RemoveUserDataAsync(userId);

        Assert.Equal(0, removedCount);
    }
}
