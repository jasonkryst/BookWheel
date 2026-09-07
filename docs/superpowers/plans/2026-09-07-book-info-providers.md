# Book Info Providers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Google Books as a second book-info provider alongside Open Library, with a per-user provider preference, a `book_info_providers` lookup table, provider stamping on books, and server-side storage for that preference plus the existing theme/analytics-consent preferences.

**Architecture:** A `book_info_providers` lookup table (mirrors the existing `book_types` pattern — no DB-level FK, just an indexed nullable `int` column on `books`). A new `GoogleBooksBookMetadataLookupService` implements the existing `IBookMetadataLookupService` interface alongside `OpenLibraryBookMetadataLookupService`. A new `BookMetadataLookupDispatcher` (plain class, constructor-injected with both providers as `IBookMetadataLookupService`) tries the user's preferred provider first and falls back to the other on failure/empty result; `BooksController` depends on the dispatcher instead of the interface directly. Three new nullable/defaulted columns on `users` (`Theme`, `AnalyticsConsentOptedOut`, `PreferredBookInfoProviderId`) back a new `GET`/`PUT /api/preferences` endpoint; the frontend still uses `localStorage` as a pre-login/pre-init paint cache (unchanged), but the server becomes the source of truth once authenticated.

**Tech Stack:** ASP.NET Core 8 (C#), EF Core 9 + Npgsql (PostgreSQL), xUnit + Testcontainers, vanilla JS frontend (no framework), i18n via a hand-rolled catalog in `js/i18n.js`.

**Spec:** `docs/superpowers/specs/2026-09-07-book-info-providers-design.md`

## Global Constraints

- No DB-level foreign-key constraints anywhere in this repo (`HasForeignKey`/`HasOne` are never used) — every relationship is a bare `int`/`Guid` column plus a `HasIndex`. `book_info_providers`/`books.BookInfoProviderId` and the new `users` columns must follow this exactly, per `docs/audits/database.md`.
- `BookMetadataLookupDispatcher`'s two provider dependencies must be registered as `IBookMetadataLookupService` (not the sealed concrete types) and the dispatcher itself must be registered `AddTransient`, not `AddSingleton` — capturing typed `HttpClient` services in a singleton defeats `IHttpClientFactory`'s handler rotation (see spec's "Provider abstraction" section).
- Every new API-facing validation error message must be added to `ApiMessageLocalizer.KeysByEnglishMessage` and to all three `BookWheel/Resources/SharedErrors*.resx` files (`en` base, `es`, `pl`), matching the existing convention (see `InvalidBookType` for the exact shape to copy).
- Every new user-facing settings-panel string needs an `en`/`es`/`pl` entry in `BookWheel/wwwroot/js/i18n.js`'s `settings` block — except the two provider option labels ("Open Library" / "Google Books"), which are proper nouns and stay untranslated, exactly like the existing `#langSelect` option text.
- `IBookRepository` is implemented by both `PostgresBookRepository` (active) and `JsonBookRepository` (legacy, still compiled) — any interface signature change must be applied to both.
- Version: bump `BookWheel/BookWheel.csproj`'s `InformationalVersion` from `2.16.2` to `2.17.0` (Task 7) — this is a new capability, no breaking change, so a minor bump per this project's convention (see the PWA spec's precedent).

---

### Task 1: Database schema — book_info_providers, BookInfoProviderId, user preference columns

**Files:**
- Create: `BookWheel/Storage/Postgres/Entities/BookInfoProviderEntity.cs`
- Modify: `BookWheel/Storage/Postgres/Entities/BookEntity.cs`
- Modify: `BookWheel/Storage/Postgres/Entities/UserEntity.cs`
- Modify: `BookWheel/Storage/Postgres/BookWheelDbContext.cs`
- Modify: `BookWheel/Models/BookRecord.cs`
- Modify: `BookWheel/Storage/IBookRepository.cs`
- Modify: `BookWheel/Storage/Postgres/PostgresBookRepository.cs`
- Modify: `BookWheel/Storage/JsonBookRepository.cs`
- Create (via `dotnet ef migrations add`): `BookWheel/Migrations/<timestamp>_AddBookInfoProviders.cs` and `.Designer.cs`
- Modify (auto-generated): `BookWheel/Migrations/BookWheelDbContextModelSnapshot.cs`
- Test: `BookWheel.Tests/BookWheelApiTests.cs` (new assertions appended to existing add/update tests)

**Interfaces:**
- Produces: `BookInfoProviderEntity { int Id; string Name; }`. `BookEntity.BookInfoProviderId` (`int?`). `UserEntity.Theme` (`string?`), `UserEntity.AnalyticsConsentOptedOut` (`bool`), `UserEntity.PreferredBookInfoProviderId` (`int?`). `BookRecord.BookInfoProviderId` (`int?`). `IBookRepository.AddAsync`/`UpdateAsync` gain a trailing `int? bookInfoProviderId = null` parameter — every later task that creates/updates a book goes through this.

- [ ] **Step 1: Add the new entity**

Create `BookWheel/Storage/Postgres/Entities/BookInfoProviderEntity.cs`:

```csharp
namespace BookWheel.Storage.Postgres.Entities;

public sealed class BookInfoProviderEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Add `BookInfoProviderId` to `BookEntity`**

In `BookWheel/Storage/Postgres/Entities/BookEntity.cs`, add a new property after `BookTypeId`:

```csharp
    public int BookTypeId { get; set; }
    public int? BookInfoProviderId { get; set; }
```

- [ ] **Step 3: Add preference columns to `UserEntity`**

In `BookWheel/Storage/Postgres/Entities/UserEntity.cs`, add after `CreatedAtUtc`:

```csharp
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? Theme { get; set; }
    public bool AnalyticsConsentOptedOut { get; set; }
    public int? PreferredBookInfoProviderId { get; set; }
```

- [ ] **Step 4: Map the new entity and columns in `BookWheelDbContext`**

In `BookWheel/Storage/Postgres/BookWheelDbContext.cs`, add the new `DbSet` after `BookTypes`:

```csharp
    public DbSet<BookTypeEntity> BookTypes => Set<BookTypeEntity>();
    public DbSet<BookInfoProviderEntity> BookInfoProviders => Set<BookInfoProviderEntity>();
```

Add `entity.HasIndex(b => b.BookInfoProviderId);` to the `BookEntity` configuration block, right after the existing `entity.HasIndex(b => b.BookTypeId);` line.

Add a new `BookInfoProviderEntity` mapping block right after the `BookTypeEntity` block (which ends with the `book_types` `HasData` call and its closing `});`):

```csharp
        modelBuilder.Entity<BookInfoProviderEntity>(entity =>
        {
            entity.ToTable("book_info_providers");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(50).IsRequired();
            entity.HasIndex(p => p.Name).IsUnique();
            entity.HasData(
                new BookInfoProviderEntity { Id = 1, Name = "Open Library" },
                new BookInfoProviderEntity { Id = 2, Name = "Google Books" });
        });
```

No changes are needed to the `UserEntity` mapping block for the three new columns — they need no `HasMaxLength`/`IsRequired`/index configuration beyond EF's conventions (nullable `string`/`int?` columns, non-null `bool` with a CLR default of `false`).

- [ ] **Step 5: Thread `BookInfoProviderId` through `BookRecord` and `IBookRepository`**

In `BookWheel/Models/BookRecord.cs`, add after `BookTypeId`:

```csharp
    public int BookTypeId { get; set; }
    public int? BookInfoProviderId { get; set; }
```

In `BookWheel/Storage/IBookRepository.cs`, change the two signatures:

```csharp
    Task<BookRecord> AddAsync(Guid userId, string title, string? isbn = null, string? author = null, string? coverUrl = null, bool addedByScanner = false, int bookTypeId = 1, Guid? createdByUserId = null, int? bookInfoProviderId = null);
    Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title, string? isbn = null, string? author = null, string? coverUrl = null, int bookTypeId = 1, Guid? updatedByUserId = null, int? bookInfoProviderId = null);
```

- [ ] **Step 6: Update `PostgresBookRepository`**

In `BookWheel/Storage/Postgres/PostgresBookRepository.cs`:

Change `AddAsync`'s signature to match Step 5 and set the new field on the created entity:

```csharp
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
```

Change `UpdateAsync`'s signature to match Step 5 and set the field:

```csharp
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
```

In `ToRecord`, add the mapping:

```csharp
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
```

- [ ] **Step 7: Update `JsonBookRepository` the same way**

In `BookWheel/Storage/JsonBookRepository.cs`, update `AddAsync`'s signature and body:

```csharp
    public async Task<BookRecord> AddAsync(Guid userId, string title, string? isbn = null, string? author = null, string? coverUrl = null, bool addedByScanner = false, int bookTypeId = 1, Guid? createdByUserId = null, int? bookInfoProviderId = null)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var record = new BookRecord
            {
                Id = Guid.NewGuid(),
                Title = title.Trim(),
                Isbn = isbn,
                Author = author,
                CoverUrl = coverUrl,
                AddedByScanner = addedByScanner,
                BookTypeId = bookTypeId,
                BookInfoProviderId = bookInfoProviderId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = createdByUserId ?? userId
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
```

And `UpdateAsync`:

```csharp
    public async Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title, string? isbn = null, string? author = null, string? coverUrl = null, int bookTypeId = 1, Guid? updatedByUserId = null, int? bookInfoProviderId = null)
    {
        await _gate.WaitAsync();
        try
        {
            var booksByUser = await ReadStoreUnsafeAsync();
            var books = GetBooksForUser(booksByUser, userId);
            var book = books.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Book not found.");
            book.Title = title.Trim();
            book.Isbn = isbn;
            book.Author = author;
            book.CoverUrl = coverUrl;
            book.BookTypeId = bookTypeId;
            book.BookInfoProviderId = bookInfoProviderId;
            book.UpdatedAtUtc = DateTimeOffset.UtcNow;
            book.LastUpdatedByUserId = updatedByUserId ?? userId;
            await WriteStoreUnsafeAsync(booksByUser);
            return book;
        }
        finally
        {
            _gate.Release();
        }
    }
