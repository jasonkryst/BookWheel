# Storage Layer Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `IBookRepository`, `ICredentialRepository`, and `IPasswordResetTokenRepository` interfaces over the existing JSON-file storage, so business logic depends on an abstraction instead of concrete `BookStore`/`CredentialStore` classes, with zero observable behavior change.

**Architecture:** New `BookWheel/Storage/` folder holds the three interfaces plus their JSON-backed implementations (`JsonBookRepository`, `JsonCredentialRepository`, `JsonPasswordResetTokenRepository`). Legacy-migration and corrupt-file-quarantine methods stay on the concrete `Json*` classes only, outside the interfaces. `AuthService` gains orchestration methods that coordinate the split credential/token repositories for password-reset flows.

**Tech Stack:** .NET 8, ASP.NET Core, xUnit, `Microsoft.AspNetCore.DataProtection`.

## Global Constraints

- No observable behavior change to any existing API endpoint (spec: "No observable behavior change" section) — except the internal sequencing of password-reset-link creation, which must preserve the exact same error messages and success/failure semantics as today.
- Every existing test in `BookWheel.Tests/BookWheelApiTests.cs` must continue to pass unmodified — it is the regression guard for this refactor.
- Migration (`HasLegacyPayloadAsync`/`MigrateLegacyPayloadAsync`) and corrupt-file quarantine stay on concrete `Json*Repository` classes only, never on the interfaces.
- `JsonCredentialRepository` and `JsonPasswordResetTokenRepository` must both create their `IDataProtector` with the exact same purpose string `"BookWheel.Credentials.v1"` that `CredentialStore` used today, so already-encrypted `user.cred`/`password-reset-tokens.dat` files on any existing deployment remain decryptable after the split.
- DI registrations must expose each JSON implementation under its interface pointing at the *same singleton instance* (not a second instance), preserving today's per-file semaphore-lock semantics.
- New unit test files live under `BookWheel.Tests/Storage/` and follow the existing project's xUnit + temp-content-root testing pattern (see `BookWheel.Tests/BookWheelWebAppFactory.cs`).

---

### Task 1: Book repository interface, JSON implementation, and unit tests

**Files:**
- Create: `BookWheel/Storage/IBookRepository.cs`
- Create: `BookWheel/Storage/JsonBookRepository.cs`
- Create: `BookWheel.Tests/Storage/StorageTestEnvironment.cs`
- Create: `BookWheel.Tests/Storage/JsonBookRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing new (uses existing `BookWheel.Models.BookRecord`, `BookWheel.Services.CorruptedDataException`)
- Produces: `IBookRepository` (7 methods below) and `JsonBookRepository` (implements it, plus `HasLegacyPayloadAsync()`, `MigrateLegacyPayloadAsync(Guid? ownerUserId)`, and nested `BookMigrationResult`), consumed by Task 2. `StorageTestEnvironment.Create(string contentRootPath)` returning `IWebHostEnvironment`, consumed by Tasks 1 and 3's test files.

- [ ] **Step 1: Create the `IBookRepository` interface**

```csharp
// BookWheel/Storage/IBookRepository.cs
using BookWheel.Models;

namespace BookWheel.Storage;

public interface IBookRepository
{
    Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId);
    Task<BookRecord> AddAsync(Guid userId, string title);
    Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title);
    Task<BookRecord> RemoveAsync(Guid userId, Guid id);
    Task<BookRecord> SelectRandomAsync(Guid userId);
    Task<int> RemoveUserDataAsync(Guid userId);
    Task<int> GetTotalBookCountAsync();
}
```

- [ ] **Step 2: Create the shared test host-environment helper**

```csharp
// BookWheel.Tests/Storage/StorageTestEnvironment.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace BookWheel.Tests.Storage;

