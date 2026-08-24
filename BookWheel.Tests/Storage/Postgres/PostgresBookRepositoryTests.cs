using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresBookRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresBookRepository _repository = null!;

    public PostgresBookRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresBookRepository(contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_Adds_Book_For_User()
    {
        var userId = Guid.NewGuid();

        var book = await _repository.AddAsync(userId, "  Dune  ");

        Assert.Equal("Dune", book.Title);
        var books = await _repository.GetAllAsync(userId);
        Assert.Single(books);
        Assert.Equal(book.Id, books[0].Id);
    }

    [Fact]
    public async Task UpdateAsync_Changes_Title_For_Existing_Book()
    {
        var userId = Guid.NewGuid();
        var book = await _repository.AddAsync(userId, "Original Title");

        var updated = await _repository.UpdateAsync(userId, book.Id, "Updated Title");

        Assert.Equal("Updated Title", updated.Title);
    }

    [Fact]
    public async Task AddAsync_Persists_Isbn_Author_And_CoverUrl()
    {
        var userId = Guid.NewGuid();

        var book = await _repository.AddAsync(userId, "Effective Java", "9780134685991", "Joshua Bloch", "https://covers.openlibrary.org/b/id/12345-L.jpg");

        Assert.Equal("9780134685991", book.Isbn);
        Assert.Equal("Joshua Bloch", book.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", book.CoverUrl);

        var books = await _repository.GetAllAsync(userId);
        var stored = Assert.Single(books);
        Assert.Equal("9780134685991", stored.Isbn);
        Assert.Equal("Joshua Bloch", stored.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", stored.CoverUrl);
    }

    [Fact]
    public async Task AddAsync_Leaves_Isbn_Author_And_CoverUrl_Null_When_Not_Provided()
    {
        var userId = Guid.NewGuid();

        var book = await _repository.AddAsync(userId, "Untagged Book");

        Assert.Null(book.Isbn);
        Assert.Null(book.Author);
        Assert.Null(book.CoverUrl);
    }

    [Fact]
    public async Task UpdateAsync_Backfills_Isbn_Author_And_CoverUrl_On_An_Existing_Book()
    {
        var userId = Guid.NewGuid();
        var book = await _repository.AddAsync(userId, "Untagged Book");

        var updated = await _repository.UpdateAsync(userId, book.Id, "Untagged Book", "9780134685991", "Joshua Bloch", "https://covers.openlibrary.org/b/id/12345-L.jpg");

        Assert.Equal("9780134685991", updated.Isbn);
        Assert.Equal("Joshua Bloch", updated.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", updated.CoverUrl);
    }

    [Fact]
    public async Task RemoveAsync_Removes_Book_And_Reduces_Count()
    {
        var userId = Guid.NewGuid();
        var book = await _repository.AddAsync(userId, "To Remove");

        await _repository.RemoveAsync(userId, book.Id);

        var books = await _repository.GetAllAsync(userId);
        Assert.Empty(books);
    }

    [Fact]
    public async Task SelectRandomAsync_Returns_A_Book_From_The_Users_List()
    {
        var userId = Guid.NewGuid();
        await _repository.AddAsync(userId, "Book One");
        await _repository.AddAsync(userId, "Book Two");

        var selected = await _repository.SelectRandomAsync(userId);

        var books = await _repository.GetAllAsync(userId);
        Assert.Contains(books, b => b.Id == selected.Id);
    }

    [Fact]
    public async Task GetTotalBookCountAsync_Sums_Books_Across_Users()
    {
        var userOne = Guid.NewGuid();
        var userTwo = Guid.NewGuid();
        await _repository.AddAsync(userOne, "User One Book");
        await _repository.AddAsync(userTwo, "User Two Book A");
        await _repository.AddAsync(userTwo, "User Two Book B");

        var total = await _repository.GetTotalBookCountAsync();

        Assert.Equal(3, total);
    }

    [Fact]
    public async Task RemoveUserDataAsync_Removes_All_Books_For_User_Only()
    {
        var userOne = Guid.NewGuid();
        var userTwo = Guid.NewGuid();
        await _repository.AddAsync(userOne, "User One Book");
        await _repository.AddAsync(userTwo, "User Two Book");

        var removedCount = await _repository.RemoveUserDataAsync(userOne);

        Assert.Equal(1, removedCount);
        Assert.Empty(await _repository.GetAllAsync(userOne));
        Assert.Single(await _repository.GetAllAsync(userTwo));
    }

    [Fact]
    public async Task UpdateAsync_On_Nonexistent_Book_Throws()
    {
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.UpdateAsync(userId, Guid.NewGuid(), "New Title"));
    }

    [Fact]
    public async Task RemoveAsync_On_Nonexistent_Book_Throws()
    {
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.RemoveAsync(userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task SelectRandomAsync_With_No_Books_Throws()
    {
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.SelectRandomAsync(userId));
    }

    [Fact]
    public async Task RemoveAsync_SoftDeletes_Row_Instead_Of_Hard_Deleting()
    {
        var userId = Guid.NewGuid();
        var book = await _repository.AddAsync(userId, "Soft Deleted Book");

        await _repository.RemoveAsync(userId, book.Id);

        await using var context = _fixture.CreateContext();
        var entity = await context.Books.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == book.Id);
        Assert.NotNull(entity);
        Assert.NotNull(entity!.DeletedAtUtc);
    }

    [Fact]
    public async Task RemoveAsync_On_Already_Deleted_Book_Throws()
    {
        var userId = Guid.NewGuid();
        var book = await _repository.AddAsync(userId, "Deleted Twice");
        await _repository.RemoveAsync(userId, book.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.RemoveAsync(userId, book.Id));
    }

    [Fact]
    public async Task GetAllAsync_Excludes_SoftDeleted_Books()
    {
        var userId = Guid.NewGuid();
        var kept = await _repository.AddAsync(userId, "Kept Book");
        var removed = await _repository.AddAsync(userId, "Removed Book");
        await _repository.RemoveAsync(userId, removed.Id);

        var books = await _repository.GetAllAsync(userId);

        var remaining = Assert.Single(books);
        Assert.Equal(kept.Id, remaining.Id);
    }

    [Fact]
    public async Task SelectRandomAsync_Never_Selects_A_SoftDeleted_Book()
    {
        var userId = Guid.NewGuid();
        var kept = await _repository.AddAsync(userId, "Kept Book");
        var removed = await _repository.AddAsync(userId, "Removed Book");
        await _repository.RemoveAsync(userId, removed.Id);

        for (var i = 0; i < 20; i++)
        {
            var selected = await _repository.SelectRandomAsync(userId);
            Assert.Equal(kept.Id, selected.Id);
        }
    }

    [Fact]
    public async Task UpdateAsync_On_SoftDeleted_Book_Throws()
    {
        var userId = Guid.NewGuid();
        var book = await _repository.AddAsync(userId, "Deleted Book");
        await _repository.RemoveAsync(userId, book.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.UpdateAsync(userId, book.Id, "New Title"));
    }

    [Fact]
    public async Task GetTotalBookCountAsync_Excludes_SoftDeleted_Books()
    {
        var userId = Guid.NewGuid();
        await _repository.AddAsync(userId, "Kept Book");
        var removed = await _repository.AddAsync(userId, "Removed Book");
        await _repository.RemoveAsync(userId, removed.Id);

        var total = await _repository.GetTotalBookCountAsync();

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetAllForExportAsync_Includes_SoftDeleted_Books_With_DeletedAtUtc_Set()
    {
        var userId = Guid.NewGuid();
        var kept = await _repository.AddAsync(userId, "Kept Book");
        var removed = await _repository.AddAsync(userId, "Removed Book");
        await _repository.RemoveAsync(userId, removed.Id);

        var exported = await _repository.GetAllForExportAsync(userId);

        Assert.Equal(2, exported.Count);
        var exportedKept = exported.Single(b => b.Id == kept.Id);
        Assert.Null(exportedKept.DeletedAtUtc);
        var exportedRemoved = exported.Single(b => b.Id == removed.Id);
        Assert.NotNull(exportedRemoved.DeletedAtUtc);
    }

    [Fact]
    public async Task GetAllForExportAsync_Only_Returns_Books_For_The_Requested_User()
    {
        var userOne = Guid.NewGuid();
        var userTwo = Guid.NewGuid();
        await _repository.AddAsync(userOne, "User One Book");
        await _repository.AddAsync(userTwo, "User Two Book");

        var exported = await _repository.GetAllForExportAsync(userOne);

        var book = Assert.Single(exported);
        Assert.Equal("User One Book", book.Title);
    }

    [Fact]
    public async Task RemoveUserDataAsync_HardDeletes_Including_Previously_SoftDeleted_Books()
    {
        var userId = Guid.NewGuid();
        var active = await _repository.AddAsync(userId, "Active Book");
        var softDeleted = await _repository.AddAsync(userId, "Already Removed Book");
        await _repository.RemoveAsync(userId, softDeleted.Id);

        var removedCount = await _repository.RemoveUserDataAsync(userId);

        Assert.Equal(2, removedCount);
        await using var context = _fixture.CreateContext();
        var remaining = await context.Books.IgnoreQueryFilters()
            .Where(b => b.Id == active.Id || b.Id == softDeleted.Id)
            .ToListAsync();
        Assert.Empty(remaining);
    }
}