```

- [ ] **Step 8: Build to confirm the plumbing compiles**

Run: `dotnet build BookWheel/BookWheel.csproj`
Expected: builds successfully (no other callers of `AddAsync`/`UpdateAsync` break — `BooksController`'s import-flow call at `_store.AddAsync(user.UserId, title, normalizedIsbn, NormalizeOptional(item.Author), NormalizeOptional(item.CoverUrl), bookTypeId: item.BookTypeId ?? 1)` uses named arguments after position 5, so the new trailing optional parameter doesn't affect it).

- [ ] **Step 9: Generate the EF Core migration**

Run: `dotnet ef migrations add AddBookInfoProviders --project BookWheel --startup-project BookWheel`

Expected: creates `BookWheel/Migrations/<timestamp>_AddBookInfoProviders.cs` and `.Designer.cs`, and updates `BookWheel/Migrations/BookWheelDbContextModelSnapshot.cs`. Open the generated `<timestamp>_AddBookInfoProviders.cs` and verify its `Up()` method contains, in some order:

- `AddColumn<string>("Theme", "users", nullable: true)`
- `AddColumn<bool>("AnalyticsConsentOptedOut", "users", nullable: false, defaultValue: false)`
- `AddColumn<int>("PreferredBookInfoProviderId", "users", nullable: true)`
- `AddColumn<int>("BookInfoProviderId", "books", nullable: true)`
- `CreateTable("book_info_providers", ...)` with an identity `Id` and a `character varying(50)` `Name`
- `InsertData("book_info_providers", ..., values: { {1, "Open Library"}, {2, "Google Books"} })`
- `CreateIndex("IX_books_BookInfoProviderId", "books", "BookInfoProviderId")`
- `CreateIndex("IX_book_info_providers_Name", "book_info_providers", "Name", unique: true)`

If EF omitted the `defaultValue: false` for the non-null `AnalyticsConsentOptedOut` column (it may, since there's no `[DefaultValue]` attribute forcing it — EF Core's default column-add behavior for a non-nullable `bool` with existing rows already requires *some* default to avoid a migration failure against a populated `users` table), add `defaultValue: false` to that `AddColumn<bool>` call by hand, mirroring `AddBookTypeTable`'s `defaultValue: 1` for its non-null `BookTypeId` column add.

- [ ] **Step 10: Verify against the real test suite (applies migrations via Testcontainers)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: all existing tests still pass — this proves the new migration applies cleanly against a fresh Postgres container (the same container `BookWheelWebAppFactory` boots the app against, which calls `Database.MigrateAsync()` on startup exactly like production).

- [ ] **Step 11: Add coverage for provider-id round-tripping through the repository layer**

Open `BookWheel.Tests/BookWheelApiTests.cs` and find the existing book add/update test(s) that assert on `bookTypeId` persistence (search for `bookTypeId` in that file). Add a new test near them:

```csharp
// bookInfoProviderId round-trip — passes once Task 5 adds it to UpdateBookRequest.
[Fact]
public async Task Add_Book_Persists_Null_BookInfoProviderId_When_Not_Supplied()
{
    var factory = _factory;
    using var client = factory.CreateClient();
    await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });

    var response = await client.PostAsJsonAsync("/api/books", new { title = "Manually Entered Book" });
    response.EnsureSuccessStatusCode();
    var created = await response.Content.ReadFromJsonAsync<JsonElement>();

    Assert.True(created.TryGetProperty("bookInfoProviderId", out var providerIdElement));
    Assert.Equal(JsonValueKind.Null, providerIdElement.ValueKind);
}

[Fact]
public async Task Add_Book_Persists_Supplied_BookInfoProviderId()
{
    var factory = _factory;
    using var client = factory.CreateClient();
    await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });

    var response = await client.PostAsJsonAsync("/api/books", new { title = "Looked-Up Book", bookInfoProviderId = 1 });
    response.EnsureSuccessStatusCode();
    var created = await response.Content.ReadFromJsonAsync<JsonElement>();

    Assert.Equal(1, created.GetProperty("bookInfoProviderId").GetInt32());
}
```

(`POST /api/auth/setup` both creates the account and signs the client in via `SignInAsync` — no separate login call is needed, matching the pattern already used throughout this file, e.g. the tests around line 64.) `bookInfoProviderId` isn't wired into `UpdateBookRequest` yet at this point in the plan, so these two tests are expected to **fail** right now (the field doesn't round-trip yet) — that's fine, they're written ahead of Task 5's controller change and will start passing once Task 5 lands.

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookInfoProviderId"`
Expected: both new tests FAIL (the JSON has no `bookInfoProviderId` property at all yet, so `TryGetProperty` returns false / `GetProperty` throws) — confirms the tests correctly detect the not-yet-wired field.

- [ ] **Step 12: Commit**

```bash
git add BookWheel/Storage/Postgres/Entities/BookInfoProviderEntity.cs BookWheel/Storage/Postgres/Entities/BookEntity.cs BookWheel/Storage/Postgres/Entities/UserEntity.cs BookWheel/Storage/Postgres/BookWheelDbContext.cs BookWheel/Models/BookRecord.cs BookWheel/Storage/IBookRepository.cs BookWheel/Storage/Postgres/PostgresBookRepository.cs BookWheel/Storage/JsonBookRepository.cs BookWheel/Migrations/ BookWheel.Tests/BookWheelApiTests.cs
git commit -m "Add book_info_providers table, BookInfoProviderId, and user preference columns (GH #70)"
```

---

### Task 2: Google Books provider service

**Files:**
- Modify: `BookWheel/Models/BookMetadataResult.cs`
- Modify: `BookWheel/Models/BookMetadataOptions.cs`
- Modify: `BookWheel/Services/OpenLibraryBookMetadataLookupService.cs`
- Create: `BookWheel/Services/GoogleBooksBookMetadataLookupService.cs`
- Modify: `BookWheel/Program.cs` (typed `HttpClient` registration only — dispatcher wiring is Task 3)
- Test: `BookWheel.Tests/Services/GoogleBooksBookMetadataLookupServiceTests.cs` (new)
- Test: `BookWheel.Tests/Services/OpenLibraryBookMetadataLookupServiceTests.cs` (one assertion added)

**Interfaces:**
- Consumes: `IBookMetadataLookupService` (unchanged, from `BookWheel/Services/IBookMetadataLookupService.cs`).
- Produces: `BookMetadataResult.ProviderId` (`int?`). `GoogleBooksBookMetadataLookupService : IBookMetadataLookupService`, constructor `(HttpClient httpClient, IOptions<BookMetadataOptions> options, ILogger<GoogleBooksBookMetadataLookupService> logger)`. `BookMetadataOptions.GoogleBooks.ApiKey` (`string?`). Both providers set their own `ProviderId` (Open Library = `1`, Google Books = `2`) on every non-null/non-empty result — Task 3's dispatcher relies on this to report which provider actually answered.

- [ ] **Step 1: Add `ProviderId` to `BookMetadataResult`**

In `BookWheel/Models/BookMetadataResult.cs`:

```csharp
namespace BookWheel.Models;

public sealed class BookMetadataResult
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Isbn { get; set; }
    public string? CoverUrl { get; set; }
    public int? ProviderId { get; set; }
}
```

- [ ] **Step 2: Add the Google Books options section**

In `BookWheel/Models/BookMetadataOptions.cs`:

```csharp
namespace BookWheel.Models;

public sealed class BookMetadataOptions
{
    public const string SectionName = "BookMetadata";

    public int TitleSearchResultLimit { get; set; } = 10;
    public GoogleBooksOptions GoogleBooks { get; set; } = new();
}

public sealed class GoogleBooksOptions
{
    public string? ApiKey { get; set; }
}
```

- [ ] **Step 3: Stamp `ProviderId = 1` on every Open Library result**

In `BookWheel/Services/OpenLibraryBookMetadataLookupService.cs`, add `ProviderId = 1` to both `BookMetadataResult` object initializers:

```csharp
            return new BookMetadataResult { Title = title, Author = author, Isbn = isbn, CoverUrl = coverUrl, ProviderId = 1 };
```

(in `LookupByIsbnAsync`), and:

```csharp
                results.Add(new BookMetadataResult { Title = resultTitle, Author = author, Isbn = isbn, CoverUrl = coverUrl, ProviderId = 1 });
```