internal static class StorageTestEnvironment
{
    public static IWebHostEnvironment Create(string contentRootPath)
    {
        var webRootPath = Path.Combine(contentRootPath, "wwwroot");
        Directory.CreateDirectory(webRootPath);

        return new TestWebHostEnvironment
        {
            ContentRootPath = contentRootPath,
            WebRootPath = webRootPath,
            EnvironmentName = "Testing",
            ApplicationName = "BookWheel.Tests",
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath),
            WebRootFileProvider = new PhysicalFileProvider(webRootPath)
        };
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
```

- [ ] **Step 3: Write the failing test file for `JsonBookRepository`**

```csharp
// BookWheel.Tests/Storage/JsonBookRepositoryTests.cs
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
```

- [ ] **Step 4: Run the tests and confirm they fail to build**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~JsonBookRepositoryTests"`
Expected: Build FAILS — `JsonBookRepository` does not exist yet (CS0246).

- [ ] **Step 5: Create `JsonBookRepository`, a straight move of `BookWheel/Services/BookStore.cs` into `BookWheel/Storage/`**

The body is functionally identical to today's `BookStore` — same fields, same methods, same locking, same quarantine logic. Only the namespace, class name, and `: IBookRepository` clause change.

```csharp
// BookWheel/Storage/JsonBookRepository.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using BookWheel.Models;
using BookWheel.Services;

namespace BookWheel.Storage;

public sealed class JsonBookRepository : IBookRepository
{
    private const int CurrentBookSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDirectory;
    private readonly string _corruptDataDirectory;
    private readonly string _dataFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class BookStoreDocument
    {
        public int SchemaVersion { get; set; } = CurrentBookSchemaVersion;
        public Dictionary<string, List<BookRecord>> Users { get; set; } = [];
    }

    public sealed class BookMigrationResult
    {
        public bool Migrated { get; set; }
        public int BooksAffected { get; set; }
        public Guid? BooksOwnerUserId { get; set; }
    }

    public JsonBookRepository(IWebHostEnvironment environment)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _corruptDataDirectory = Path.Combine(_dataDirectory, "corrupt");
        _dataFilePath = Path.Combine(_dataDirectory, "books.json");
    }

    public async Task<bool> HasLegacyPayloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var raw = await ReadRawUnsafeAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (TryDeserialize<Dictionary<string, List<BookRecord>>>(raw) is not null)
            {
                return false;
            }

            return TryDeserialize<List<BookRecord>>(raw) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookMigrationResult> MigrateLegacyPayloadAsync(Guid? ownerUserId)
    {
        await _gate.WaitAsync();
        try
        {
            var raw = await ReadRawUnsafeAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new BookMigrationResult();
            }

            var storeDocument = TryDeserialize<BookStoreDocument>(raw);
            var booksByUser = storeDocument?.Users;
            if (booksByUser is null)
            {
                booksByUser = TryDeserialize<Dictionary<string, List<BookRecord>>>(raw);
            }
            if (booksByUser is not null)
            {
                if (!booksByUser.TryGetValue("legacy-unassigned", out var unassignedBooks))
                {
                    return new BookMigrationResult();
                }

                var owner = ownerUserId ?? Guid.Empty;
                booksByUser.Remove("legacy-unassigned");
                booksByUser[owner.ToString()] = unassignedBooks;
                await WriteStoreUnsafeAsync(booksByUser);

                return new BookMigrationResult
                {
                    Migrated = true,
                    BooksAffected = unassignedBooks.Count,
                    BooksOwnerUserId = owner
                };
            }

            var legacyBooks = TryDeserialize<List<BookRecord>>(raw);
            if (legacyBooks is null)
            {
                return new BookMigrationResult();
            }

            var targetOwner = ownerUserId ?? Guid.Empty;
            var migratedStore = new Dictionary<string, List<BookRecord>>
            {
                [targetOwner.ToString()] = legacyBooks
            };

            await WriteStoreUnsafeAsync(migratedStore);

            return new BookMigrationResult
            {
                Migrated = true,
                BooksAffected = legacyBooks.Count,
                BooksOwnerUserId = targetOwner
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            return GetBooksForUser(booksByUser, userId).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> AddAsync(Guid userId, string title)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var record = new BookRecord
            {
                Id = Guid.NewGuid(),
                Title = title.Trim()
            };

            books.Add(record);
            await WriteStoreUnsafeAsync(booksByUser);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            book.Title = title.Trim();
            await WriteStoreUnsafeAsync(booksByUser);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> SelectRandomAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            if (books.Count == 0)
            {
                throw new InvalidOperationException("No books are available in the wheel.");
            }

            var selected = books[Random.Shared.Next(books.Count)];
            return selected;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BookRecord> RemoveAsync(Guid userId, Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            books.RemoveAll(x => x.Id == id);
            await WriteStoreUnsafeAsync(booksByUser);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RemoveUserDataAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var userKey = userId.ToString();
            if (!booksByUser.TryGetValue(userKey, out var userBooks))
            {
                return 0;
            }

            var removedCount = userBooks.Count;
            booksByUser.Remove(userKey);
            await WriteStoreUnsafeAsync(booksByUser);
            return removedCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> GetTotalBookCountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            return booksByUser.Values.Sum(books => books.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, List<BookRecord>>> ReadStoreUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_dataFilePath))
        {
            await File.WriteAllTextAsync(_dataFilePath, "{}");
            return [];
        }

        var raw = await ReadRawUnsafeAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var storeDocument = TryDeserialize<BookStoreDocument>(raw);
        var booksByUser = storeDocument?.Users;
        if (booksByUser is null)
        {
            booksByUser = TryDeserialize<Dictionary<string, List<BookRecord>>>(raw);
        }
        if (booksByUser is not null)
        {
            return booksByUser;
        }

        var legacyBooks = TryDeserialize<List<BookRecord>>(raw);
        if (legacyBooks is not null)
        {
            return [];
        }

        QuarantineCorruptBooksUnsafe();
        throw new CorruptedDataException("Book data is corrupted and has been quarantined. Restore App_Data from backup.");
    }

    private async Task WriteStoreUnsafeAsync(Dictionary<string, List<BookRecord>> booksByUser)
    {
        var migrated = booksByUser
            .Where(pair => !string.Equals(pair.Key, "legacy-unassigned", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        await using var stream = File.Open(_dataFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var document = new BookStoreDocument { Users = migrated };
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
    }

    private static List<BookRecord> GetBooksForUser(Dictionary<string, List<BookRecord>> booksByUser, Guid userId)
    {
        var userKey = userId.ToString();
        if (!booksByUser.TryGetValue(userKey, out var books))
        {
            books = [];
            booksByUser[userKey] = books;
        }

        return books;
    }

    private async Task<string?> ReadRawUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_dataFilePath))
        {
            await File.WriteAllTextAsync(_dataFilePath, "{}");
            return null;
        }

        return await File.ReadAllTextAsync(_dataFilePath);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private void QuarantineCorruptBooksUnsafe()
    {
        if (!File.Exists(_dataFilePath))
        {
            return;
        }

        Directory.CreateDirectory(_corruptDataDirectory);
        var quarantineName = $"books.json-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt";
        var quarantinePath = Path.Combine(_corruptDataDirectory, quarantineName);
        File.Move(_dataFilePath, quarantinePath, overwrite: true);
        File.WriteAllText(_dataFilePath, "{}");
    }
}
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~JsonBookRepositoryTests"`
Expected: All PASS (11 tests).

- [ ] **Step 7: Commit**

```bash
git add BookWheel/Storage/IBookRepository.cs BookWheel/Storage/JsonBookRepository.cs BookWheel.Tests/Storage/StorageTestEnvironment.cs BookWheel.Tests/Storage/JsonBookRepositoryTests.cs
git commit -m "Add IBookRepository and JsonBookRepository with unit tests (#14)"
```

---

### Task 2: Wire `IBookRepository` into the application

**Files:**
- Modify: `BookWheel/Program.cs`
- Modify: `BookWheel/Controllers/BooksController.cs`
- Modify: `BookWheel/Controllers/MetricsController.cs`
- Modify: `BookWheel/Services/AppMetricsService.cs`
- Modify: `BookWheel/Services/DataMigrationService.cs`
- Modify: `BookWheel/Controllers/UsersController.cs`
- Modify: `BookWheel.Tests/BookWheelWebAppFactory.cs`
- Delete: `BookWheel/Services/BookStore.cs`

**Interfaces:**
- Consumes: `IBookRepository`, `JsonBookRepository` from Task 1.
- Produces: no new public API; all `BookStore` consumers now depend on `IBookRepository` (or concrete `JsonBookRepository` where migration methods are needed).

- [ ] **Step 1: Update `Program.cs` DI registrations**

Add `using BookWheel.Storage;` near the top, and replace the `BookStore`/`CredentialStore` registration block:

```csharp
// BookWheel/Program.cs — before
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppMetricsService>();
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<BookStore>();
builder.Services.AddSingleton<DataMigrationService>();
```

```csharp
// BookWheel/Program.cs — after
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppMetricsService>();
builder.Services.AddSingleton<CredentialStore>();

builder.Services.AddSingleton<JsonBookRepository>();
builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<JsonBookRepository>());

builder.Services.AddSingleton<DataMigrationService>();
```

(The `CredentialStore` line stays untouched in this task — it is removed in Task 4.)

- [ ] **Step 2: Update `BooksController.cs`**

Only the `using BookWheel.Storage;` import and the `IBookRepository`-typed field/constructor parameter change; every method body (`GetAll`, `Add`, `Update`, `Spin`, `Remove`) stays exactly as it is today.

```csharp
// BookWheel/Controllers/BooksController.cs
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _store;

    public BooksController(AuthService authService, AppMetricsService metricsService, IBookRepository store)
    {
        _authService = authService;
        _metricsService = metricsService;
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var books = await _store.GetAllAsync(user.UserId);
            return Ok(new
            {
                books,
                activeBooks = books.ToList()
            });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] UpdateBookRequest request)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Book title is required." });
        }

        try
        {
            var book = await _store.AddAsync(user.UserId, request.Title);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookRequest request)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Book title is required." });
        }

        try
        {
            var book = await _store.UpdateAsync(user.UserId, id, request.Title);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("spin")]
    public async Task<IActionResult> Spin()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var selected = await _store.SelectRandomAsync(user.UserId);
            _metricsService.IncrementSpinCount();
            var books = await _store.GetAllAsync(user.UserId);
            return Ok(new
            {
                selected,
                activeBooks = books.ToList()
            });
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var book = await _store.RemoveAsync(user.UserId, id);
            return Ok(book);
        }
        catch (CorruptedDataException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 3: Update `MetricsController.cs`**

```csharp
// BookWheel/Controllers/MetricsController.cs
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _bookStore;

