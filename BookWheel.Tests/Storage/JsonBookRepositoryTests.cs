using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;

namespace BookWheel.Tests.Storage;

public sealed class JsonBookRepositoryTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly JsonBookRepository _repository;

    public JsonBookRepositoryTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-book-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        var environment = StorageTestEnvironment.Create(_contentRoot);
        _repository = new JsonBookRepository(environment);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            try
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }

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
    public async Task GetAllAsync_With_Corrupted_Data_File_Throws_And_Quarantines()
    {
        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var dataFilePath = Path.Combine(dataDirectory, "books.json");
        await File.WriteAllTextAsync(dataFilePath, "{ not valid json");

        await Assert.ThrowsAsync<CorruptedDataException>(
            () => _repository.GetAllAsync(Guid.NewGuid()));

        var corruptDirectory = Path.Combine(dataDirectory, "corrupt");
        Assert.True(Directory.Exists(corruptDirectory));
        Assert.NotEmpty(Directory.GetFiles(corruptDirectory));
    }
}