(in `LookupByTitleAsync`'s loop).

- [ ] **Step 4: Add a regression assertion for `ProviderId = 1`**

In `BookWheel.Tests/Services/OpenLibraryBookMetadataLookupServiceTests.cs`, in `LookupByIsbnAsync_Returns_Metadata_When_Book_Is_Found`, add after the existing assertions:

```csharp
        Assert.Equal(1, result.ProviderId);
```

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~OpenLibraryBookMetadataLookupServiceTests"`
Expected: PASS (all Open Library tests, including the new assertion).

- [ ] **Step 5: Write the failing tests for `GoogleBooksBookMetadataLookupService`**

Create `BookWheel.Tests/Services/GoogleBooksBookMetadataLookupServiceTests.cs`:

```csharp
using System.Net;
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookWheel.Tests.Services;

public sealed class GoogleBooksBookMetadataLookupServiceTests
{
    [Fact]
    public async Task LookupByIsbnAsync_Returns_Metadata_When_Book_Is_Found()
    {
        const string responseJson = """
        {
          "items": [
            {
              "volumeInfo": {
                "title": "Effective Java",
                "authors": ["Joshua Bloch"],
                "imageLinks": { "thumbnail": "http://books.google.com/thumb.jpg" }
              }
            }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Effective Java", result!.Title);
        Assert.Equal("Joshua Bloch", result.Author);
        Assert.Equal("9780134685991", result.Isbn);
        Assert.Equal("https://books.google.com/thumb.jpg", result.CoverUrl);
        Assert.Equal(2, result.ProviderId);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Joins_Multiple_Authors()
    {
        const string responseJson = """
        {
          "items": [
            { "volumeInfo": { "title": "Effective Java", "authors": ["Joshua Bloch", "Someone Else"] } }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Equal("Joshua Bloch, Someone Else", result!.Author);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_When_No_Items_Match()
    {
        var service = CreateService("""{ "totalItems": 0 }""");

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_On_Http_Error_Status()
    {
        var service = CreateService("{}", HttpStatusCode.InternalServerError);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_On_Malformed_Json()
    {
        var service = CreateService("{ not valid json");

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_On_Network_Failure()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Simulated network failure."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var service = new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        var result = await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Matches_With_Best_Isbn_Preferring_Isbn13()
    {
        const string responseJson = """
        {
          "items": [
            {
              "volumeInfo": {
                "title": "Foundation",
                "authors": ["Isaac Asimov"],
                "industryIdentifiers": [
                  { "type": "ISBN_10", "identifier": "0553293354" },
                  { "type": "ISBN_13", "identifier": "9780553293357" }
                ],
                "imageLinks": { "thumbnail": "http://books.google.com/foundation.jpg" }
              }
            }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var results = await service.LookupByTitleAsync("Foundation", 10, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Foundation", result.Title);
        Assert.Equal("Isaac Asimov", result.Author);
        Assert.Equal("9780553293357", result.Isbn);
        Assert.Equal("https://books.google.com/foundation.jpg", result.CoverUrl);
        Assert.Equal(2, result.ProviderId);
    }

    [Fact]
    public async Task LookupByTitleAsync_Caps_Results_At_MaxResults_Even_If_The_Api_Returns_More()
    {
        const string responseJson = """
        {
          "items": [
            { "volumeInfo": { "title": "Foundation", "authors": ["Author One"] } },
            { "volumeInfo": { "title": "Foundation", "authors": ["Author Two"] } },
            { "volumeInfo": { "title": "Foundation", "authors": ["Author Three"] } }
          ]
        }
        """;
        var service = CreateService(responseJson);

        var results = await service.LookupByTitleAsync("Foundation", 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Empty_When_No_Items_Match()
    {
        var service = CreateService("""{ "totalItems": 0 }""");

        var results = await service.LookupByTitleAsync("Some Nonexistent Title Xyz", 10, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task LookupByTitleAsync_Passes_MaxResults_As_The_MaxResults_Query_Parameter()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "items": [] }""") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var service = new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        await service.LookupByTitleAsync("Foundation", 7, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains("maxResults=7", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_Append_Api_Key_When_Configured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "items": [] }""") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var options = Options.Create(new BookMetadataOptions { GoogleBooks = new GoogleBooksOptions { ApiKey = "test-key" } });
        var service = new GoogleBooksBookMetadataLookupService(httpClient, options, NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains("key=test-key", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_Omit_Key_Parameter_When_Not_Configured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "items": [] }""") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        var service = new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);

        await service.LookupByIsbnAsync("9780134685991", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.DoesNotContain("key=", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    private static GoogleBooksBookMetadataLookupService CreateService(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson)
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/books/v1/") };
        return new GoogleBooksBookMetadataLookupService(httpClient, Options.Create(new BookMetadataOptions()), NullLogger<GoogleBooksBookMetadataLookupService>.Instance);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request, cancellationToken));
        }
    }
}
```

- [ ] **Step 6: Run the new tests to verify they fail (the service doesn't exist yet)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~GoogleBooksBookMetadataLookupServiceTests"`
Expected: FAIL to compile — `GoogleBooksBookMetadataLookupService` doesn't exist yet.

- [ ] **Step 7: Implement `GoogleBooksBookMetadataLookupService`**

Create `BookWheel/Services/GoogleBooksBookMetadataLookupService.cs`:

```csharp
using System.Text.Json;
using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class GoogleBooksBookMetadataLookupService : IBookMetadataLookupService
{
    private const int ProviderId = 2;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<GoogleBooksBookMetadataLookupService> _logger;

    public GoogleBooksBookMetadataLookupService(HttpClient httpClient, IOptions<BookMetadataOptions> options, ILogger<GoogleBooksBookMetadataLookupService> logger)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.GoogleBooks.ApiKey;
        _logger = logger;
    }

    public async Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = AppendApiKey($"volumes?q=isbn:{Uri.EscapeDataString(isbn)}");
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!TryGetFirstVolumeInfo(document.RootElement, out var volumeInfo))
            {
                return null;
            }

            var title = volumeInfo.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var author = ExtractAuthors(volumeInfo);
            var coverUrl = ExtractCoverUrl(volumeInfo);

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(coverUrl))
            {
                return null;
            }

            return new BookMetadataResult { Title = title, Author = author, Isbn = isbn, CoverUrl = coverUrl, ProviderId = ProviderId };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "ISBN metadata lookup failed for {Isbn}.", isbn);
            return null;
        }
    }

    public async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = AppendApiKey($"volumes?q=intitle:{Uri.EscapeDataString(title)}&maxResults={maxResults}");
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<BookMetadataResult>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                if (!item.TryGetProperty("volumeInfo", out var volumeInfo))
                {
                    continue;
                }

                var resultTitle = volumeInfo.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(resultTitle))
                {
                    continue;
                }

                var author = ExtractAuthors(volumeInfo);
                var isbn = ExtractBestIsbn(volumeInfo);
                var coverUrl = ExtractCoverUrl(volumeInfo);

                results.Add(new BookMetadataResult { Title = resultTitle, Author = author, Isbn = isbn, CoverUrl = coverUrl, ProviderId = ProviderId });
            }

            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Title metadata lookup failed for {Title}.", title);
            return [];
        }
    }

    private string AppendApiKey(string requestUri)
    {
        return string.IsNullOrWhiteSpace(_apiKey) ? requestUri : $"{requestUri}&key={Uri.EscapeDataString(_apiKey)}";
    }

    private static bool TryGetFirstVolumeInfo(JsonElement root, out JsonElement volumeInfo)
    {
        volumeInfo = default;
        if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array || itemsElement.GetArrayLength() == 0)
        {
            return false;
        }

        return itemsElement[0].TryGetProperty("volumeInfo", out volumeInfo);
    }

    private static string? ExtractAuthors(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("authors", out var authorsElement) || authorsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = authorsElement.EnumerateArray()
            .Select(a => a.GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? ExtractCoverUrl(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("imageLinks", out var imageLinks))
        {
            return null;
        }

        foreach (var size in new[] { "thumbnail", "smallThumbnail" })
        {
            if (imageLinks.TryGetProperty(size, out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                var url = urlElement.GetString();
                return url is null ? null : url.Replace("http://", "https://", StringComparison.Ordinal);
            }
        }

        return null;
    }

    private static string? ExtractBestIsbn(JsonElement volumeInfo)
    {
        if (!volumeInfo.TryGetProperty("industryIdentifiers", out var identifiersElement) || identifiersElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var identifiers = identifiersElement.EnumerateArray()
            .Select(i => i.TryGetProperty("identifier", out var idElement) ? idElement.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        return identifiers.FirstOrDefault(id => id!.Length == 13) ?? identifiers.FirstOrDefault();
    }
}
```

- [ ] **Step 8: Run the new tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~GoogleBooksBookMetadataLookupServiceTests"`
Expected: PASS (all 12 tests).

- [ ] **Step 9: Register the typed `HttpClient` in `Program.cs`**

In `BookWheel/Program.cs`, change the existing registration:

```csharp
builder.Services.AddHttpClient<IBookMetadataLookupService, OpenLibraryBookMetadataLookupService>(client =>
{
	client.BaseAddress = new Uri("https://openlibrary.org/");
	client.Timeout = TimeSpan.FromSeconds(8);
	client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheel/1.0 (+https://github.com/jasonkryst/BookWheel)");
});
```

to (single-generic form — drops the `IBookMetadataLookupService` binding, since nothing resolves that interface directly from the container anymore after Task 3):

```csharp
builder.Services.AddHttpClient<OpenLibraryBookMetadataLookupService>(client =>
{
	client.BaseAddress = new Uri("https://openlibrary.org/");
	client.Timeout = TimeSpan.FromSeconds(8);
	client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheel/1.0 (+https://github.com/jasonkryst/BookWheel)");
});
builder.Services.AddHttpClient<GoogleBooksBookMetadataLookupService>(client =>
{
	client.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
	client.Timeout = TimeSpan.FromSeconds(8);
	client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheel/1.0 (+https://github.com/jasonkryst/BookWheel)");
});
builder.Services.Configure<BookMetadataOptions>(builder.Configuration.GetSection(BookMetadataOptions.SectionName));
```

(the `Configure<BookMetadataOptions>` line already exists earlier in the file — do not duplicate it; only the two `AddHttpClient` calls are new/changed here. Leave the existing `Configure<BookMetadataOptions>` call where it already is.)

Note: `Program.cs` no longer compiles as-is at this point in the plan — `BooksController` still expects `IBookMetadataLookupService` to be resolvable from DI, and it no longer is. This is expected and gets fixed in Task 5. Do **not** try to make the app run end-to-end after this task; just confirm the project builds.

Run: `dotnet build BookWheel/BookWheel.csproj`
Expected: builds successfully — `BooksController`'s constructor still declares `IBookMetadataLookupService metadataLookup`, and ASP.NET Core only resolves controller dependencies at request time (not at build time), so this compiles fine even though it will fail at runtime until Task 5.

- [ ] **Step 10: Commit**

```bash
git add BookWheel/Models/BookMetadataResult.cs BookWheel/Models/BookMetadataOptions.cs BookWheel/Services/OpenLibraryBookMetadataLookupService.cs BookWheel/Services/GoogleBooksBookMetadataLookupService.cs BookWheel/Program.cs BookWheel.Tests/Services/GoogleBooksBookMetadataLookupServiceTests.cs BookWheel.Tests/Services/OpenLibraryBookMetadataLookupServiceTests.cs
git commit -m "Add GoogleBooksBookMetadataLookupService and BookMetadataResult.ProviderId (GH #70)"
```

---

### Task 3: BookMetadataLookupDispatcher

**Files:**
- Create: `BookWheel/Services/BookMetadataLookupDispatcher.cs`
- Test: `BookWheel.Tests/Services/BookMetadataLookupDispatcherTests.cs` (new)

**Interfaces:**
- Consumes: `IBookMetadataLookupService` (both providers from Task 2, or hand-rolled test fakes).
- Produces: `BookMetadataLookupDispatcher` with `Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, int? preferredProviderId, CancellationToken cancellationToken)` and `Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, int? preferredProviderId, CancellationToken cancellationToken)`. Task 5's `BooksController` calls these two methods directly.

- [ ] **Step 1: Write the failing tests**

Create `BookWheel.Tests/Services/BookMetadataLookupDispatcherTests.cs`:

```csharp
using BookWheel.Models;
using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class BookMetadataLookupDispatcherTests
{
    [Fact]
    public async Task LookupByIsbnAsync_Uses_Preferred_Provider_When_It_Succeeds()
    {
        var openLibrary = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Open Library", ProviderId = 1 });
        var googleBooks = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Google Books", ProviderId = 2 });
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: 2, CancellationToken.None);

        Assert.Equal("From Google Books", result!.Title);
        Assert.Equal(2, result.ProviderId);
        Assert.Equal(1, googleBooks.IsbnCallCount);
        Assert.Equal(0, openLibrary.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Defaults_To_Provider_One_When_No_Preference_Set()
    {
        var openLibrary = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Open Library", ProviderId = 1 });
        var googleBooks = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Google Books", ProviderId = 2 });
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: null, CancellationToken.None);

        Assert.Equal("From Open Library", result!.Title);
        Assert.Equal(1, openLibrary.IsbnCallCount);
        Assert.Equal(0, googleBooks.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Falls_Back_When_Preferred_Provider_Returns_Null()
    {
        var openLibrary = new StubProvider(isbnResult: new BookMetadataResult { Title = "From Open Library", ProviderId = 1 });
        var googleBooks = new StubProvider(isbnResult: null);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: 2, CancellationToken.None);

        Assert.Equal("From Open Library", result!.Title);
        Assert.Equal(1, googleBooks.IsbnCallCount);
        Assert.Equal(1, openLibrary.IsbnCallCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_Returns_Null_When_Both_Providers_Return_Null()
    {
        var openLibrary = new StubProvider(isbnResult: null);
        var googleBooks = new StubProvider(isbnResult: null);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var result = await dispatcher.LookupByIsbnAsync("9780134685991", preferredProviderId: 1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByTitleAsync_Falls_Back_When_Preferred_Provider_Returns_Empty()
    {
        IReadOnlyList<BookMetadataResult> fallbackResults = [new BookMetadataResult { Title = "From Google Books", ProviderId = 2 }];
        var openLibrary = new StubProvider(titleResults: []);
        var googleBooks = new StubProvider(titleResults: fallbackResults);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var results = await dispatcher.LookupByTitleAsync("Foundation", 10, preferredProviderId: 1, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("From Google Books", result.Title);
        Assert.Equal(1, openLibrary.TitleCallCount);
        Assert.Equal(1, googleBooks.TitleCallCount);
    }

    [Fact]
    public async Task LookupByTitleAsync_Returns_Empty_When_Both_Providers_Return_Empty()
    {
        var openLibrary = new StubProvider(titleResults: []);
        var googleBooks = new StubProvider(titleResults: []);
        var dispatcher = new BookMetadataLookupDispatcher(openLibrary, googleBooks);

        var results = await dispatcher.LookupByTitleAsync("Foundation", 10, preferredProviderId: null, CancellationToken.None);

        Assert.Empty(results);
    }

    private sealed class StubProvider : IBookMetadataLookupService
    {
        private readonly BookMetadataResult? _isbnResult;
        private readonly IReadOnlyList<BookMetadataResult> _titleResults;

        public int IsbnCallCount { get; private set; }
        public int TitleCallCount { get; private set; }

        public StubProvider(BookMetadataResult? isbnResult = null, IReadOnlyList<BookMetadataResult>? titleResults = null)
        {
            _isbnResult = isbnResult;
            _titleResults = titleResults ?? [];
        }

        public Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
        {
            IsbnCallCount++;
            return Task.FromResult(_isbnResult);
        }

        public Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
        {
            TitleCallCount++;
            return Task.FromResult(_titleResults);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookMetadataLookupDispatcherTests"`
Expected: FAIL to compile — `BookMetadataLookupDispatcher` doesn't exist yet.

- [ ] **Step 3: Implement the dispatcher**

Create `BookWheel/Services/BookMetadataLookupDispatcher.cs`:

```csharp
using BookWheel.Models;

namespace BookWheel.Services;

public sealed class BookMetadataLookupDispatcher
{
    private readonly Dictionary<int, IBookMetadataLookupService> _providers;

    public BookMetadataLookupDispatcher(IBookMetadataLookupService openLibraryProvider, IBookMetadataLookupService googleBooksProvider)
    {
        _providers = new Dictionary<int, IBookMetadataLookupService>
        {
            [1] = openLibraryProvider,
            [2] = googleBooksProvider
        };
    }

    public async Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, int? preferredProviderId, CancellationToken cancellationToken)
    {
        foreach (var provider in GetProviderOrder(preferredProviderId))
        {
            var result = await provider.LookupByIsbnAsync(isbn, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, int? preferredProviderId, CancellationToken cancellationToken)
    {
        foreach (var provider in GetProviderOrder(preferredProviderId))
        {
            var results = await provider.LookupByTitleAsync(title, maxResults, cancellationToken);
            if (results.Count > 0)
            {
                return results;
            }
        }

        return [];
    }

    private IEnumerable<IBookMetadataLookupService> GetProviderOrder(int? preferredProviderId)
    {
        var preferredId = _providers.ContainsKey(preferredProviderId ?? 1) ? preferredProviderId ?? 1 : 1;
        yield return _providers[preferredId];

        foreach (var (id, provider) in _providers)
        {
            if (id != preferredId)
            {
                yield return provider;
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify success**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookMetadataLookupDispatcherTests"`
Expected: PASS (all 6 tests).

- [ ] **Step 5: Commit**

```bash
git add BookWheel/Services/BookMetadataLookupDispatcher.cs BookWheel.Tests/Services/BookMetadataLookupDispatcherTests.cs
git commit -m "Add BookMetadataLookupDispatcher with preferred-provider fallback (GH #70)"
```

---

### Task 4: User preferences repository and API

**Files:**
- Create: `BookWheel/Models/UserPreferences.cs`
- Create: `BookWheel/Models/UpdatePreferencesRequest.cs`
- Create: `BookWheel/Storage/IUserPreferencesRepository.cs`
- Create: `BookWheel/Storage/Postgres/PostgresUserPreferencesRepository.cs`
- Create: `BookWheel/Controllers/PreferencesController.cs`
- Modify: `BookWheel/Program.cs` (DI registration)
- Modify: `BookWheel/Services/ApiMessageLocalizer.cs`
- Modify: `BookWheel/Resources/SharedErrors.resx`, `SharedErrors.es.resx`, `SharedErrors.pl.resx`
- Test: `BookWheel.Tests/Controllers/PreferencesControllerTests.cs` (new)

**Interfaces:**
- Consumes: `AuthService.GetAuthenticatedUser(HttpContext)` (existing, from `BookWheel/Services/AuthService.cs`), `IDbContextFactory<BookWheelDbContext>` (existing).
- Produces: `UserPreferences { string? Theme; bool AnalyticsConsentOptedOut; int? PreferredBookInfoProviderId; }`. `IUserPreferencesRepository.GetAsync(Guid userId)` / `.UpdateAsync(Guid userId, string? theme, bool analyticsConsentOptedOut, int? preferredBookInfoProviderId)`, both returning `Task<UserPreferences>`. `GET /api/preferences` and `PUT /api/preferences` — Task 6's frontend calls these directly.

- [ ] **Step 1: Add the model classes**

Create `BookWheel/Models/UserPreferences.cs`:

```csharp
namespace BookWheel.Models;

public sealed class UserPreferences
{
    public string? Theme { get; set; }
    public bool AnalyticsConsentOptedOut { get; set; }
    public int? PreferredBookInfoProviderId { get; set; }
}
```

Create `BookWheel/Models/UpdatePreferencesRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdatePreferencesRequest
{
    [RegularExpression("^(dark|light|high-contrast)$", ErrorMessage = "Theme must be a valid value.")]
    public string? Theme { get; set; }

    public bool AnalyticsConsentOptedOut { get; set; }

    [Range(1, 2, ErrorMessage = "Book info provider must be a valid provider.")]
    public int? PreferredBookInfoProviderId { get; set; }
}
```

- [ ] **Step 2: Add the repository interface and Postgres implementation**

Create `BookWheel/Storage/IUserPreferencesRepository.cs`:

```csharp
using BookWheel.Models;

namespace BookWheel.Storage;

public interface IUserPreferencesRepository
{
    Task<UserPreferences> GetAsync(Guid userId);
    Task<UserPreferences> UpdateAsync(Guid userId, string? theme, bool analyticsConsentOptedOut, int? preferredBookInfoProviderId);
}
```

Create `BookWheel/Storage/Postgres/PostgresUserPreferencesRepository.cs`:

```csharp
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
```

- [ ] **Step 3: Register the repository in `Program.cs`**

In `BookWheel/Program.cs`, add after the existing `PostgresPasswordResetTokenRepository` registration block:

```csharp
builder.Services.AddSingleton<PostgresUserPreferencesRepository>();
builder.Services.AddSingleton<IUserPreferencesRepository>(sp => sp.GetRequiredService<PostgresUserPreferencesRepository>());
```

- [ ] **Step 4: Add the new validation messages to `ApiMessageLocalizer` and the three resx files**

In `BookWheel/Services/ApiMessageLocalizer.cs`, add two entries to `KeysByEnglishMessage` (anywhere in the dictionary, e.g. right after the `InvalidBookType` entry):

```csharp
			["Book type must be a valid type."] = "InvalidBookType",
			["Book info provider must be a valid provider."] = "InvalidBookInfoProvider",
			["Theme must be a valid value."] = "InvalidTheme",
```

In `BookWheel/Resources/SharedErrors.resx`, add after the `InvalidBookType` `<data>` block:

```xml
  <data name="InvalidBookInfoProvider" xml:space="preserve">
    <value>Book info provider must be a valid provider.</value>
  </data>
  <data name="InvalidTheme" xml:space="preserve">
    <value>Theme must be a valid value.</value>
  </data>
```

In `BookWheel/Resources/SharedErrors.es.resx`, add after its `InvalidBookType` block:

```xml
  <data name="InvalidBookInfoProvider" xml:space="preserve">
    <value>El proveedor de información del libro debe ser válido.</value>
  </data>
  <data name="InvalidTheme" xml:space="preserve">
    <value>El tema debe ser un valor válido.</value>
  </data>
```

In `BookWheel/Resources/SharedErrors.pl.resx`, add after its `InvalidBookType` block:

```xml
  <data name="InvalidBookInfoProvider" xml:space="preserve">
    <value>Dostawca informacji o książce musi być prawidłowy.</value>
  </data>
  <data name="InvalidTheme" xml:space="preserve">
    <value>Motyw musi mieć prawidłową wartość.</value>
  </data>
```

- [ ] **Step 5: Write the failing controller tests**

Create `BookWheel.Tests/Controllers/PreferencesControllerTests.cs`. `BookWheelWebAppFactory` exposes `StartAsync()` (idempotent — boots the host and runs migrations) and `ResetAsync()` (truncates `books`, `password_reset_tokens`, `users`, `spin_selections` between tests) — the exact pair `BookWheelApiTests.InitializeAsync` already calls; use the same pair here. `POST /api/auth/setup` both creates the account and signs the client in (see `AuthController.Setup` → `SignInAsync`), so no separate login call is needed for a fresh test user.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookWheel.Tests.Controllers;

public sealed class PreferencesControllerTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
{
    private readonly BookWheelWebAppFactory _factory;

    public PreferencesControllerTests(BookWheelWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_Returns_Unauthorized_When_Not_Logged_In()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Default_Preferences_For_A_Fresh_User()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.GetAsync("/api/preferences");
        response.EnsureSuccessStatusCode();
        var preferences = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, preferences.GetProperty("theme").ValueKind);
        Assert.False(preferences.GetProperty("analyticsConsentOptedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preferences.GetProperty("preferredBookInfoProviderId").ValueKind);
    }

    [Fact]
    public async Task Put_Persists_Preferences_And_Get_Reflects_Them()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var putResponse = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "light",
            analyticsConsentOptedOut = true,
            preferredBookInfoProviderId = 2
        });
        putResponse.EnsureSuccessStatusCode();

        var getResponse = await client.GetAsync("/api/preferences");
        getResponse.EnsureSuccessStatusCode();
        var preferences = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("light", preferences.GetProperty("theme").GetString());
        Assert.True(preferences.GetProperty("analyticsConsentOptedOut").GetBoolean());
        Assert.Equal(2, preferences.GetProperty("preferredBookInfoProviderId").GetInt32());
    }

    [Fact]
    public async Task Put_Rejects_Unknown_Provider_Id()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = (string?)null,
            analyticsConsentOptedOut = false,
            preferredBookInfoProviderId = 99
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Rejects_Unknown_Theme()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "not-a-real-theme",
            analyticsConsentOptedOut = false,
            preferredBookInfoProviderId = (int?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/setup", new { username = $"user-{Guid.NewGuid():N}", password = "correct horse battery staple" });
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 6: Run to verify failure**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~PreferencesControllerTests"`
Expected: FAIL to compile — `PreferencesController` doesn't exist yet.

- [ ] **Step 7: Implement `PreferencesController`**

Create `BookWheel/Controllers/PreferencesController.cs`:

```csharp
using BookWheel.Models;
using BookWheel.Services;
using BookWheel.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BookWheel.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PreferencesController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IUserPreferencesRepository _preferencesRepository;

    public PreferencesController(AuthService authService, IUserPreferencesRepository preferencesRepository)
    {
        _authService = authService;
        _preferencesRepository = preferencesRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        var preferences = await _preferencesRepository.GetAsync(user.UserId);
        return Ok(preferences);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePreferencesRequest request)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        var preferences = await _preferencesRepository.UpdateAsync(
            user.UserId,
            request.Theme,
            request.AnalyticsConsentOptedOut,
            request.PreferredBookInfoProviderId);
        return Ok(preferences);
    }
}
```

- [ ] **Step 8: Run to verify success**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~PreferencesControllerTests"`
Expected: PASS (all 5 tests).

- [ ] **Step 9: Commit**

```bash
git add BookWheel/Models/UserPreferences.cs BookWheel/Models/UpdatePreferencesRequest.cs BookWheel/Storage/IUserPreferencesRepository.cs BookWheel/Storage/Postgres/PostgresUserPreferencesRepository.cs BookWheel/Controllers/PreferencesController.cs BookWheel/Program.cs BookWheel/Services/ApiMessageLocalizer.cs BookWheel/Resources/SharedErrors.resx BookWheel/Resources/SharedErrors.es.resx BookWheel/Resources/SharedErrors.pl.resx BookWheel.Tests/Controllers/PreferencesControllerTests.cs
git commit -m "Add GET/PUT /api/preferences for server-side theme/analytics/provider preferences (GH #70)"
```

---

### Task 5: Wire BooksController to the dispatcher and provider stamping

**Files:**
- Modify: `BookWheel/Controllers/BooksController.cs`
- Modify: `BookWheel/Models/UpdateBookRequest.cs`
- Modify: `BookWheel/Program.cs` (dispatcher DI registration)
- Modify: `BookWheel.Tests/BookWheelWebAppFactory.cs`
- Create: `BookWheel.Tests/Services/FakeGoogleBooksMetadataLookupService.cs`
- Modify: `BookWheel.Tests/BookWheelApiTests.cs` (the two provider-id tests from Task 1 Step 11 now pass; add fallback-preference integration test)

**Interfaces:**
- Consumes: `BookMetadataLookupDispatcher` (Task 3), `IUserPreferencesRepository` (Task 4), `IBookRepository.AddAsync`/`UpdateAsync` (Task 1).
- Produces: `/api/books/lookup` responses now include `providerId`; `POST`/`PUT /api/books` accept `bookInfoProviderId`.

- [ ] **Step 1: Register the dispatcher in `Program.cs`**

In `BookWheel/Program.cs`, add right after the two `AddHttpClient` calls from Task 2 Step 9:

```csharp
builder.Services.AddTransient(sp => new BookMetadataLookupDispatcher(
	sp.GetRequiredService<OpenLibraryBookMetadataLookupService>(),
	sp.GetRequiredService<GoogleBooksBookMetadataLookupService>()));
```

- [ ] **Step 2: Add `BookInfoProviderId` to `UpdateBookRequest`**

In `BookWheel/Models/UpdateBookRequest.cs`, add after `BookTypeId`:

```csharp
    [Range(1, 3, ErrorMessage = "Book type must be a valid type.")]
    public int BookTypeId { get; set; } = 1;

    [Range(1, 2, ErrorMessage = "Book info provider must be a valid provider.")]
    public int? BookInfoProviderId { get; set; }
```

- [ ] **Step 3: Wire `BooksController` to the dispatcher and preferences repository**

In `BookWheel/Controllers/BooksController.cs`:

Change the constructor's field type and add the preferences repository:

```csharp
    private readonly AuthService _authService;
    private readonly AppMetricsService _metricsService;
    private readonly IBookRepository _store;
    private readonly ISpinHistoryRepository _spinHistory;
    private readonly ApiMessageLocalizer _errors;
    private readonly BookMetadataLookupDispatcher _metadataLookup;
    private readonly IUserPreferencesRepository _preferencesRepository;
    private readonly IOptionsSnapshot<BookMetadataOptions> _metadataOptions;

    public BooksController(
        AuthService authService,
        AppMetricsService metricsService,
        IBookRepository store,
        ISpinHistoryRepository spinHistory,
        ApiMessageLocalizer errors,
        BookMetadataLookupDispatcher metadataLookup,
        IUserPreferencesRepository preferencesRepository,
        IOptionsSnapshot<BookMetadataOptions> metadataOptions)
    {
        _authService = authService;
        _metricsService = metricsService;
        _store = store;
        _spinHistory = spinHistory;
        _errors = errors;
        _metadataLookup = metadataLookup;
        _preferencesRepository = preferencesRepository;
        _metadataOptions = metadataOptions;
    }
```

(`BookMetadataLookupDispatcher` lives in `BookWheel.Services` and `IUserPreferencesRepository` in `BookWheel.Storage` — both namespaces are already `using`'d at the top of this file, since it already uses `AuthService`/`IBookMetadataLookupService` and `IBookRepository` respectively. No new `using` directives are needed.)

Change the `Add` action's call to `_store.AddAsync` to pass the new field:

```csharp
            var book = await _store.AddAsync(user.UserId, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl), request.AddedByScanner, request.BookTypeId, bookInfoProviderId: request.BookInfoProviderId);
```

Change the `Update` action's call to `_store.UpdateAsync` to pass the new field:

```csharp
            var book = await _store.UpdateAsync(user.UserId, id, request.Title, normalizedIsbn, NormalizeOptional(request.Author), NormalizeOptional(request.CoverUrl), request.BookTypeId, bookInfoProviderId: request.BookInfoProviderId);
```

Change the `Lookup` action to resolve the user's preference and call the dispatcher instead of the raw interface:

```csharp
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string? isbn, [FromQuery] string? title)
    {
        var user = _authService.GetAuthenticatedUser(HttpContext);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(isbn) && string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = _errors.Localize("Provide an ISBN or a title to look up.") });
        }

        var preferences = await _preferencesRepository.GetAsync(user.UserId);

        if (!string.IsNullOrWhiteSpace(isbn))
        {
            if (!IsbnValidator.TryNormalize(isbn, out var normalizedIsbn))
            {
                return BadRequest(new { message = _errors.Localize("The provided ISBN is not valid.") });
            }

            var result = await _metadataLookup.LookupByIsbnAsync(normalizedIsbn, preferences.PreferredBookInfoProviderId, HttpContext.RequestAborted);
            if (result is null)
            {
                return NotFound(new { message = _errors.Localize("No book metadata found for that ISBN.") });
            }

            return Ok(result);
        }

        var maxResults = Math.Clamp(_metadataOptions.Value.TitleSearchResultLimit, 1, 25);
        var results = await _metadataLookup.LookupByTitleAsync(title!.Trim(), maxResults, preferences.PreferredBookInfoProviderId, HttpContext.RequestAborted);
        if (results.Count == 0)
        {
            return NotFound(new { message = _errors.Localize("No book metadata found for that title.") });
        }

        return Ok(new { results });
    }
```

- [ ] **Step 4: Add the Google Books test fake**

Create `BookWheel.Tests/Services/FakeGoogleBooksMetadataLookupService.cs`:

```csharp
using BookWheel.Models;
using BookWheel.Services;

namespace BookWheel.Tests.Services;

/// <summary>
/// Deterministic stand-in for <see cref="GoogleBooksBookMetadataLookupService"/> used by
/// integration tests so they never depend on reaching the real Google Books API.
/// </summary>
public sealed class FakeGoogleBooksMetadataLookupService : IBookMetadataLookupService
{
    public const string KnownIsbn = "9780441013593";
    public const string KnownIsbnTitle = "Dune";
    public const string KnownIsbnAuthor = "Frank Herbert";
    public const string KnownIsbnCoverUrl = "https://books.google.com/dune.jpg";

    public const string KnownTitle = "Neuromancer";
    public const string KnownTitleIsbn = "9780441569595";
    public const string KnownTitleAuthor = "William Gibson";
    public const string KnownTitleCoverUrl = "https://books.google.com/neuromancer.jpg";

    public Task<BookMetadataResult?> LookupByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.Equals(isbn, KnownIsbn, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BookMetadataResult?>(new BookMetadataResult
            {
                Title = KnownIsbnTitle,
                Author = KnownIsbnAuthor,
                Isbn = KnownIsbn,
                CoverUrl = KnownIsbnCoverUrl,
                ProviderId = 2
            });
        }

        return Task.FromResult<BookMetadataResult?>(null);
    }

    public Task<IReadOnlyList<BookMetadataResult>> LookupByTitleAsync(string title, int maxResults, CancellationToken cancellationToken)
    {
        if (string.Equals(title, KnownTitle, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<BookMetadataResult> singleMatch =
            [
                new BookMetadataResult { Title = KnownTitle, Author = KnownTitleAuthor, Isbn = KnownTitleIsbn, CoverUrl = KnownTitleCoverUrl, ProviderId = 2 }
            ];
            return Task.FromResult(singleMatch);
        }

        return Task.FromResult<IReadOnlyList<BookMetadataResult>>(Array.Empty<BookMetadataResult>());
    }
}
```

- [ ] **Step 5: Update `BookWheelWebAppFactory`'s test wiring**

In `BookWheel.Tests/BookWheelWebAppFactory.cs`, change:

```csharp
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookMetadataLookupService>();
            services.AddSingleton<IBookMetadataLookupService, FakeBookMetadataLookupService>();
        });
```

to:

```csharp
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BookMetadataLookupDispatcher>();
            services.AddSingleton(new BookMetadataLookupDispatcher(
                new FakeBookMetadataLookupService(),
                new FakeGoogleBooksMetadataLookupService()));
        });
```

(add `using BookWheel.Tests.Services;` at the top of the file if `FakeBookMetadataLookupService`/`FakeGoogleBooksMetadataLookupService` aren't already in scope there — check the existing `using` list first, since `FakeBookMetadataLookupService` was already referenced here before this change).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: PASS, including:
- All pre-existing `/api/books/lookup` integration tests (unaffected — provider `1`/Open Library fake is still the default when no preference is set)
- The two `BookInfoProviderId` round-trip tests added in Task 1 Step 11 (now pass — remove the "passes once Task 5 lands" comment above them in `BookWheel.Tests/BookWheelApiTests.cs`, since it's now true)

- [ ] **Step 7: Add an integration test for preference-driven provider fallback**

In `BookWheel.Tests/BookWheelApiTests.cs`, add (near the other `/api/books/lookup` tests):

```csharp
[Fact]
public async Task Lookup_By_Isbn_Uses_Preferred_Provider_Then_Falls_Back()
{
    var factory = _factory;
    using var client = factory.CreateClient();
    await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });

    await client.PutAsJsonAsync("/api/preferences", new
    {
        theme = (string?)null,
        analyticsConsentOptedOut = false,
        preferredBookInfoProviderId = 2
    });

    var response = await client.GetAsync($"/api/books/lookup?isbn={Services.FakeGoogleBooksMetadataLookupService.KnownIsbn}");
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<JsonElement>();

    Assert.Equal(Services.FakeGoogleBooksMetadataLookupService.KnownIsbnTitle, result.GetProperty("title").GetString());
    Assert.Equal(2, result.GetProperty("providerId").GetInt32());
}

[Fact]
public async Task Lookup_By_Isbn_Falls_Back_To_Open_Library_When_Preferred_Google_Books_Has_No_Match()
{
    var factory = _factory;
    using var client = factory.CreateClient();
    await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });

    await client.PutAsJsonAsync("/api/preferences", new
    {
        theme = (string?)null,
        analyticsConsentOptedOut = false,
        preferredBookInfoProviderId = 2
    });

    // This ISBN is only known to the Open Library fake, not the Google Books fake,
    // so a preference for provider 2 must still fall back to provider 1's match.
    var response = await client.GetAsync($"/api/books/lookup?isbn={Services.FakeBookMetadataLookupService.KnownIsbn}");
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<JsonElement>();

    Assert.Equal(Services.FakeBookMetadataLookupService.KnownIsbnTitle, result.GetProperty("title").GetString());
    Assert.Equal(1, result.GetProperty("providerId").GetInt32());
}
```

(adjust the `Services.FakeGoogleBooksMetadataLookupService`/`Services.FakeBookMetadataLookupService` fully-qualified references to a plain `using BookWheel.Tests.Services;` import matching this file's existing style if it already imports that namespace — check the top of `BookWheelApiTests.cs` first.)

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelApiTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add BookWheel/Controllers/BooksController.cs BookWheel/Models/UpdateBookRequest.cs BookWheel/Program.cs BookWheel.Tests/BookWheelWebAppFactory.cs BookWheel.Tests/Services/FakeGoogleBooksMetadataLookupService.cs BookWheel.Tests/BookWheelApiTests.cs
git commit -m "Wire BooksController to BookMetadataLookupDispatcher and provider-preference fallback (GH #70)"
```

---

### Task 6: Frontend — preferences panel, provider threading, i18n

**Files:**
- Modify: `BookWheel/wwwroot/js/i18n.js`
- Modify: `BookWheel/wwwroot/index.html`
- Modify: `BookWheel/wwwroot/js/app.js`
- Test: `BookWheel.Tests/BookWheelFrontendTests.cs`

**Interfaces:**
- Consumes: `GET/PUT /api/preferences` (Task 4), `providerId` on `/api/books/lookup` results and `bookInfoProviderId` on `POST`/`PUT /api/books` (Task 5).

- [ ] **Step 1: Add the new i18n key to all three locales**

In `BookWheel/wwwroot/js/i18n.js`, in the `en` locale's `settings` block, add after `analyticsLabel`:

```javascript
        analyticsLabel: 'Allow anonymous usage analytics',
        bookInfoProviderLabel: 'Book info source',
```

In the `es` locale's `settings` block (near line 325), add after its `analyticsLabel`:

```javascript
        analyticsLabel: 'Permitir análisis de uso anónimo',
        bookInfoProviderLabel: 'Fuente de información del libro',
```

In the `pl` locale's `settings` block (near line 574), add after its `analyticsLabel`:

```javascript
        analyticsLabel: 'Zezwól na anonimową analitykę użytkowania',
        bookInfoProviderLabel: 'Źródło informacji o książce',
```

- [ ] **Step 2: Add the provider select to the Preferences settings panel**

In `BookWheel/wwwroot/index.html`, in `#settingsPreferencesPanel`, add a new row right before the closing `</section>` (after the existing analytics-consent `<label class="checkbox-row settings-row">...</label>` block):

```html
        <label class="settings-row">
          <span data-i18n="settings.bookInfoProviderLabel">Book info source</span>
          <select id="bookInfoProviderSelect">
            <option value="1">Open Library</option>
            <option value="2">Google Books</option>
          </select>
        </label>
```

- [ ] **Step 3: Add DOM element references and state variables in `app.js`**

Near the top of `BookWheel/wwwroot/js/app.js`, after the existing `const analyticsConsentCheckbox = document.getElementById('analyticsConsentCheckbox');` line, add:

```javascript
const bookInfoProviderSelect = document.getElementById('bookInfoProviderSelect');
```

Near the existing `let currentUser = null;` / `let allUsers = [];` declarations, add:

```javascript
let bookAddLookupProviderId = null;
let editBookLookupProviderId = null;
```

- [ ] **Step 4: Add `applyPreferences` and `loadAndApplyPreferences`**

In `app.js`, add these two functions right after the existing `setAnalyticsConsent` function:

```javascript
function applyPreferences(preferences) {
  if (!preferences) {
    return;
  }
  if (preferences.theme) {
    applyTheme(preferences.theme);
  }
  setAnalyticsConsent(!preferences.analyticsConsentOptedOut);
  if (bookInfoProviderSelect) {
    bookInfoProviderSelect.value = String(preferences.preferredBookInfoProviderId || 1);
  }
}

async function loadAndApplyPreferences() {
  try {
    const preferences = await requestJson('/api/preferences');
    applyPreferences(preferences);
  } catch (error) {
    // Preferences are best-effort on top of the locally cached theme/consent
    // already applied at bootstrap; a failed fetch just means this device
    // won't pick up changes made on another device this session.
  }
}

async function savePreferencesIfAuthenticated() {
  if (!currentUser) {
    return;
  }
  try {
    await requestJson('/api/preferences', {
      method: 'PUT',
      body: JSON.stringify({
        theme: document.documentElement.getAttribute('data-theme') || DARK_THEME,
        analyticsConsentOptedOut: isAnalyticsOptedOut(),
        preferredBookInfoProviderId: bookInfoProviderSelect ? (parseInt(bookInfoProviderSelect.value, 10) || null) : null
      })
    });
  } catch (error) {
    // Best-effort: the change is already reflected locally (localStorage/DOM);
    // a failed PUT just means it won't sync to the server until the next save.
  }
}
```

- [ ] **Step 5: Call `savePreferencesIfAuthenticated` from the existing preference controls, and wire the new one**

Change `toggleTheme` to save after applying:

```javascript
function toggleTheme() {
  const currentTheme = document.documentElement.getAttribute('data-theme') || DARK_THEME;
  const currentIndex = THEME_CYCLE.indexOf(currentTheme);
  const nextTheme = THEME_CYCLE[(currentIndex + 1) % THEME_CYCLE.length];
  applyTheme(nextTheme);
  savePreferencesIfAuthenticated();
}
```

Find the existing analytics-consent checkbox listener (`analyticsConsentCheckbox.addEventListener('change', ...)`, around line 2274) and change it to also save:

```javascript
if (analyticsConsentCheckbox) {
  analyticsConsentCheckbox.addEventListener('change', () => {
    setAnalyticsConsent(analyticsConsentCheckbox.checked);
    savePreferencesIfAuthenticated();
  });
}
```

Add a new listener near it for the provider select:

```javascript
if (bookInfoProviderSelect) {
  bookInfoProviderSelect.addEventListener('change', () => {
    savePreferencesIfAuthenticated();
  });
}
```

- [ ] **Step 6: Load preferences after authentication resolves**

In the bootstrap IIFE, find:

```javascript
  applyTheme(getPreferredTheme());
  applyAnalyticsConsent();
  await loadAppVersion();

  try {
    const status = await requestJson('/api/auth/status');
    setAuthMode(status.setupRequired ? 'setup' : 'login');
    const me = await requestJson('/api/auth/me');
    applyCurrentUser({
      userId: me.userId,
      username: me.username,
      isAdmin: me.isAdmin
    });
```

and add a call right after `applyCurrentUser(...)`:

```javascript
    applyCurrentUser({
      userId: me.userId,
      username: me.username,
      isAdmin: me.isAdmin
    });
    await loadAndApplyPreferences();
```

Find the login/setup submit handler's success path:

```javascript
    applyCurrentUser(authResult.user || null);
    showApp(true);
    showToast(authMode === 'setup' ? t('auth.accountCreatedToast') : t('auth.signedInToast'), 'success');
    await refreshBooks();
```

and add the same call:

```javascript
    applyCurrentUser(authResult.user || null);
    await loadAndApplyPreferences();
    showApp(true);
    showToast(authMode === 'setup' ? t('auth.accountCreatedToast') : t('auth.signedInToast'), 'success');
    await refreshBooks();
```

- [ ] **Step 7: Capture `providerId` from lookup results**

Change `applyLookupResult`'s signature and body:

```javascript
function applyLookupResult(result, { titleInput, isbnInput, authorInput, coverInput, previewEl, coverImgEl, authorTextEl, setProviderId }) {
  const titleValue = titleInput.value.trim();
  const isbnValue = isbnInput.value.trim();

  if (!titleValue && result.title) {
    titleInput.value = result.title;
  }
  if (!isbnValue && result.isbn) {
    isbnInput.value = result.isbn;
  }
  authorInput.value = result.author || '';
  coverInput.value = result.coverUrl || '';

  if (setProviderId) {
    setProviderId(typeof result.providerId === 'number' ? result.providerId : null);
  }

  renderMetadataPreview({
    previewEl,
    coverImgEl,
    authorTextEl,
    author: result.author,
    coverUrl: result.coverUrl,
    title: result.title || titleInput.value
  });
}
```

Change the two `runMetadataLookup` call sites to pass a `setProviderId` callback:

```javascript
bookLookupBtn.addEventListener('click', () => runMetadataLookup({
  titleInput: bookTitle,
  isbnInput: bookIsbn,
  authorInput: bookAuthor,
  coverInput: bookCoverUrl,
  previewEl: bookAddPreview,
  coverImgEl: bookAddCoverImg,
  authorTextEl: bookAddAuthorText,
  messageEl: bookMessage,
  setProviderId: value => { bookAddLookupProviderId = value; }
}));

editLookupBtn.addEventListener('click', () => runMetadataLookup({
  titleInput: editBookTitle,
  isbnInput: editBookIsbn,
  authorInput: editBookAuthor,
  coverInput: editBookCoverUrl,
  previewEl: editBookPreview,
  coverImgEl: editBookCoverImg,
  authorTextEl: editBookAuthorText,
  messageEl: editError,
  setProviderId: value => { editBookLookupProviderId = value; }
}));
```

- [ ] **Step 8: Thread `bookInfoProviderId` through add/edit submission, and preserve it across an edit-dialog open**

In `editBook(book)`, add after `editBookType.value = String(book.bookTypeId || 1);`:

```javascript
  editBookLookupProviderId = typeof book.bookInfoProviderId === 'number' ? book.bookInfoProviderId : null;
```

In `saveEdit()`, add `bookInfoProviderId` to the PUT body:

```javascript
  await requestJson(`/api/books/${editBookId.value}`, {
    method: 'PUT',
    body: JSON.stringify({
      title: trimmed,
      isbn: editBookIsbn.value.trim(),
      author: editBookAuthor.value.trim(),
      coverUrl: editBookCoverUrl.value.trim(),
      bookTypeId: parseInt(editBookType.value, 10) || 1,
      bookInfoProviderId: editBookLookupProviderId
    })
  });
```

In the `bookForm` submit handler, add `bookInfoProviderId` to the POST body and reset the tracking variable on success:

```javascript
    await requestJson('/api/books', {
      method: 'POST',
      body: JSON.stringify({
        title: trimmedTitle,
        isbn: bookIsbn.value.trim(),
        author: bookAuthor.value.trim(),
        coverUrl: bookCoverUrl.value.trim(),
        addedByScanner: wasAddedByScanner,
        bookTypeId: parseInt(bookType.value, 10) || 1,
        bookInfoProviderId: bookAddLookupProviderId
      })
    });
    bookTitle.value = '';
    bookIsbn.value = '';
    bookAuthor.value = '';
    bookCoverUrl.value = '';
    bookType.value = '1';
    bookAddLookupProviderId = null;
```

- [ ] **Step 9: Add frontend test coverage**

In `BookWheel.Tests/BookWheelFrontendTests.cs`, add near the existing analytics-consent checkbox test (the one asserting `id="analyticsConsentCheckbox"` and `data-i18n="settings.analyticsLabel"`):

```csharp
[Fact]
public async Task Home_Page_Should_Include_Book_Info_Provider_Select()
{
    var factory = _factory;
    using var client = factory.CreateClient();

    var html = await client.GetStringAsync("/");

    Assert.Contains("id=\"bookInfoProviderSelect\"", html, StringComparison.Ordinal);
    Assert.Contains("data-i18n=\"settings.bookInfoProviderLabel\"", html, StringComparison.Ordinal);
    Assert.Contains("<option value=\"1\">Open Library</option>", html, StringComparison.Ordinal);
    Assert.Contains("<option value=\"2\">Google Books</option>", html, StringComparison.Ordinal);
}
```

Find the existing i18n locale-completeness test (asserting representative translated strings from `js/i18n.js`, e.g. the one containing `Assert.Contains("Iniciar sesión", ...)`), and add:

```csharp
        Assert.Contains("Fuente de información del libro", script, StringComparison.Ordinal);
        Assert.Contains("Źródło informacji o książce", script, StringComparison.Ordinal);
```

- [ ] **Step 10: Run the frontend tests**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelFrontendTests"`
Expected: PASS.

- [ ] **Step 11: Manually verify in a running instance**

Run: `dotnet run --project BookWheel/BookWheel.csproj` (ensure `ConnectionStrings:BookWheel` is configured, e.g. via the bundled `docker-compose.yml` Postgres service), then open the app in a browser:

1. Complete first-run setup.
2. Open Settings → Preferences. Confirm the new "Book info source" row appears with Open Library/Google Books options.
3. Switch it to Google Books, close and reopen Settings — confirm the selection persisted (this proves the `PUT`/`GET /api/preferences` round trip works end-to-end, not just against fakes).
4. Add a book with a known ISBN via Lookup — confirm the lookup still auto-fills author/cover (now via Google Books first, falling back to Open Library if Google Books has no match), and the book is saved successfully.
5. Reload the page — confirm the theme and provider selection are still correct (proves the server-authoritative preferences load correctly on a fresh page load, not just after login).

Report back this manual check passed before proceeding — this is the one step in the plan that unit/integration tests cannot substitute for (Global Constraints note: UI behavior needs a real browser check per this project's standing engineering practice for frontend changes).

- [ ] **Step 12: Commit**

```bash
git add BookWheel/wwwroot/js/i18n.js BookWheel/wwwroot/index.html BookWheel/wwwroot/js/app.js BookWheel.Tests/BookWheelFrontendTests.cs
git commit -m "Add book-info-provider preference UI and server-side preference sync (GH #70)"
```

---

### Task 7: Documentation and version bump

**Files:**
- Modify: `README.md`
- Modify: `docs/audits/database.md`
- Modify: `BookWheel/BookWheel.csproj`

**Interfaces:**
- None — this task only updates documentation and the version string; no code interfaces change.

- [ ] **Step 1: Add a Features bullet**

In `README.md`'s `## Features` section, add a new bullet after the existing "Book type classification..." bullet (GH #73):

```markdown
- Book info providers: book lookups can be served by Open Library or Google Books, selectable per-user from Settings → Preferences ("Book info source"); if the preferred provider has no match, the other provider is tried automatically before giving up. Available providers are backed by a `book_info_providers` reference table, and each book records which provider actually supplied its data (null for manually-entered books, or books added before this feature). Google Books calls the public keyless endpoint by default; set `BookMetadata:GoogleBooks:ApiKey` (or the `BookMetadata__GoogleBooks__ApiKey` environment variable) to use an API key for a higher request quota (GH #70)
```

- [ ] **Step 2: Document the new preferences endpoints**

In `README.md`'s `## API Overview` section, add after the `GET /api/auth/me` auth-endpoints bullet list (before the `Operational endpoint (admin only):` line):

```markdown
Preferences endpoints (authentication required):

- `GET /api/preferences` — returns the authenticated user's `theme`, `analyticsConsentOptedOut`, and `preferredBookInfoProviderId` (all `null`/`false` by default for a user who has never set one)
- `PUT /api/preferences` — replaces all three preference values in one call; `theme` must be `dark`, `light`, or `high-contrast` (or omitted/`null`), and `preferredBookInfoProviderId` must be a known provider id (or omitted/`null`)
```

Add a sentence to the existing `GET /api/books/lookup` bullet, changing:

```markdown
- `GET /api/books/lookup?isbn={isbn}` or `GET /api/books/lookup?title={title}` — queries the Open Library API for a book's title, author, ISBN, and cover URL; returns `404` when nothing matches and `400` when neither `isbn` nor `title` is supplied or the ISBN fails checksum validation
```

to:

```markdown
- `GET /api/books/lookup?isbn={isbn}` or `GET /api/books/lookup?title={title}` — queries the authenticated user's preferred book-info provider (Open Library or Google Books, falling back to the other provider if the preferred one has no match) for a book's title, author, ISBN, and cover URL, plus which provider answered (`providerId`); returns `404` when nothing matches and `400` when neither `isbn` nor `title` is supplied or the ISBN fails checksum validation
```

And extend the sentence about `POST`/`PUT /api/books` accepted fields:

```markdown
`POST /api/books` and `PUT /api/books/{id}` accept optional `isbn`, `author`, `coverUrl`, and `bookInfoProviderId` fields alongside the required `title`. A supplied `isbn` is validated (ISBN-10 or ISBN-13, hyphens/spaces ignored) and rejected with `400` if it fails checksum validation.
```

- [ ] **Step 3: Update the Testing section**

In `README.md`'s `## Testing` section, add a bullet to the "Current integration tests cover:" list (after the existing title-lookup/GH #63 bullet):

```markdown
- Book info provider preference: `/api/books/lookup` uses the authenticated user's preferred provider (Open Library or Google Books) and falls back to the other provider when the preferred one has no match; `POST`/`PUT /api/books` persist which provider actually supplied a book's data (`null` for manual entries); `GET`/`PUT /api/preferences` round-trip theme, analytics-consent, and provider preference, rejecting unknown values with `400` (GH #70)
```

Update the closing integration-test-fakes paragraph, changing:

```markdown
Integration tests never call the real Open Library API: `BookWheelWebAppFactory` substitutes a deterministic `IBookMetadataLookupService` fake, and `OpenLibraryBookMetadataLookupServiceTests` exercises the real HTTP-parsing logic against a stubbed `HttpMessageHandler` (success, not-found, malformed JSON, and network-failure cases).
```

to:

```markdown
Integration tests never call the real Open Library or Google Books APIs: `BookWheelWebAppFactory` substitutes a `BookMetadataLookupDispatcher` built from two deterministic `IBookMetadataLookupService` fakes (one per provider), and `OpenLibraryBookMetadataLookupServiceTests`/`GoogleBooksBookMetadataLookupServiceTests` exercise each provider's real HTTP-parsing logic against a stubbed `HttpMessageHandler` (success, not-found, malformed JSON, and network-failure cases). `BookMetadataLookupDispatcherTests` covers the preferred-provider/fallback selection logic in isolation, with no HTTP involved at all.
```

- [ ] **Step 4: Update the Data Storage section's table-ownership list**

In `README.md`'s `## Data Storage` section, add `book_info_providers` to the `ALTER TABLE ... OWNER TO bookwheel_migrator;` block (used when upgrading an existing deployment's volume):

```sql
docker exec bookwheel-postgres psql -U bookwheel -d bookwheel -c '
  ALTER TABLE "__EFMigrationsHistory" OWNER TO bookwheel_migrator;
  ALTER TABLE book_info_providers OWNER TO bookwheel_migrator;
  ALTER TABLE book_types OWNER TO bookwheel_migrator;
  ALTER TABLE books OWNER TO bookwheel_migrator;
  ALTER TABLE password_reset_tokens OWNER TO bookwheel_migrator;
  ALTER TABLE spin_selections OWNER TO bookwheel_migrator;
  ALTER TABLE users OWNER TO bookwheel_migrator;
'
```

- [ ] **Step 5: Note the new table in the database audit doc**

In `docs/audits/database.md`, in the "1. No FK-navigation / no DB-level FK constraints" section, extend the list of bare-column relationships in the first sentence to include the two new ones:

```markdown
Verified against every migration and the live schema: relationships are modeled purely as bare `Guid`/`int` columns with `HasIndex` (`books.UserId`, `books.BookTypeId`, `books.BookInfoProviderId`, `books.CreatedByUserId`, `books.LastUpdatedByUserId`, `users.PreferredBookInfoProviderId`, `spin_selections.UserId`/`BookId`, `password_reset_tokens.UserId`) — none of these are `FOREIGN KEY` constraints in the live `\d+` output.
```

(this is a factual audit document about existing schema state, so it's fine — expected — for a later feature to extend this list; no new "finding" numbered section is needed, this is just keeping an existing factual sentence accurate.)

- [ ] **Step 6: Bump the version**

In `BookWheel/BookWheel.csproj`, change:

```xml
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">2.16.2</InformationalVersion>
```

to:

```xml
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">2.17.0</InformationalVersion>
```

- [ ] **Step 7: Run the full test suite one final time**

Run: `dotnet test BookWheel.slnx`
Expected: PASS — every test in the solution, including the new `/api/version` behavior implicitly reflecting `2.17.0` (no test currently asserts the literal version string, per the existing pattern of not hardcoding it in tests — confirm this is still true by checking for any test asserting `2.16.2` literally; update it to `2.17.0` if one exists).

- [ ] **Step 8: Commit**

```bash
git add README.md docs/audits/database.md BookWheel/BookWheel.csproj
git commit -m "Document Google Books provider support and bump version to 2.17.0 (GH #70)"
```

---

## Self-Review Notes

- **Spec coverage:** Google Books provider (Task 2), user preference for provider selection (Task 4 + Task 6), `book_info_providers` table (Task 1), provider stamping on books (Task 1 + Task 5), theme/analytics-consent migration to server-side (Task 4 + Task 6), tests (every task), docs (Task 7), version bump (Task 7) — all spec sections have a corresponding task.
- **Type consistency checked:** `BookMetadataResult.ProviderId` (Task 2) flows unchanged through `BookMetadataLookupDispatcher` (Task 3) into `BooksController.Lookup`'s response (Task 5) into `applyLookupResult`'s `result.providerId` (Task 6). `IBookRepository.AddAsync/UpdateAsync`'s `bookInfoProviderId` parameter (Task 1) is called from `BooksController` (Task 5) with `request.BookInfoProviderId` (added to `UpdateBookRequest` in Task 5 Step 2). `UserPreferences`'s three fields (Task 4) match `UpdatePreferencesRequest`'s three fields exactly and match what `app.js`'s `applyPreferences`/`savePreferencesIfAuthenticated` read/write (Task 6).
- **Test infra verified against the real file:** `BookWheelWebAppFactory.StartAsync()`/`ResetAsync()` and the `POST /api/auth/setup` (which signs the client in on its own, per `AuthController.Setup`) pattern used throughout Tasks 1, 4, and 5's new tests were confirmed by reading `BookWheel.Tests/BookWheelApiTests.cs` and `BookWheel.Tests/BookWheelWebAppFactory.cs` directly, not assumed.