    public MetricsController(AuthService authService, AppMetricsService metricsService, IBookRepository bookStore)
    {
        _authService = authService;
        _metricsService = metricsService;
        _bookStore = bookStore;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetrics()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!user.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var snapshot = await _metricsService.GetSnapshotAsync(_bookStore);
        return Ok(snapshot);
    }
}
```

- [ ] **Step 4: Update `AppMetricsService.cs`**

```csharp
// BookWheel/Services/AppMetricsService.cs
using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class AppMetricsService
{
    private long _loginFailureCount;
    private long _loginLockoutCount;
    private long _successfulLoginCount;
    private long _spinCount;

    public void IncrementLoginFailure()
    {
        Interlocked.Increment(ref _loginFailureCount);
    }

    public void IncrementLoginLockout()
    {
        Interlocked.Increment(ref _loginLockoutCount);
    }

    public void IncrementSuccessfulLogin()
    {
        Interlocked.Increment(ref _successfulLoginCount);
    }

    public void IncrementSpinCount()
    {
        Interlocked.Increment(ref _spinCount);
    }

    public async Task<MetricsSnapshot> GetSnapshotAsync(IBookRepository bookRepository)
    {
        return new MetricsSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LoginFailureCount = Interlocked.Read(ref _loginFailureCount),
            LoginLockoutCount = Interlocked.Read(ref _loginLockoutCount),
            SuccessfulLoginCount = Interlocked.Read(ref _successfulLoginCount),
            SpinCount = Interlocked.Read(ref _spinCount),
            TotalBookCount = await bookRepository.GetTotalBookCountAsync()
        };
    }
}
```

- [ ] **Step 5: Update `DataMigrationService.cs`'s book dependency**

Only the `BookStore` parameter changes type; the `CredentialStore` parameter stays as-is until Task 4.

```csharp
// BookWheel/Services/DataMigrationService.cs
using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class DataMigrationService
{
    private readonly CredentialStore _credentialStore;
    private readonly JsonBookRepository _bookRepository;

    public DataMigrationService(CredentialStore credentialStore, JsonBookRepository bookRepository)
    {
        _credentialStore = credentialStore;
        _bookRepository = bookRepository;
    }

    public async Task<DataMigrationStatus> GetStatusAsync()
    {
        return new DataMigrationStatus
        {
            HasLegacyCredentialPayload = await _credentialStore.HasLegacyPayloadAsync(),
            HasLegacyBooksPayload = await _bookRepository.HasLegacyPayloadAsync()
        };
    }

    public async Task<DataMigrationReport> RunAsync()
    {
        var credentials = await _credentialStore.MigrateLegacyPayloadAsync();
        var users = await _credentialStore.GetUsersAsync();
        var booksOwnerId = users.OrderBy(user => user.CreatedAtUtc).Select(user => user.UserId).FirstOrDefault();
        var resolvedOwner = booksOwnerId == Guid.Empty ? (Guid?)null : booksOwnerId;
        var books = await _bookRepository.MigrateLegacyPayloadAsync(resolvedOwner);

        return new DataMigrationReport
        {
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            CredentialPayloadMigrated = credentials.Migrated,
            CredentialUsersAffected = credentials.UsersAffected,
            BooksPayloadMigrated = books.Migrated,
            BooksAffected = books.BooksAffected,
            BooksOwnerUserId = books.BooksOwnerUserId,
            Message = !credentials.Migrated && !books.Migrated
                ? "No legacy payloads required migration."
                : "Legacy payload migration completed."
        };
    }
}
```

- [ ] **Step 6: Update `UsersController.cs`'s book dependency only**

Only the `BookStore _bookStore` field/constructor parameter changes type to `IBookRepository`; `CredentialStore` stays untouched until Task 4.

```csharp
// BookWheel/Controllers/UsersController.cs — constructor and field only
using BookWheel.Storage;
// ... existing usings stay ...

private readonly AuthService _authService;
private readonly CredentialStore _credentialStore;
private readonly IBookRepository _bookStore;
private readonly ILogger<UsersController> _logger;

public UsersController(AuthService authService, CredentialStore credentialStore, IBookRepository bookStore, ILogger<UsersController> logger)
{
    _authService = authService;
    _credentialStore = credentialStore;
    _bookStore = bookStore;
    _logger = logger;
}
```

The `DeleteUser` method body is unchanged — it already only calls `_bookStore.RemoveUserDataAsync(id)`, which exists on `IBookRepository`.

- [ ] **Step 7: Update `BookWheelWebAppFactory.cs`**

Only the top `using` list and the `ConfigureServices` lambda inside `ConfigureWebHost` change. Everything else in the file (fields, `ContentRootPath`, `LogDirectoryPath`, `LoggerProvider`, constructor, `Dispose`, `CopyDirectory`, the private `TestWebHostEnvironment` class) stays exactly as it is today.

Add this import alongside the existing ones at the top of the file:

```csharp
// BookWheel.Tests/BookWheelWebAppFactory.cs — add to the existing using list
using BookWheel.Storage;
```

Replace the `builder.ConfigureServices(...)` block inside `ConfigureWebHost`:

```csharp
// BookWheel.Tests/BookWheelWebAppFactory.cs — before
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BookStore>();
            services.AddSingleton<BookStore>(_ =>
            {
                var env = new TestWebHostEnvironment
                {
                    ContentRootPath = _tempContentRoot,
                    WebRootPath = Path.Combine(_tempContentRoot, "wwwroot"),
                    EnvironmentName = "Testing",
                    ApplicationName = "BookWheel"
                };
                env.ContentRootFileProvider = new PhysicalFileProvider(env.ContentRootPath);
                env.WebRootFileProvider = new PhysicalFileProvider(env.WebRootPath);

                return new BookStore(env);
            });
        });
```

```csharp
// BookWheel.Tests/BookWheelWebAppFactory.cs — after
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<JsonBookRepository>();
            services.RemoveAll<IBookRepository>();
            services.AddSingleton<JsonBookRepository>(_ =>
            {
                var env = new TestWebHostEnvironment
                {
                    ContentRootPath = _tempContentRoot,
                    WebRootPath = Path.Combine(_tempContentRoot, "wwwroot"),
                    EnvironmentName = "Testing",
                    ApplicationName = "BookWheel"
                };
                env.ContentRootFileProvider = new PhysicalFileProvider(env.ContentRootPath);
                env.WebRootFileProvider = new PhysicalFileProvider(env.WebRootPath);

                return new JsonBookRepository(env);
            });
            services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<JsonBookRepository>());
        });
```

- [ ] **Step 8: Delete the old `BookStore.cs`**

```bash
git rm BookWheel/Services/BookStore.cs
```

- [ ] **Step 9: Build the solution**

Run: `dotnet build BookWheel.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 10: Run the full test suite**

Run: `dotnet test BookWheel.slnx`
Expected: All tests PASS, including every existing test in `BookWheelApiTests.cs` and the new `JsonBookRepositoryTests.cs`.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "Wire IBookRepository through controllers, services, and test host (#14)"
```

---

### Task 3: Credential and password-reset-token repository interfaces, JSON implementations, and unit tests

**Files:**
- Create: `BookWheel/Storage/ICredentialRepository.cs`
- Create: `BookWheel/Storage/IPasswordResetTokenRepository.cs`
- Create: `BookWheel/Storage/JsonCredentialRepository.cs`
- Create: `BookWheel/Storage/JsonPasswordResetTokenRepository.cs`
- Create: `BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs`
- Create: `BookWheel.Tests/Storage/JsonPasswordResetTokenRepositoryTests.cs`

**Interfaces:**
- Consumes: `StorageTestEnvironment.Create(string)` from Task 1.
- Produces: `ICredentialRepository`, `IPasswordResetTokenRepository`, `PasswordResetTokenLookup` (with `bool IsValid`, `Guid UserId`, `DateTimeOffset? ExpiresAtUtc`), `JsonCredentialRepository`, `JsonPasswordResetTokenRepository` — all consumed by Task 4.

- [ ] **Step 1: Create `ICredentialRepository`**

```csharp
// BookWheel/Storage/ICredentialRepository.cs
using BookWheel.Models;

namespace BookWheel.Storage;

public interface ICredentialRepository
{
    Task<bool> HasAccountAsync();
    Task<CredentialRecord> CreateInitialAccountAsync(string username, string password);
    Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password);
    Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync();
    Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked);
    Task<UserAccountSummary> DeleteUserAsync(Guid userId);
    Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId);
    Task<string> SetPasswordAsync(Guid userId, string newPassword);
    Task<string?> GetUsernameAsync(Guid userId);
}
```

- [ ] **Step 2: Create `IPasswordResetTokenRepository`**

```csharp
// BookWheel/Storage/IPasswordResetTokenRepository.cs
namespace BookWheel.Storage;

public interface IPasswordResetTokenRepository
{
    Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId);
    Task<PasswordResetTokenLookup> ValidateAsync(string token);
    Task<Guid> CompleteAsync(string token);
}

public sealed class PasswordResetTokenLookup
{
    public bool IsValid { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
```

- [ ] **Step 3: Write the failing test file for `JsonCredentialRepository`**

```csharp
// BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace BookWheel.Tests.Storage;

public sealed class JsonCredentialRepositoryTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly JsonCredentialRepository _repository;

    public JsonCredentialRepositoryTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-credential-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        var environment = StorageTestEnvironment.Create(_contentRoot);
        var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_contentRoot, "keys")));
        _repository = new JsonCredentialRepository(environment, dataProtectionProvider);
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
    public async Task CreateInitialAccountAsync_Creates_First_Account_As_Admin()
    {
        var user = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        Assert.True(user.IsAdmin);
        Assert.True(await _repository.HasAccountAsync());
    }

    [Fact]
    public async Task ValidateCredentialsAsync_With_Correct_Password_Returns_Record()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("admin-one", "correct-password");

        Assert.NotNull(result);
        Assert.Equal("admin-one", result!.Username);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_With_Wrong_Password_Returns_Null()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("admin-one", "wrong-password");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_With_Unknown_Username_Returns_Null()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("nobody", "correct-password");

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateUserAsync_Adds_NonAdmin_User()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var user = await _repository.CreateUserAsync("reader-one", isAdmin: false);

        Assert.False(user.IsAdmin);
        var users = await _repository.GetUsersAsync();
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task CreateUserAsync_With_Duplicate_Username_Throws()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");
        await _repository.CreateUserAsync("reader-one", isAdmin: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("Reader-One", isAdmin: false));
    }

    [Fact]
    public async Task UpdateUserAsync_Demoting_Last_Admin_Throws()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.UpdateUserAsync(admin.UserId, admin.Username, isAdmin: false));
    }

    [Fact]
    public async Task DeleteUserAsync_Removes_NonFirst_User()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");
        var reader = await _repository.CreateUserAsync("reader-one", isAdmin: false);

        var deleted = await _repository.DeleteUserAsync(reader.UserId);

        Assert.Equal(reader.UserId, deleted.UserId);
        var users = await _repository.GetUsersAsync();
        Assert.Single(users);
    }

    [Fact]
    public async Task DeleteUserAsync_On_First_Account_Throws()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.DeleteUserAsync(admin.UserId));
    }

    [Fact]
    public async Task DeleteUserAsync_On_Unknown_User_Throws()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.DeleteUserAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkForPasswordResetAsync_Sets_ForcePasswordReset_And_Clears_Lock()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");

        var marked = await _repository.MarkForPasswordResetAsync(admin.UserId);

        Assert.True(marked.ForcePasswordReset);
        Assert.False(marked.IsLocked);
    }

    [Fact]
    public async Task SetPasswordAsync_Updates_Password_And_Clears_ForcePasswordReset()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password");
        await _repository.MarkForPasswordResetAsync(admin.UserId);

        var username = await _repository.SetPasswordAsync(admin.UserId, "new-password");

        Assert.Equal("admin-one", username);
        var validated = await _repository.ValidateCredentialsAsync("admin-one", "new-password");
        Assert.NotNull(validated);
        Assert.False(validated!.ForcePasswordReset);
    }

    [Fact]
    public async Task SetPasswordAsync_On_Unknown_User_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.SetPasswordAsync(Guid.NewGuid(), "new-password"));
    }

    [Fact]
    public async Task GetUsernameAsync_Returns_Null_For_Unknown_User()
    {
        var username = await _repository.GetUsernameAsync(Guid.NewGuid());

        Assert.Null(username);
    }

    [Fact]
    public async Task ReadUsers_With_Corrupted_Credential_File_Throws_And_Quarantines()
    {
        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var credentialFilePath = Path.Combine(dataDirectory, "user.cred");
        await File.WriteAllTextAsync(credentialFilePath, "not-a-protected-payload");

        await Assert.ThrowsAsync<CorruptedDataException>(() => _repository.HasAccountAsync());

        var corruptDirectory = Path.Combine(dataDirectory, "corrupt");
        Assert.True(Directory.Exists(corruptDirectory));
        Assert.NotEmpty(Directory.GetFiles(corruptDirectory));
    }
}
```

- [ ] **Step 4: Write the failing test file for `JsonPasswordResetTokenRepository`**

```csharp
// BookWheel.Tests/Storage/JsonPasswordResetTokenRepositoryTests.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace BookWheel.Tests.Storage;

public sealed class JsonPasswordResetTokenRepositoryTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly JsonPasswordResetTokenRepository _repository;

    public JsonPasswordResetTokenRepositoryTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-reset-token-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        var environment = StorageTestEnvironment.Create(_contentRoot);
        _dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_contentRoot, "keys")));
        _repository = new JsonPasswordResetTokenRepository(environment, _dataProtectionProvider);
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
    public async Task CreateAsync_Returns_Token_That_Validates_Successfully()
    {
        var userId = Guid.NewGuid();

        var (rawToken, expiresAtUtc) = await _repository.CreateAsync(userId);

        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        Assert.True(expiresAtUtc > DateTimeOffset.UtcNow);

        var lookup = await _repository.ValidateAsync(rawToken);
        Assert.True(lookup.IsValid);
        Assert.Equal(userId, lookup.UserId);
    }

    [Fact]
    public async Task CreateAsync_For_Same_User_Invalidates_Previous_Token()
    {
        var userId = Guid.NewGuid();
        var (firstToken, _) = await _repository.CreateAsync(userId);

        var (secondToken, _) = await _repository.CreateAsync(userId);

        var firstLookup = await _repository.ValidateAsync(firstToken);
        var secondLookup = await _repository.ValidateAsync(secondToken);
        Assert.False(firstLookup.IsValid);
        Assert.True(secondLookup.IsValid);
    }

    [Fact]
    public async Task CompleteAsync_Consumes_Token_So_It_Cannot_Be_Reused()
    {
        var userId = Guid.NewGuid();
        var (rawToken, _) = await _repository.CreateAsync(userId);

        var completedUserId = await _repository.CompleteAsync(rawToken);

        Assert.Equal(userId, completedUserId);
        var lookupAfterComplete = await _repository.ValidateAsync(rawToken);
        Assert.False(lookupAfterComplete.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_With_Unknown_Token_Returns_Invalid()
    {
        var lookup = await _repository.ValidateAsync("not-a-real-token");

        Assert.False(lookup.IsValid);
    }

    [Fact]
    public async Task CompleteAsync_With_Unknown_Token_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CompleteAsync("not-a-real-token"));
    }

    [Fact]
    public async Task ValidateAsync_With_Expired_Token_Returns_Invalid()
    {
        var userId = Guid.NewGuid();
        const string rawToken = "expired-test-token";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var expiredDocumentJson = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Tokens = new[]
            {
                new
                {
                    UserId = userId,
                    TokenHash = tokenHash,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
                }
            }
        });

        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var tokenFilePath = Path.Combine(dataDirectory, "password-reset-tokens.dat");
        var protector = _dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
        await File.WriteAllTextAsync(tokenFilePath, protector.Protect(expiredDocumentJson));

        var lookup = await _repository.ValidateAsync(rawToken);

        Assert.False(lookup.IsValid);
    }

    [Fact]
    public async Task ReadTokens_With_Corrupted_Token_File_Throws_And_Quarantines()
    {
        var dataDirectory = Path.Combine(_contentRoot, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var tokenFilePath = Path.Combine(dataDirectory, "password-reset-tokens.dat");
        await File.WriteAllTextAsync(tokenFilePath, "not-a-protected-payload");

        await Assert.ThrowsAsync<CorruptedDataException>(() => _repository.ValidateAsync("any-token"));

        var corruptDirectory = Path.Combine(dataDirectory, "corrupt");
        Assert.True(Directory.Exists(corruptDirectory));
        Assert.NotEmpty(Directory.GetFiles(corruptDirectory));
    }
}
```

- [ ] **Step 5: Run the tests and confirm they fail to build**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~JsonCredentialRepositoryTests|FullyQualifiedName~JsonPasswordResetTokenRepositoryTests"`
Expected: Build FAILS — `JsonCredentialRepository` and `JsonPasswordResetTokenRepository` do not exist yet (CS0246).

- [ ] **Step 6: Create `JsonCredentialRepository`**

```csharp
// BookWheel/Storage/JsonCredentialRepository.cs
using System.Text.Json;
using System.Security.Cryptography;
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BookWheel.Storage;

public sealed class JsonCredentialRepository : ICredentialRepository
{
    private const int CurrentCredentialSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly PasswordHasher<string> PasswordHasher = new();

    private readonly string _dataDirectory;
    private readonly string _corruptDataDirectory;
    private readonly string _credentialFilePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public sealed class CredentialMigrationResult
    {
        public bool Migrated { get; set; }
        public int UsersAffected { get; set; }
    }

    private sealed class CredentialDocument
    {
        public int SchemaVersion { get; set; } = CurrentCredentialSchemaVersion;
        public List<CredentialRecord> Users { get; set; } = [];
    }

    public JsonCredentialRepository(IWebHostEnvironment environment, IDataProtectionProvider dataProtectionProvider)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _corruptDataDirectory = Path.Combine(_dataDirectory, "corrupt");
        _credentialFilePath = Path.Combine(_dataDirectory, "user.cred");
        _protector = dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
    }

    public async Task<bool> HasAccountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            return users.Count > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasLegacyPayloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var json = await ReadProtectedCredentialJsonUnsafeAsync();
            if (json is null)
            {
                return false;
            }

            if (IsCurrentCredentialDocument(json))
            {
                return false;
            }

            return TryDeserialize<List<CredentialRecord>>(json) is not null
                || TryDeserialize<CredentialRecord>(json) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialMigrationResult> MigrateLegacyPayloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var json = await ReadProtectedCredentialJsonUnsafeAsync();
            if (json is null)
            {
                return new CredentialMigrationResult();
            }

            if (IsCurrentCredentialDocument(json))
            {
                return new CredentialMigrationResult();
            }

            List<CredentialRecord>? users = TryDeserialize<List<CredentialRecord>>(json);
            if (users is null)
            {
                var singleUser = TryDeserialize<CredentialRecord>(json);
                users = singleUser is null ? [] : [singleUser];
            }

            if (users.Count == 0)
            {
                return new CredentialMigrationResult();
            }

            var adminFound = false;
            for (var index = 0; index < users.Count; index++)
            {
                if (users[index].UserId == Guid.Empty)
                {
                    users[index].UserId = Guid.NewGuid();
                }

                if (users[index].CreatedAtUtc == default)
                {
                    users[index].CreatedAtUtc = DateTimeOffset.UtcNow;
                }

                if (users[index].IsAdmin)
                {
                    adminFound = true;
                }
            }

            if (!adminFound)
            {
                users[0].IsAdmin = true;
            }

            await WriteUsersUnsafeAsync(users);
            return new CredentialMigrationResult
            {
                Migrated = true,
                UsersAffected = users.Count
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialRecord> CreateInitialAccountAsync(string username, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            if (users.Count > 0)
            {
                throw new InvalidOperationException("An account already exists.");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Username and password are required.");
            }

            var normalizedUsername = username.Trim();

            var record = new CredentialRecord
            {
                UserId = Guid.NewGuid(),
                Username = normalizedUsername,
                PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
                IsAdmin = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(record);
            await WriteUsersUnsafeAsync(users);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var normalizedUsername = username.Trim();
            var record = users.FirstOrDefault(user =>
                string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (record is null)
            {
                return null;
            }

            var result = PasswordHasher.VerifyHashedPassword(record.Username, record.PasswordHash, password);
            return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded
                ? record
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            return users
                .OrderBy(user => user.CreatedAtUtc)
                .Select(ToSummary)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            if (users.Count == 0)
            {
                throw new InvalidOperationException("Create the initial account first.");
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("Username is required.");
            }

            var normalizedUsername = username.Trim();
            if (users.Any(user => string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var record = new CredentialRecord
            {
                UserId = Guid.NewGuid(),
                Username = normalizedUsername,
                PasswordHash = PasswordHasher.HashPassword(normalizedUsername, GenerateTemporaryPassword()),
                IsAdmin = isAdmin,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(record);
            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var normalizedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                throw new InvalidOperationException("Username is required.");
            }

            var duplicateUser = users.FirstOrDefault(user =>
                user.UserId != userId && string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (duplicateUser is not null)
            {
                throw new InvalidOperationException("Username already exists.");
            }

            if (!isAdmin)
            {
                var adminCount = users.Count(user => user.IsAdmin);
                if (record.IsAdmin && adminCount <= 1)
                {
                    throw new InvalidOperationException("At least one administrator account is required.");
                }
            }

            record.Username = normalizedUsername;
            record.IsAdmin = isAdmin;

            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var normalizedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                throw new InvalidOperationException("Username is required.");
            }

            var duplicateUser = users.FirstOrDefault(user =>
                user.UserId != userId && string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (duplicateUser is not null)
            {
                throw new InvalidOperationException("Username already exists.");
            }

            if (!isAdmin)
            {
                var adminCount = users.Count(user => user.IsAdmin);
                if (record.IsAdmin && adminCount <= 1)
                {
                    throw new InvalidOperationException("At least one administrator account is required.");
                }
            }

            record.Username = normalizedUsername;
            record.IsAdmin = isAdmin;
            record.IsDisabled = isDisabled;
            record.ForcePasswordReset = forcePasswordReset;
            record.IsLocked = isLocked;
            record.LockedUntilUtc = isLocked ? DateTimeOffset.UtcNow.AddHours(12) : null;

            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccountSummary> DeleteUserAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var firstUser = users
                .OrderBy(user => user.CreatedAtUtc)
                .FirstOrDefault();

            if (firstUser is not null && firstUser.UserId == userId)
            {
                throw new InvalidOperationException("The first account cannot be removed.");
            }

            users.RemoveAll(user => user.UserId == userId);
            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var user = users.FirstOrDefault(existingUser => existingUser.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            user.ForcePasswordReset = true;
            user.IsLocked = false;
            user.LockedUntilUtc = null;

            await WriteUsersUnsafeAsync(users);
            return user;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> SetPasswordAsync(Guid userId, string newPassword)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var user = users.FirstOrDefault(existingUser => existingUser.UserId == userId)
                ?? throw new InvalidOperationException("User not found for this reset link.");

            user.PasswordHash = PasswordHasher.HashPassword(user.Username, newPassword);
            user.ForcePasswordReset = false;
            user.IsLocked = false;
            user.LockedUntilUtc = null;

            await WriteUsersUnsafeAsync(users);
            return user.Username;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetUsernameAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            return users.FirstOrDefault(user => user.UserId == userId)?.Username;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<CredentialRecord>> ReadUsersUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_credentialFilePath))
        {
            return [];
        }

        var protectedPayload = await File.ReadAllTextAsync(_credentialFilePath);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return [];
        }

        string json;
        try
        {
            json = _protector.Unprotect(protectedPayload);
        }
        catch (Exception)
        {
            QuarantineCorruptFileUnsafe(_credentialFilePath, "user.cred");
            throw new CorruptedDataException("Credential data is corrupted and has been quarantined. Restore App_Data from backup.");
        }

        var document = TryDeserialize<CredentialDocument>(json);
        if (document?.Users is { Count: > 0 })
        {
            return document.Users;
        }

        var users = TryDeserialize<List<CredentialRecord>>(json);
        if (users is { Count: > 0 })
        {
            return users;
        }

        var legacy = TryDeserialize<CredentialRecord>(json);
        if (legacy is null)
        {
            QuarantineCorruptFileUnsafe(_credentialFilePath, "user.cred");
            throw new CorruptedDataException("Credential data is corrupted and has been quarantined. Restore App_Data from backup.");
        }

        if (legacy.UserId == Guid.Empty)
        {
            legacy.UserId = Guid.NewGuid();
        }

        if (legacy.CreatedAtUtc == default)
        {
            legacy.CreatedAtUtc = DateTimeOffset.UtcNow;
        }

        legacy.IsAdmin = true;
        return [legacy];
    }

    private async Task<string?> ReadProtectedCredentialJsonUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_credentialFilePath))
        {
            return null;
        }

        var protectedPayload = await File.ReadAllTextAsync(_credentialFilePath);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedPayload);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private async Task WriteUsersUnsafeAsync(List<CredentialRecord> users)
    {
        Directory.CreateDirectory(_dataDirectory);

        var json = JsonSerializer.Serialize(new CredentialDocument { Users = users }, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        await File.WriteAllTextAsync(_credentialFilePath, protectedPayload);
    }

    private static UserAccountSummary ToSummary(CredentialRecord record)
    {
        return new UserAccountSummary
        {
            UserId = record.UserId,
            Username = record.Username,
            IsAdmin = record.IsAdmin,
            IsDisabled = record.IsDisabled,
            ForcePasswordReset = record.ForcePasswordReset,
            IsLocked = record.IsLocked,
            LockedUntilUtc = record.LockedUntilUtc,
            CreatedAtUtc = record.CreatedAtUtc
        };
    }

    private void QuarantineCorruptFileUnsafe(string sourcePath, string fileNamePrefix)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(_corruptDataDirectory);
        var quarantineName = $"{fileNamePrefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt";
        var quarantinePath = Path.Combine(_corruptDataDirectory, quarantineName);
        File.Move(sourcePath, quarantinePath, overwrite: true);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string GenerateTemporaryPassword()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    }

    private static bool IsCurrentCredentialDocument(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Users", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 7: Create `JsonPasswordResetTokenRepository`**

```csharp
// BookWheel/Storage/JsonPasswordResetTokenRepository.cs
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.AspNetCore.DataProtection;

namespace BookWheel.Storage;

public sealed class JsonPasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private const int CurrentResetTokenSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _corruptDataDirectory;
    private readonly string _passwordResetTokenFilePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class PasswordResetTokenDocument
    {
        public int SchemaVersion { get; set; } = CurrentResetTokenSchemaVersion;
        public List<PasswordResetTokenRecord> Tokens { get; set; } = [];
    }

    public JsonPasswordResetTokenRepository(IWebHostEnvironment environment, IDataProtectionProvider dataProtectionProvider)
    {
        _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        _corruptDataDirectory = Path.Combine(_dataDirectory, "corrupt");
        _passwordResetTokenFilePath = Path.Combine(_dataDirectory, "password-reset-tokens.dat");
        _protector = dataProtectionProvider.CreateProtector("BookWheel.Credentials.v1");
    }

    public async Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            var tokens = await ReadTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now || existingToken.UserId == userId);

            var rawToken = GenerateResetToken();
            var expiresAtUtc = now.AddHours(24);
            tokens.Add(new PasswordResetTokenRecord
            {
                UserId = userId,
                TokenHash = HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            });

            await WriteTokensUnsafeAsync(tokens);
            return (rawToken, expiresAtUtc);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PasswordResetTokenLookup> ValidateAsync(string token)
    {
        await _gate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new PasswordResetTokenLookup { IsValid = false };
            }

            var tokens = await ReadTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now);

            var tokenHash = HashToken(token.Trim());
            var matchingToken = tokens.FirstOrDefault(existingToken => existingToken.TokenHash == tokenHash);

            await WriteTokensUnsafeAsync(tokens);

            return matchingToken is null
                ? new PasswordResetTokenLookup { IsValid = false }
                : new PasswordResetTokenLookup
                {
                    IsValid = true,
                    UserId = matchingToken.UserId,
                    ExpiresAtUtc = matchingToken.ExpiresAtUtc
                };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Guid> CompleteAsync(string token)
    {
        await _gate.WaitAsync();
        try
        {
            var tokens = await ReadTokensUnsafeAsync();
            var now = DateTimeOffset.UtcNow;
            tokens.RemoveAll(existingToken => existingToken.ExpiresAtUtc <= now);

            var tokenHash = HashToken((token ?? string.Empty).Trim());
            var matchingToken = tokens.FirstOrDefault(existingToken => existingToken.TokenHash == tokenHash)
                ?? throw new InvalidOperationException("The password reset link is invalid or has expired.");

            tokens.RemoveAll(existingToken => existingToken.TokenHash == tokenHash);
            await WriteTokensUnsafeAsync(tokens);

            return matchingToken.UserId;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PasswordResetTokenRecord>> ReadTokensUnsafeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_passwordResetTokenFilePath))
        {
            return [];
        }

        var protectedPayload = await File.ReadAllTextAsync(_passwordResetTokenFilePath);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return [];
        }

        string json;
        try
        {
            json = _protector.Unprotect(protectedPayload);
        }
        catch (Exception)
        {
            QuarantineCorruptFileUnsafe(_passwordResetTokenFilePath, "password-reset-tokens.dat");
            throw new CorruptedDataException("Password reset token data is corrupted and has been quarantined. Restore App_Data from backup.");
        }

        var tokenDocument = TryDeserialize<PasswordResetTokenDocument>(json);
        if (tokenDocument?.Tokens is { Count: >= 0 })
        {
            return tokenDocument.Tokens;
        }

        var tokens = TryDeserialize<List<PasswordResetTokenRecord>>(json);
        return tokens ?? [];
    }

    private async Task WriteTokensUnsafeAsync(List<PasswordResetTokenRecord> tokens)
    {
        Directory.CreateDirectory(_dataDirectory);

        var json = JsonSerializer.Serialize(new PasswordResetTokenDocument { Tokens = tokens }, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        await File.WriteAllTextAsync(_passwordResetTokenFilePath, protectedPayload);
    }

    private void QuarantineCorruptFileUnsafe(string sourcePath, string fileNamePrefix)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(_corruptDataDirectory);
        var quarantineName = $"{fileNamePrefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt";
        var quarantinePath = Path.Combine(_corruptDataDirectory, quarantineName);
        File.Move(sourcePath, quarantinePath, overwrite: true);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 8: Run the tests and confirm they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~JsonCredentialRepositoryTests|FullyQualifiedName~JsonPasswordResetTokenRepositoryTests"`
Expected: All PASS (14 credential tests + 7 token tests).

- [ ] **Step 9: Commit**

```bash
git add BookWheel/Storage/ICredentialRepository.cs BookWheel/Storage/IPasswordResetTokenRepository.cs BookWheel/Storage/JsonCredentialRepository.cs BookWheel/Storage/JsonPasswordResetTokenRepository.cs BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs BookWheel.Tests/Storage/JsonPasswordResetTokenRepositoryTests.cs
git commit -m "Add ICredentialRepository/IPasswordResetTokenRepository with unit tests (#14)"
```

---

### Task 4: Wire credential and password-reset-token repositories into the application

**Files:**
- Modify: `BookWheel/Services/AuthService.cs`
- Modify: `BookWheel/Controllers/UsersController.cs`
- Modify: `BookWheel/Services/DataMigrationService.cs`
- Modify: `BookWheel/Program.cs`
- Delete: `BookWheel/Services/CredentialStore.cs`

**Interfaces:**
- Consumes: `ICredentialRepository`, `IPasswordResetTokenRepository`, `JsonCredentialRepository`, `PasswordResetTokenLookup` from Task 3.
- Produces: `AuthService.CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl)` returning `(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)`, consumed by `UsersController`.

- [ ] **Step 1: Rewrite `AuthService.cs` to depend on the split repositories and own the password-reset orchestration**

```csharp
// BookWheel/Services/AuthService.cs
using System.Collections.Concurrent;
using BookWheel.Models;
using BookWheel.Storage;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class AuthService
{
    private readonly ICredentialRepository _credentialRepository;
    private readonly IPasswordResetTokenRepository _resetTokenRepository;
    private readonly SecurityOptions _securityOptions;
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, FailedLoginRecord> _failedLogins = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);

    private sealed class FailedLoginRecord
    {
        public int Count { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
    }

    private sealed class SessionRecord
    {
        public AuthenticatedUser User { get; set; } = new();
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    public AuthService(ICredentialRepository credentialRepository, IPasswordResetTokenRepository resetTokenRepository, IOptions<SecurityOptions> securityOptions)
    {
        _credentialRepository = credentialRepository;
        _resetTokenRepository = resetTokenRepository;
        _securityOptions = securityOptions.Value;
    }

    public Task<bool> HasAccountAsync()
    {
        return _credentialRepository.HasAccountAsync();
    }

    public async Task<AuthenticatedUser> CreateAccountAsync(string username, string password)
    {
        var user = await _credentialRepository.CreateInitialAccountAsync(username, password);
        return ToAuthenticatedUser(user);
    }

    public async Task<LoginValidationResult> ValidateCredentialsAsync(string username, string password)
    {
        var normalizedUsername = username.Trim();
        var failedRecord = _failedLogins.GetOrAdd(normalizedUsername, _ => new FailedLoginRecord());
        if (failedRecord.LockedUntilUtc.HasValue && failedRecord.LockedUntilUtc.Value > DateTimeOffset.UtcNow)
        {
            return new LoginValidationResult
            {
                IsLockedOut = true,
                LockoutEndsAtUtc = failedRecord.LockedUntilUtc
            };
        }

        var user = await _credentialRepository.ValidateCredentialsAsync(username, password);
        if (user is null)
        {
            failedRecord.Count += 1;
            var count = failedRecord.Count;
            var threshold = Math.Max(2, _securityOptions.UsernameLockoutThreshold);
            if (count >= threshold)
            {
                var lockoutDuration = TimeSpan.FromMinutes(Math.Max(1, _securityOptions.UsernameLockoutMinutes));
                failedRecord.LockedUntilUtc = DateTimeOffset.UtcNow.Add(lockoutDuration);
                failedRecord.Count = 0;
                return new LoginValidationResult
                {
                    IsLockedOut = true,
                    IsInvalidCredentials = true,
                    LockoutTriggered = true,
                    LockoutEndsAtUtc = failedRecord.LockedUntilUtc
                };
            }

            return new LoginValidationResult { IsInvalidCredentials = true };
        }

        _failedLogins.TryRemove(normalizedUsername, out _);

        if (user.IsDisabled)
        {
            return new LoginValidationResult { IsDisabled = true };
        }

        if (user.IsLocked && user.LockedUntilUtc.GetValueOrDefault(DateTimeOffset.MaxValue) > DateTimeOffset.UtcNow)
        {
            return new LoginValidationResult
            {
                IsLockedOut = true,
                LockoutEndsAtUtc = user.LockedUntilUtc
            };
        }

        if (user.ForcePasswordReset)
        {
            return new LoginValidationResult { RequiresPasswordReset = true };
        }

        return new LoginValidationResult { User = ToAuthenticatedUser(user) };
    }

    public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)> CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl)
    {
        var user = await _credentialRepository.MarkForPasswordResetAsync(userId);
        var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(userId);

        var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
        var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";
        return (resetLink, expiresAtUtc, user.Username);
    }

    public async Task<string> CompletePasswordResetAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
        {
            throw new InvalidOperationException("A valid token and password are required.");
        }

        var lookup = await _resetTokenRepository.ValidateAsync(token);
        if (!lookup.IsValid)
        {
            throw new InvalidOperationException("The password reset link is invalid or has expired.");
        }

        var username = await _credentialRepository.SetPasswordAsync(lookup.UserId, newPassword);
        await _resetTokenRepository.CompleteAsync(token);
        return username;
    }

    public async Task<PasswordResetTokenValidationResult> ValidatePasswordResetTokenAsync(string token)
    {
        var lookup = await _resetTokenRepository.ValidateAsync(token);
        if (!lookup.IsValid)
        {
            return new PasswordResetTokenValidationResult { IsValid = false };
        }

        var username = await _credentialRepository.GetUsernameAsync(lookup.UserId);
        if (username is null)
        {
            return new PasswordResetTokenValidationResult { IsValid = false };
        }

        return new PasswordResetTokenValidationResult
        {
            IsValid = true,
            Username = username,
            ExpiresAtUtc = lookup.ExpiresAtUtc
        };
    }

    public string CreateSession(AuthenticatedUser user)
    {
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        _sessions[token] = new SessionRecord
        {
            User = user,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_sessionLifetime)
        };
        return token;
    }

    public bool IsAuthenticated(HttpContext context)
    {
        return GetAuthenticatedUser(context) is not null;
    }

    public bool IsAdmin(HttpContext context)
    {
        return GetAuthenticatedUser(context)?.IsAdmin == true;
    }

    public AuthenticatedUser? GetAuthenticatedUser(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue("BookWheel.Auth", out var token))
        {
            return null;
        }

        if (!_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (session.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        session.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_sessionLifetime);
        _sessions[token] = session;
        return session.User;
    }

    public void SignOut(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue("BookWheel.Auth", out var token))
        {
            _sessions.TryRemove(token, out _);
        }

        context.Response.Cookies.Delete("BookWheel.Auth", new CookieOptions { Path = "/" });
    }

    public void RemoveSessionsForUser(Guid userId)
    {
        var sessionTokens = _sessions
            .Where(entry => entry.Value.User.UserId == userId)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var token in sessionTokens)
        {
            _sessions.TryRemove(token, out _);
        }
    }

    private static AuthenticatedUser ToAuthenticatedUser(CredentialRecord credential)
    {
        return new AuthenticatedUser
        {
            UserId = credential.UserId,
            Username = credential.Username,
            IsAdmin = credential.IsAdmin
        };
    }
}
```

- [ ] **Step 2: Update `UsersController.cs`**

```csharp
// BookWheel/Controllers/UsersController.cs
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IBookRepository _bookStore;
    private readonly ILogger<UsersController> _logger;

    public UsersController(AuthService authService, ICredentialRepository credentialRepository, IBookRepository bookStore, ILogger<UsersController> logger)
    {
        _authService = authService;
        _credentialRepository = credentialRepository;
        _bookStore = bookStore;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var users = await _credentialRepository.GetUsersAsync();
        return Ok(new { users });
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var user = await _credentialRepository.CreateUserAsync(request.Username, request.IsAdmin);
            var appBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var setupLink = await _authService.CreatePasswordResetLinkAsync(user.UserId, appBaseUrl);
            _logger.LogInformation(
                "User account created with setup link. Actor {ActorUsername} target {TargetUsername} role {IsAdmin} request {RequestId}",
                currentUser.Username,
                user.Username,
                user.IsAdmin,
                HttpContext.TraceIdentifier);
            return Ok(new
            {
                user.UserId,
                user.Username,
                user.IsAdmin,
                user.IsDisabled,
                user.ForcePasswordReset,
                user.IsLocked,
                user.LockedUntilUtc,
                user.CreatedAtUtc,
                setupLink = setupLink.ResetLink,
                setupLinkExpiresAtUtc = setupLink.ExpiresAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserAccountRequest request)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (id == currentUser.UserId)
        {
            return BadRequest(new { message = "Administrators can only update other user accounts." });
        }

        try
        {
            var before = (await _credentialRepository.GetUsersAsync()).FirstOrDefault(user => user.UserId == id);
            var user = await _credentialRepository.UpdateUserAsync(id, request.Username, request.IsAdmin, request.IsDisabled, request.ForcePasswordReset, request.IsLocked);
            if (before is not null)
            {
                if (before.IsAdmin != user.IsAdmin)
                {
                    _logger.LogInformation(
                        "Role changed. Actor {ActorUsername} target {TargetUsername} from {OldIsAdmin} to {NewIsAdmin} request {RequestId}",
                        currentUser.Username,
                        user.Username,
                        before.IsAdmin,
                        user.IsAdmin,
                        HttpContext.TraceIdentifier);
                }

                if (before.IsDisabled != user.IsDisabled || before.IsLocked != user.IsLocked || before.ForcePasswordReset != user.ForcePasswordReset)
                {
                    _logger.LogInformation(
                        "Account security state changed. Actor {ActorUsername} target {TargetUsername} disabled {IsDisabled} locked {IsLocked} forceReset {ForcePasswordReset} request {RequestId}",
                        currentUser.Username,
                        user.Username,
                        user.IsDisabled,
                        user.IsLocked,
                        user.ForcePasswordReset,
                        HttpContext.TraceIdentifier);
                }
            }

            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/password-reset-link")]
    public async Task<IActionResult> CreatePasswordResetLink(Guid id)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (id == currentUser.UserId)
        {
            return BadRequest(new { message = "Administrators can only generate reset links for other user accounts." });
        }

        try
        {
            var appBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var result = await _authService.CreatePasswordResetLinkAsync(id, appBaseUrl);
            _logger.LogInformation(
                "Forced password reset link generated. Actor {ActorUsername} target {TargetUsername} request {RequestId}",
                currentUser.Username,
                result.Username,
                HttpContext.TraceIdentifier);
            return Ok(new
            {
                username = result.Username,
                resetLink = result.ResetLink,
                expiresAtUtc = result.ExpiresAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var currentUser = _authService.GetAuthenticatedUser(HttpContext);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (id == currentUser.UserId)
        {
            return BadRequest(new { message = "Administrators can only remove other user accounts." });
        }

        try
        {
            var deletedUser = await _credentialRepository.DeleteUserAsync(id);
            var removedBooks = await _bookStore.RemoveUserDataAsync(id);
            _authService.RemoveSessionsForUser(id);
            _logger.LogInformation(
                "User account deleted. Actor {ActorUsername} target {TargetUsername} removed books {RemovedBooks} request {RequestId}",
                currentUser.Username,
                deletedUser.Username,
                removedBooks,
                HttpContext.TraceIdentifier);
            return Ok(new
            {
                userId = deletedUser.UserId,
                username = deletedUser.Username,
                removedBooks
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 3: Update `DataMigrationService.cs`'s credential dependency**

```csharp
// BookWheel/Services/DataMigrationService.cs
using BookWheel.Models;
using BookWheel.Storage;

namespace BookWheel.Services;

public sealed class DataMigrationService
{
    private readonly JsonCredentialRepository _credentialRepository;
    private readonly JsonBookRepository _bookRepository;

    public DataMigrationService(JsonCredentialRepository credentialRepository, JsonBookRepository bookRepository)
    {
        _credentialRepository = credentialRepository;
        _bookRepository = bookRepository;
    }

    public async Task<DataMigrationStatus> GetStatusAsync()
    {
        return new DataMigrationStatus
        {
            HasLegacyCredentialPayload = await _credentialRepository.HasLegacyPayloadAsync(),
            HasLegacyBooksPayload = await _bookRepository.HasLegacyPayloadAsync()
        };
    }

    public async Task<DataMigrationReport> RunAsync()
    {
        var credentials = await _credentialRepository.MigrateLegacyPayloadAsync();
        var users = await _credentialRepository.GetUsersAsync();
        var booksOwnerId = users.OrderBy(user => user.CreatedAtUtc).Select(user => user.UserId).FirstOrDefault();
        var resolvedOwner = booksOwnerId == Guid.Empty ? (Guid?)null : booksOwnerId;
        var books = await _bookRepository.MigrateLegacyPayloadAsync(resolvedOwner);

        return new DataMigrationReport
        {
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            CredentialPayloadMigrated = credentials.Migrated,
            CredentialUsersAffected = credentials.UsersAffected,
            BooksPayloadMigrated = books.Migrated,
            BooksAffected = books.BooksAffected,
            BooksOwnerUserId = books.BooksOwnerUserId,
            Message = !credentials.Migrated && !books.Migrated
                ? "No legacy payloads required migration."
                : "Legacy payload migration completed."
        };
    }
}
```

- [ ] **Step 4: Update `Program.cs` DI registrations to remove `CredentialStore` and add the split repositories**

Replace the registration block left over from Task 2 Step 1:

```csharp
// BookWheel/Program.cs — before (as it stands after Task 2)
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppMetricsService>();
builder.Services.AddSingleton<CredentialStore>();

builder.Services.AddSingleton<JsonBookRepository>();
builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<JsonBookRepository>());

builder.Services.AddSingleton<DataMigrationService>();
```

```csharp
// BookWheel/Program.cs — after
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppMetricsService>();

builder.Services.AddSingleton<JsonBookRepository>();
builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<JsonBookRepository>());

builder.Services.AddSingleton<JsonCredentialRepository>();
builder.Services.AddSingleton<ICredentialRepository>(sp => sp.GetRequiredService<JsonCredentialRepository>());

builder.Services.AddSingleton<JsonPasswordResetTokenRepository>();
builder.Services.AddSingleton<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<JsonPasswordResetTokenRepository>());

builder.Services.AddSingleton<DataMigrationService>();
```

- [ ] **Step 5: Delete the old `CredentialStore.cs`**

```bash
git rm BookWheel/Services/CredentialStore.cs
```

- [ ] **Step 6: Build the solution**

Run: `dotnet build BookWheel.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test BookWheel.slnx`
Expected: All tests PASS, including every existing test in `BookWheelApiTests.cs` (in particular `Password_Reset_Link_Can_Be_Generated_And_Used_Once`, the last-admin-protection test, and the first-account-protection test) and every new `BookWheel.Tests/Storage/*` test.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Wire ICredentialRepository/IPasswordResetTokenRepository through AuthService, controllers, and DI (#14)"
```

---

### Task 5: Update documentation

**Files:**
- Modify: `README.md`
- Modify: `IMPROVEMENT_ROADMAP.md`
- Modify: `SECURITY_AUDIT_REPORT.md` (only if a stale reference is found)

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing consumed by other tasks.

- [ ] **Step 1: Update the README "Solution Structure" tree**

```text
# README.md — before (lines 54-71)
Book Wheel/
  BookWheel.slnx
  README.md
  BookWheel/
    BookWheel.csproj
    Program.cs
    appsettings.json
    App_Data/
    Controllers/
    Models/
    Services/
    wwwroot/
  BookWheel.Tests/
    BookWheel.Tests.csproj
    BookWheelApiTests.cs
    BookWheelWebAppFactory.cs
```

```text
# README.md — after
Book Wheel/
  BookWheel.slnx
  README.md
  BookWheel/
    BookWheel.csproj
    Program.cs
    appsettings.json
    App_Data/
    Controllers/
    Models/
    Services/
    Storage/
    wwwroot/
  BookWheel.Tests/
    BookWheel.Tests.csproj
    BookWheelApiTests.cs
    BookWheelWebAppFactory.cs
    Storage/
```

Use the Edit tool to replace the `Services/` / `wwwroot/` block and the `BookWheelWebAppFactory.cs` line with the versions above (add the two new lines, keep everything else identical).

- [ ] **Step 2: Update `IMPROVEMENT_ROADMAP.md`**

Add one bullet to "Current Strengths" (after the "Persistent storage for books, credentials, logs, and Data Protection keys" line):

```markdown
- Storage CRUD operations are abstracted behind repository interfaces (JSON-backed today), so a SQL/NoSQL backend can be swapped in later without touching controllers or services
```

Add one new item to the end of the numbered list under "Priority 5: Reliability and Data Management" (after item 5, "[Done] Add health checks for storage, logging, and app readiness."):

```markdown
6. [Done] Abstract storage CRUD operations behind repository interfaces so the JSON-file backend can be swapped for SQL/NoSQL without touching business logic (#14).
```

- [ ] **Step 3: Check `SECURITY_AUDIT_REPORT.md` for stale type references**

Run: `grep -n "BookStore\|CredentialStore" SECURITY_AUDIT_REPORT.md`
Expected: No matches (confirmed during planning — the report does not name these types today). If a match is found, replace `BookStore` with `JsonBookRepository` and `CredentialStore` with `JsonCredentialRepository`/`JsonPasswordResetTokenRepository` as appropriate for the surrounding context, preserving the sentence's meaning.

- [ ] **Step 4: Commit**

```bash
git add README.md IMPROVEMENT_ROADMAP.md SECURITY_AUDIT_REPORT.md
git commit -m "Document storage layer abstraction in README and roadmap (#14)"
```

(If Step 3 found no changes needed, omit `SECURITY_AUDIT_REPORT.md` from the `git add`.)

---

### Task 6: Final full verification

**Files:**
- None (verification only).

**Interfaces:**
- Consumes: the complete solution from Tasks 1-5.
- Produces: nothing.

- [ ] **Step 1: Build the full solution**

Run: `dotnet build BookWheel.slnx`
Expected: Build succeeds with no errors or warnings introduced by this change.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test BookWheel.slnx`
Expected: Every test passes — the full pre-existing `BookWheelApiTests.cs` suite plus all new tests in `BookWheel.Tests/Storage/`.

- [ ] **Step 3: Confirm no stray files and a clean working tree**

Run: `git status --short`
Expected: Empty output (everything from Tasks 1-5 already committed on branch `14`).

- [ ] **Step 4: Confirm `BookStore`/`CredentialStore` no longer exist anywhere in the solution**

Run: `grep -rn "BookStore\b\|CredentialStore\b" BookWheel BookWheel.Tests --include=*.cs`
Expected: No matches (both types were fully replaced by `JsonBookRepository`, `JsonCredentialRepository`, and `JsonPasswordResetTokenRepository` in Tasks 2 and 4).
