# PostgreSQL Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat-file `books.json` / `user.cred` / `password-reset-tokens.dat` storage with PostgreSQL via EF Core, without changing any controller, `AuthService`, or public API behavior, and with a one-shot CLI migration tool to move existing data across.

**Architecture:** New `BookWheel/Storage/Postgres/` folder holds an EF Core `BookWheelDbContext`, three entity classes, and three repository classes (`PostgresBookRepository`, `PostgresCredentialRepository`, `PostgresPasswordResetTokenRepository`) that implement the *existing* `IBookRepository`/`ICredentialRepository`/`IPasswordResetTokenRepository` interfaces untouched since [[2026-08-03-storage-layer-abstraction]]. `Program.cs` swaps the DI registrations for those three interfaces from `Json*` to `Postgres*`; the `Json*` classes stay in the codebase, no longer wired to the interfaces, used only by a new `PostgresMigrationService` that reads them directly (the same pattern `DataMigrationService` already uses) to perform a one-shot copy into Postgres via a `--migrate-to-postgres` CLI flag. Logs (`App_Data/logs/*.jsonl`) and Data Protection keys stay exactly as they are today — out of scope.

**Tech Stack:** .NET 8, EF Core 8 (`Npgsql.EntityFrameworkCore.PostgreSQL`), PostgreSQL 16, `Testcontainers.PostgreSql` for tests, xUnit.

**Spec:** No separate spec document exists. Requirements were established via conversation on 2026-08-18: ORM = EF Core, scope = books/credentials/password-reset-tokens only (audit logs and Data Protection keys stay on disk), cutover = one-shot migration (no dual-write/dual-read mode).

## Global Constraints

- `IBookRepository`, `ICredentialRepository`, `IPasswordResetTokenRepository` (in `BookWheel/Storage/`) are **not modified** — controllers and `AuthService` must not change at all.
- No dual-mode runtime: after this plan ships, `IBookRepository`/`ICredentialRepository`/`IPasswordResetTokenRepository` resolve to `Postgres*` implementations only. There is no configuration switch back to JSON at runtime.
- `Json*Repository` classes are **not deleted**. They remain exactly as they are today (behavior-unchanged) and are used only by `PostgresMigrationService` for the one-shot migration read path.
- Audit logs (`App_Data/logs/*.jsonl`) and ASP.NET Core Data Protection keys (`App_Data/DataProtection-Keys` / `DataProtection:KeyDirectory`) stay file-based. Do not move them to Postgres.
- No field-level encryption of Postgres columns. `Username` moves from a `IDataProtector`-encrypted JSON blob to a plain `citext` column (needed for case-insensitive uniqueness enforcement at the DB level); `PasswordHash` stays an irreversible `PasswordHasher<string>` hash either way, so this is not a security regression for passwords. Production deployments should rely on TLS-in-transit to Postgres and disk/volume encryption at rest, same as any other ASP.NET Core app backed by SQL.
- Single-instance deployment assumption is preserved (matches today's in-memory `AuthService` session/lockout dictionaries, which are not multi-instance-safe either). No distributed locking is introduced; EF Core's per-call `SaveChangesAsync` plus DB-level unique constraints provide equivalent safety to today's single-process `SemaphoreSlim`.
- `PasswordHasher<string>.HashPassword(username, password)` must be called with the exact same two arguments the JSON repository uses today, so hashes migrated from `user.cred` continue to validate after cutover.
- New test files under `BookWheel.Tests/Storage/Postgres/` follow the existing project's xUnit pattern; Postgres-backed tests use `Testcontainers.PostgreSql`, never a mocked/fake DB context.

---

### Task 1: Add EF Core / Npgsql / Testcontainers packages

**Files:**
- Modify: `BookWheel/BookWheel.csproj`
- Modify: `BookWheel.Tests/BookWheel.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (BookWheel project) and `Testcontainers.PostgreSql` (test project) available for Task 2 onward.

- [ ] **Step 1: Add EF Core/Npgsql packages to the app project**

```xml
<!-- BookWheel/BookWheel.csproj — inside a new or existing ItemGroup -->
<ItemGroup>
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

- [ ] **Step 2: Add Testcontainers to the test project**

```xml
<!-- BookWheel.Tests/BookWheel.Tests.csproj — inside a new or existing ItemGroup -->
<ItemGroup>
  <PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
</ItemGroup>
```

- [ ] **Step 3: Restore and build**

Run: `dotnet restore BookWheel.slnx && dotnet build BookWheel.slnx`
Expected: builds clean, no compile errors (nothing references the new packages yet).

- [ ] **Step 4: Commit**

```bash
git add BookWheel/BookWheel.csproj BookWheel.Tests/BookWheel.Tests.csproj
git commit -m "Add EF Core/Npgsql and Testcontainers.PostgreSql packages"
```

---

### Task 2: Entities, DbContext, design-time factory, and initial migration

**Files:**
- Create: `BookWheel/Storage/Postgres/Entities/UserEntity.cs`
- Create: `BookWheel/Storage/Postgres/Entities/BookEntity.cs`
- Create: `BookWheel/Storage/Postgres/Entities/PasswordResetTokenEntity.cs`
- Create: `BookWheel/Storage/Postgres/BookWheelDbContext.cs`
- Create: `BookWheel/Storage/Postgres/BookWheelDbContextFactory.cs`
- Create: `BookWheel/Migrations/*.cs` (generated by `dotnet ef migrations add`)
- Modify: `BookWheel/appsettings.json`

**Interfaces:**
- Consumes: nothing new
- Produces: `BookWheelDbContext` with `DbSet<UserEntity> Users`, `DbSet<BookEntity> Books`, `DbSet<PasswordResetTokenEntity> PasswordResetTokens`, consumed by every task from here on. Entity shapes below are final — later tasks map to/from them.

- [ ] **Step 1: Create the entity classes**

```csharp
// BookWheel/Storage/Postgres/Entities/UserEntity.cs
namespace BookWheel.Storage.Postgres.Entities;

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool ForcePasswordReset { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

```csharp
// BookWheel/Storage/Postgres/Entities/BookEntity.cs
namespace BookWheel.Storage.Postgres.Entities;

public sealed class BookEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
}
```

```csharp
// BookWheel/Storage/Postgres/Entities/PasswordResetTokenEntity.cs
namespace BookWheel.Storage.Postgres.Entities;

public sealed class PasswordResetTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
```

- [ ] **Step 2: Create the DbContext**

```csharp
// BookWheel/Storage/Postgres/BookWheelDbContext.cs
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class BookWheelDbContext : DbContext
{
    public BookWheelDbContext(DbContextOptions<BookWheelDbContext> options) : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<BookEntity> Books => Set<BookEntity>();
    public DbSet<PasswordResetTokenEntity> PasswordResetTokens => Set<PasswordResetTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).HasColumnType("citext").IsRequired();
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<BookEntity>(entity =>
        {
            entity.ToTable("books");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Title).IsRequired();
            entity.HasIndex(b => b.UserId);
        });

        modelBuilder.Entity<PasswordResetTokenEntity>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
        });
    }
}
```

No foreign-key constraints between `BookEntity.UserId`/`PasswordResetTokenEntity.UserId` and `UserEntity.Id` — the current JSON model has no enforced referential relationship either (books live in a dictionary keyed by user id string; cascade cleanup happens explicitly via `RemoveUserDataAsync`, not a DB cascade). Keeping this loosely coupled avoids changing that behavior.

- [ ] **Step 3: Create the design-time factory (needed for `dotnet ef migrations add` without a live DB)**

```csharp
// BookWheel/Storage/Postgres/BookWheelDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookWheel.Storage.Postgres;

public sealed class BookWheelDbContextFactory : IDesignTimeDbContextFactory<BookWheelDbContext>
{
    public BookWheelDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=bookwheel;Username=bookwheel;Password=design-time-only");
        return new BookWheelDbContext(optionsBuilder.Options);
    }
}
```

- [ ] **Step 4: Add the (empty) connection string setting**

```json
// BookWheel/appsettings.json — add as a new top-level property
"ConnectionStrings": {
  "BookWheel": ""
},
```

- [ ] **Step 5: Install the EF Core CLI tool (if not already installed) and generate the initial migration**

Run:
```bash
dotnet tool install --global dotnet-ef --version 8.0.11 || dotnet tool update --global dotnet-ef --version 8.0.11
dotnet ef migrations add InitialCreate --project BookWheel/BookWheel.csproj --startup-project BookWheel/BookWheel.csproj --output-dir Migrations
```
Expected: `BookWheel/Migrations/<timestamp>_InitialCreate.cs`, `<timestamp>_InitialCreate.Designer.cs`, and `BookWheelDbContextModelSnapshot.cs` are created. Inspect the generated `Up()` method — it must create the `citext` extension and the `users`, `books`, `password_reset_tokens` tables with the indexes from Step 2.

- [ ] **Step 6: Build**

Run: `dotnet build BookWheel.slnx`
Expected: builds clean.

- [ ] **Step 7: Commit**

```bash
git add BookWheel/Storage/Postgres BookWheel/Migrations BookWheel/appsettings.json
git commit -m "Add BookWheelDbContext, entities, and initial EF Core migration"
```

---

### Task 3: Shared Postgres test fixture

**Files:**
- Create: `BookWheel.Tests/Storage/Postgres/PostgresTestFixture.cs`
- Create: `BookWheel.Tests/Storage/Postgres/PostgresTestFixtureTests.cs`

**Interfaces:**
- Consumes: `BookWheelDbContext` (Task 2)
- Produces: `PostgresTestFixture` with `string ConnectionString`, `Task ResetAsync()`, used by every Postgres-backed test class from Task 4 onward via `[Collection(PostgresCollection.Name)]`.

- [ ] **Step 1: Create the fixture and collection definition**

```csharp
// BookWheel.Tests/Storage/Postgres/PostgresTestFixture.cs
using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BookWheel.Tests.Storage.Postgres;

public sealed class PostgresTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("bookwheel_test")
        .WithUsername("bookwheel_test")
        .WithPassword("bookwheel_test")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public BookWheelDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString);
        return new BookWheelDbContext(optionsBuilder.Options);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE books, password_reset_tokens, users RESTART IDENTITY CASCADE;");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "Postgres";
}
```

- [ ] **Step 2: Write the fixture smoke test**

```csharp
// BookWheel.Tests/Storage/Postgres/PostgresTestFixtureTests.cs
namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresTestFixtureTests
{
    private readonly PostgresTestFixture _fixture;

    public PostgresTestFixtureTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fixture_Provides_A_Reachable_Migrated_Database()
    {
        await using var context = _fixture.CreateContext();

        var canConnect = await context.Database.CanConnectAsync();

        Assert.True(canConnect);
    }
}
```

- [ ] **Step 3: Run the test**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresTestFixtureTests`
Expected: PASS (requires a local Docker daemon; Testcontainers pulls `postgres:16-alpine` on first run).

- [ ] **Step 4: Commit**

```bash
git add BookWheel.Tests/Storage/Postgres/PostgresTestFixture.cs BookWheel.Tests/Storage/Postgres/PostgresTestFixtureTests.cs
git commit -m "Add shared Testcontainers Postgres fixture for repository tests"
```

---

### Task 4: `PostgresBookRepository`

**Files:**
- Create: `BookWheel/Storage/Postgres/PostgresBookRepository.cs`
- Create: `BookWheel.Tests/Storage/Postgres/PostgresBookRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<BookWheelDbContext>` (via DI, constructed directly with a fixture-backed factory in tests), `IBookRepository` (pre-existing)
- Produces: `PostgresBookRepository : IBookRepository`, consumed by Task 8 (DI wiring) and Task 7 (migration target).

- [ ] **Step 1: Write the failing tests (mirrors `JsonBookRepositoryTests` coverage)**

```csharp
// BookWheel.Tests/Storage/Postgres/PostgresBookRepositoryTests.cs
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
}
```

- [ ] **Step 2: Run the tests to verify they fail (missing `PostgresBookRepository`)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresBookRepositoryTests`
Expected: FAIL to compile — `PostgresBookRepository` does not exist.

- [ ] **Step 3: Implement `PostgresBookRepository`**

```csharp
// BookWheel/Storage/Postgres/PostgresBookRepository.cs
using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresBookRepository : IBookRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresBookRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Books
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Id)
            .Select(b => new BookRecord { Id = b.Id, Title = b.Title })
            .ToListAsync();
    }

    public async Task<BookRecord> AddAsync(Guid userId, string title)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = new BookEntity { Id = Guid.NewGuid(), UserId = userId, Title = title.Trim() };
        context.Books.Add(entity);
        await context.SaveChangesAsync();
        return new BookRecord { Id = entity.Id, Title = entity.Title };
    }

    public async Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id)
            ?? throw new InvalidOperationException("Book not found.");
        entity.Title = title.Trim();
        await context.SaveChangesAsync();
        return new BookRecord { Id = entity.Id, Title = entity.Title };
    }

    public async Task<BookRecord> SelectRandomAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var books = await context.Books.Where(b => b.UserId == userId).ToListAsync();
        if (books.Count == 0)
        {
            throw new InvalidOperationException("No books are available in the wheel.");
        }

        var selected = books[Random.Shared.Next(books.Count)];
        return new BookRecord { Id = selected.Id, Title = selected.Title };
    }

    public async Task<BookRecord> RemoveAsync(Guid userId, Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Books.FirstOrDefaultAsync(b => b.UserId == userId && b.Id == id)
            ?? throw new InvalidOperationException("Book not found.");
        context.Books.Remove(entity);
        await context.SaveChangesAsync();
        return new BookRecord { Id = entity.Id, Title = entity.Title };
    }

    public async Task<int> RemoveUserDataAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var books = await context.Books.Where(b => b.UserId == userId).ToListAsync();
        if (books.Count == 0)
        {
            return 0;
        }

        context.Books.RemoveRange(books);
        await context.SaveChangesAsync();
        return books.Count;
    }

    public async Task<int> GetTotalBookCountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Books.CountAsync();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresBookRepositoryTests`
Expected: PASS (all 9 tests).

- [ ] **Step 5: Commit**

```bash
git add BookWheel/Storage/Postgres/PostgresBookRepository.cs BookWheel.Tests/Storage/Postgres/PostgresBookRepositoryTests.cs
git commit -m "Add PostgresBookRepository with Testcontainers-backed tests"
```

---

### Task 5: `PostgresCredentialRepository`

**Files:**
- Create: `BookWheel/Storage/Postgres/PostgresCredentialRepository.cs`
- Create: `BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<BookWheelDbContext>` (Task 2)
- Produces: `PostgresCredentialRepository : ICredentialRepository`, consumed by Task 8 and Task 7.

- [ ] **Step 1: Write the failing tests (mirrors `JsonCredentialRepositoryTests` coverage)**

```csharp
// BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs
using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresCredentialRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresCredentialRepository _repository = null!;

    public PostgresCredentialRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresCredentialRepository(contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
    public async Task ValidateCredentialsAsync_Is_Case_Insensitive_On_Username()
    {
        await _repository.CreateInitialAccountAsync("Admin-One", "correct-password");

        var result = await _repository.ValidateCredentialsAsync("admin-one", "correct-password");

        Assert.NotNull(result);
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
    public async Task GetUsernameAsync_Returns_Null_For_Unknown_User()
    {
        var username = await _repository.GetUsernameAsync(Guid.NewGuid());

        Assert.Null(username);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (missing `PostgresCredentialRepository`)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresCredentialRepositoryTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement `PostgresCredentialRepository`**

```csharp
// BookWheel/Storage/Postgres/PostgresCredentialRepository.cs
using System.Security.Cryptography;
using BookWheel.Models;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresCredentialRepository : ICredentialRepository
{
    private static readonly PasswordHasher<string> PasswordHasher = new();
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresCredentialRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<bool> HasAccountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Users.AnyAsync();
    }

    public async Task<CredentialRecord> CreateInitialAccountAsync(string username, string password)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (await context.Users.AnyAsync())
        {
            throw new InvalidOperationException("An account already exists.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Username and password are required.");
        }

        var normalizedUsername = username.Trim();
        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
            IsAdmin = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Users.Add(entity);
        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var normalizedUsername = username.Trim();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername);
        if (entity is null)
        {
            return null;
        }

        var result = PasswordHasher.VerifyHashedPassword(entity.Username, entity.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? ToRecord(entity)
            : null;
    }

    public async Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Users
            .OrderBy(u => u.CreatedAtUtc)
            .Select(u => new UserAccountSummary
            {
                UserId = u.Id,
                Username = u.Username,
                IsAdmin = u.IsAdmin,
                IsDisabled = u.IsDisabled,
                ForcePasswordReset = u.ForcePasswordReset,
                IsLocked = u.IsLocked,
                LockedUntilUtc = u.LockedUntilUtc,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync();
    }

    public async Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (!await context.Users.AnyAsync())
        {
            throw new InvalidOperationException("Create the initial account first.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        var normalizedUsername = username.Trim();
        if (await context.Users.AnyAsync(u => u.Username == normalizedUsername))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = PasswordHasher.HashPassword(normalizedUsername, GenerateTemporaryPassword()),
            IsAdmin = isAdmin,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Users.Add(entity);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        return ToSummary(entity);
    }

    public Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin)
    {
        return UpdateUserCoreAsync(userId, username, isAdmin, isDisabled: null, forcePasswordReset: null, isLocked: null);
    }

    public Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked)
    {
        return UpdateUserCoreAsync(userId, username, isAdmin, isDisabled, forcePasswordReset, isLocked);
    }

    private async Task<UserAccountSummary> UpdateUserCoreAsync(Guid userId, string username, bool isAdmin, bool? isDisabled, bool? forcePasswordReset, bool? isLocked)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("Username is required.");
        }

        var duplicateExists = await context.Users.AnyAsync(u => u.Id != userId && u.Username == normalizedUsername);
        if (duplicateExists)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        if (!isAdmin)
        {
            var adminCount = await context.Users.CountAsync(u => u.IsAdmin);
            if (entity.IsAdmin && adminCount <= 1)
            {
                throw new InvalidOperationException("At least one administrator account is required.");
            }
        }

        entity.Username = normalizedUsername;
        entity.IsAdmin = isAdmin;

        if (isDisabled.HasValue)
        {
            entity.IsDisabled = isDisabled.Value;
        }

        if (forcePasswordReset.HasValue)
        {
            entity.ForcePasswordReset = forcePasswordReset.Value;
        }

        if (isLocked.HasValue)
        {
            entity.IsLocked = isLocked.Value;
            entity.LockedUntilUtc = isLocked.Value ? DateTimeOffset.UtcNow.AddHours(12) : null;
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        return ToSummary(entity);
    }

    public async Task<UserAccountSummary> DeleteUserAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var firstUserId = await context.Users
            .OrderBy(u => u.CreatedAtUtc)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (firstUserId == userId)
        {
            throw new InvalidOperationException("The first account cannot be removed.");
        }

        context.Users.Remove(entity);
        await context.SaveChangesAsync();
        return ToSummary(entity);
    }

    public async Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        entity.ForcePasswordReset = true;
        entity.IsLocked = false;
        entity.LockedUntilUtc = null;

        await context.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<string> SetPasswordAsync(Guid userId, string newPassword)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found for this reset link.");

        entity.PasswordHash = PasswordHasher.HashPassword(entity.Username, newPassword);
        entity.ForcePasswordReset = false;
        entity.IsLocked = false;
        entity.LockedUntilUtc = null;

        await context.SaveChangesAsync();
        return entity.Username;
    }

    public async Task<string?> GetUsernameAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync();
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    private static CredentialRecord ToRecord(UserEntity entity) => new()
    {
        UserId = entity.Id,
        Username = entity.Username,
        PasswordHash = entity.PasswordHash,
        IsAdmin = entity.IsAdmin,
        IsDisabled = entity.IsDisabled,
        ForcePasswordReset = entity.ForcePasswordReset,
        IsLocked = entity.IsLocked,
        LockedUntilUtc = entity.LockedUntilUtc,
        CreatedAtUtc = entity.CreatedAtUtc
    };

    private static UserAccountSummary ToSummary(UserEntity entity) => new()
    {
        UserId = entity.Id,
        Username = entity.Username,
        IsAdmin = entity.IsAdmin,
        IsDisabled = entity.IsDisabled,
        ForcePasswordReset = entity.ForcePasswordReset,
        IsLocked = entity.IsLocked,
        LockedUntilUtc = entity.LockedUntilUtc,
        CreatedAtUtc = entity.CreatedAtUtc
    };

    private static string GenerateTemporaryPassword()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresCredentialRepositoryTests`
Expected: PASS (all 12 tests).

- [ ] **Step 5: Commit**

```bash
git add BookWheel/Storage/Postgres/PostgresCredentialRepository.cs BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs
git commit -m "Add PostgresCredentialRepository with Testcontainers-backed tests"
```

---

### Task 6: `PostgresPasswordResetTokenRepository`

**Files:**
- Create: `BookWheel/Storage/Postgres/PostgresPasswordResetTokenRepository.cs`
- Create: `BookWheel.Tests/Storage/Postgres/PostgresPasswordResetTokenRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<BookWheelDbContext>` (Task 2)
- Produces: `PostgresPasswordResetTokenRepository : IPasswordResetTokenRepository`, consumed by Task 8 and Task 7.

- [ ] **Step 1: Write the failing tests (mirrors `JsonPasswordResetTokenRepositoryTests` coverage)**

```csharp
// BookWheel.Tests/Storage/Postgres/PostgresPasswordResetTokenRepositoryTests.cs
using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresPasswordResetTokenRepositoryTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private PostgresPasswordResetTokenRepository _repository = null!;

    public PostgresPasswordResetTokenRepositoryTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        _repository = new PostgresPasswordResetTokenRepository(contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
}
```

- [ ] **Step 2: Run the tests to verify they fail (missing `PostgresPasswordResetTokenRepository`)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresPasswordResetTokenRepositoryTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement `PostgresPasswordResetTokenRepository`**

```csharp
// BookWheel/Storage/Postgres/PostgresPasswordResetTokenRepository.cs
using System.Security.Cryptography;
using System.Text;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class PostgresPasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresPasswordResetTokenRepository(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var expiredOrExisting = await context.PasswordResetTokens
            .Where(t => t.ExpiresAtUtc <= now || t.UserId == userId)
            .ToListAsync();
        context.PasswordResetTokens.RemoveRange(expiredOrExisting);

        var rawToken = GenerateResetToken();
        var expiresAtUtc = now.AddHours(24);
        context.PasswordResetTokens.Add(new PasswordResetTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        });

        await context.SaveChangesAsync();
        return (rawToken, expiresAtUtc);
    }

    public async Task<PasswordResetTokenLookup> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PasswordResetTokenLookup { IsValid = false };
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var expired = await context.PasswordResetTokens.Where(t => t.ExpiresAtUtc <= now).ToListAsync();
        context.PasswordResetTokens.RemoveRange(expired);
        await context.SaveChangesAsync();

        var tokenHash = HashToken(token.Trim());
        var match = await context.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        return match is null
            ? new PasswordResetTokenLookup { IsValid = false }
            : new PasswordResetTokenLookup { IsValid = true, UserId = match.UserId, ExpiresAtUtc = match.ExpiresAtUtc };
    }

    public async Task<Guid> CompleteAsync(string token)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var expired = await context.PasswordResetTokens.Where(t => t.ExpiresAtUtc <= now).ToListAsync();
        context.PasswordResetTokens.RemoveRange(expired);

        var tokenHash = HashToken((token ?? string.Empty).Trim());
        var match = await context.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash)
            ?? throw new InvalidOperationException("The password reset link is invalid or has expired.");

        context.PasswordResetTokens.Remove(match);
        await context.SaveChangesAsync();
        return match.UserId;
    }

    private static string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresPasswordResetTokenRepositoryTests`
Expected: PASS (all 5 tests).

- [ ] **Step 5: Commit**

```bash
git add BookWheel/Storage/Postgres/PostgresPasswordResetTokenRepository.cs BookWheel.Tests/Storage/Postgres/PostgresPasswordResetTokenRepositoryTests.cs
git commit -m "Add PostgresPasswordResetTokenRepository with Testcontainers-backed tests"
```

---

### Task 7: One-shot JSON-to-Postgres migration tool

**Files:**
- Modify: `BookWheel/Storage/JsonBookRepository.cs`
- Modify: `BookWheel/Storage/JsonCredentialRepository.cs`
- Modify: `BookWheel/Storage/JsonPasswordResetTokenRepository.cs`
- Create: `BookWheel/Models/PostgresMigrationReport.cs`
- Create: `BookWheel/Services/PostgresMigrationService.cs`
- Modify: `BookWheel/Program.cs`
- Create: `BookWheel.Tests/Services/PostgresMigrationServiceTests.cs`

**Interfaces:**
- Consumes: `JsonBookRepository`, `JsonCredentialRepository`, `JsonPasswordResetTokenRepository` (existing, unchanged behavior), `DataMigrationService.RunAsync()` (existing), `PostgresBookRepository`/`PostgresCredentialRepository`/`PostgresPasswordResetTokenRepository` entity shapes (Tasks 4-6)
- Produces: `PostgresMigrationService.RunAsync() : Task<PostgresMigrationReport>`, invoked from `Program.cs` via `--migrate-to-postgres`.

- [ ] **Step 1: Add migration-read methods to the three `Json*` repositories**

```csharp
// BookWheel/Storage/JsonBookRepository.cs — add as a new public method, anywhere in the class body
public async Task<Dictionary<string, List<BookRecord>>> GetAllForMigrationAsync()
{
    await _gate.WaitAsync();
    try
    {
        return await ReadStoreUnsafeAsync();
    }
    finally
    {
        _gate.Release();
    }
}
```

```csharp
// BookWheel/Storage/JsonCredentialRepository.cs — add as a new public method, anywhere in the class body
public async Task<List<CredentialRecord>> GetAllForMigrationAsync()
{
    await _gate.WaitAsync();
    try
    {
        return await ReadUsersUnsafeAsync();
    }
    finally
    {
        _gate.Release();
    }
}
```

```csharp
// BookWheel/Storage/JsonPasswordResetTokenRepository.cs — add as a new public method, anywhere in the class body
public async Task<List<PasswordResetTokenRecord>> GetAllForMigrationAsync()
{
    await _gate.WaitAsync();
    try
    {
        return await ReadTokensUnsafeAsync();
    }
    finally
    {
        _gate.Release();
    }
}
```

- [ ] **Step 2: Create the migration report model**

```csharp
// BookWheel/Models/PostgresMigrationReport.cs
namespace BookWheel.Models;

public sealed class PostgresMigrationReport
{
    public DateTimeOffset ExecutedAtUtc { get; set; }
    public int UsersMigrated { get; set; }
    public int BooksMigrated { get; set; }
    public int PasswordResetTokensMigrated { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Write the failing migration service test**

```csharp
// BookWheel.Tests/Services/PostgresMigrationServiceTests.cs
using BookWheel.Services;
using BookWheel.Storage;
using BookWheel.Storage.Postgres;
using BookWheel.Tests.Storage;
using BookWheel.Tests.Storage.Postgres;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests.Services;

[Collection(PostgresCollection.Name)]
public sealed class PostgresMigrationServiceTests : IAsyncLifetime, IDisposable
{
    private readonly PostgresTestFixture _fixture;
    private readonly string _contentRoot;
    private PostgresMigrationService _service = null!;
    private JsonCredentialRepository _jsonCredentialRepository = null!;

    public PostgresMigrationServiceTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _contentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-pg-migration-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();

        var environment = StorageTestEnvironment.Create(_contentRoot);
        var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_contentRoot, "keys")));

        _jsonCredentialRepository = new JsonCredentialRepository(environment, dataProtectionProvider);
        var jsonBookRepository = new JsonBookRepository(environment);
        var jsonTokenRepository = new JsonPasswordResetTokenRepository(environment, dataProtectionProvider);
        var legacyMigrationService = new DataMigrationService(_jsonCredentialRepository, jsonBookRepository);

        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();

        _service = new PostgresMigrationService(
            legacyMigrationService,
            _jsonCredentialRepository,
            jsonBookRepository,
            jsonTokenRepository,
            contextFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
    public async Task RunAsync_Copies_Users_Books_And_Tokens_Into_Postgres()
    {
        var admin = await _jsonCredentialRepository.CreateInitialAccountAsync("admin-one", "correct-password");

        var report = await _service.RunAsync();

        Assert.Equal(1, report.UsersMigrated);

        await using var context = _fixture.CreateContext();
        var migratedUser = await context.Users.SingleAsync();
        Assert.Equal(admin.UserId, migratedUser.Id);
        Assert.Equal("admin-one", migratedUser.Username);
        Assert.True(migratedUser.IsAdmin);
    }

    [Fact]
    public async Task RunAsync_Twice_Throws_On_Second_Run()
    {
        await _jsonCredentialRepository.CreateInitialAccountAsync("admin-one", "correct-password");
        await _service.RunAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RunAsync());
    }
}
```

- [ ] **Step 4: Run the test to verify it fails (missing `PostgresMigrationService`)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresMigrationServiceTests`
Expected: FAIL to compile.

- [ ] **Step 5: Implement `PostgresMigrationService`**

```csharp
// BookWheel/Services/PostgresMigrationService.cs
using BookWheel.Models;
using BookWheel.Storage;
using BookWheel.Storage.Postgres;
using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Services;

public sealed class PostgresMigrationService
{
    private readonly DataMigrationService _legacyJsonMigrationService;
    private readonly JsonCredentialRepository _jsonCredentialRepository;
    private readonly JsonBookRepository _jsonBookRepository;
    private readonly JsonPasswordResetTokenRepository _jsonPasswordResetTokenRepository;
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public PostgresMigrationService(
        DataMigrationService legacyJsonMigrationService,
        JsonCredentialRepository jsonCredentialRepository,
        JsonBookRepository jsonBookRepository,
        JsonPasswordResetTokenRepository jsonPasswordResetTokenRepository,
        IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _legacyJsonMigrationService = legacyJsonMigrationService;
        _jsonCredentialRepository = jsonCredentialRepository;
        _jsonBookRepository = jsonBookRepository;
        _jsonPasswordResetTokenRepository = jsonPasswordResetTokenRepository;
        _contextFactory = contextFactory;
    }

    public async Task<PostgresMigrationReport> RunAsync()
    {
        await _legacyJsonMigrationService.RunAsync();

        var users = await _jsonCredentialRepository.GetAllForMigrationAsync();
        var booksByUser = await _jsonBookRepository.GetAllForMigrationAsync();
        var tokens = await _jsonPasswordResetTokenRepository.GetAllForMigrationAsync();

        await using var context = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        if (await context.Users.AnyAsync())
        {
            throw new InvalidOperationException(
                "PostgreSQL already contains user data. Refusing to overwrite an existing migration target.");
        }

        foreach (var user in users)
        {
            context.Users.Add(new UserEntity
            {
                Id = user.UserId,
                Username = user.Username,
                PasswordHash = user.PasswordHash,
                IsAdmin = user.IsAdmin,
                IsDisabled = user.IsDisabled,
                ForcePasswordReset = user.ForcePasswordReset,
                IsLocked = user.IsLocked,
                LockedUntilUtc = user.LockedUntilUtc,
                CreatedAtUtc = user.CreatedAtUtc
            });
        }

        var booksMigrated = 0;
        foreach (var (userIdKey, books) in booksByUser)
        {
            if (!Guid.TryParse(userIdKey, out var userId))
            {
                continue;
            }

            foreach (var book in books)
            {
                context.Books.Add(new BookEntity { Id = book.Id, UserId = userId, Title = book.Title });
                booksMigrated++;
            }
        }

        foreach (var token in tokens)
        {
            context.PasswordResetTokens.Add(new PasswordResetTokenEntity
            {
                Id = Guid.NewGuid(),
                UserId = token.UserId,
                TokenHash = token.TokenHash,
                CreatedAtUtc = token.CreatedAtUtc,
                ExpiresAtUtc = token.ExpiresAtUtc
            });
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new PostgresMigrationReport
        {
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            UsersMigrated = users.Count,
            BooksMigrated = booksMigrated,
            PasswordResetTokensMigrated = tokens.Count,
            Message = users.Count == 0 && booksMigrated == 0 && tokens.Count == 0
                ? "No legacy file data found to migrate."
                : "Legacy file data migrated to PostgreSQL."
        };
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter PostgresMigrationServiceTests`
Expected: PASS (both tests).

- [ ] **Step 7: Wire the `--migrate-to-postgres` CLI flag into `Program.cs`**

```csharp
// BookWheel/Program.cs — add this DI registration next to the existing DataMigrationService line
builder.Services.AddSingleton<PostgresMigrationService>();
```

```csharp
// BookWheel/Program.cs — add this branch immediately after the existing `runMigrationOnly` block
// (both read `args`, so order between them doesn't matter; place after the JSON-only migration block)
var runPostgresMigrationOnly = args.Any(arg => string.Equals(arg, "--migrate-to-postgres", StringComparison.OrdinalIgnoreCase));
if (runPostgresMigrationOnly)
{
    var postgresMigrationService = app.Services.GetRequiredService<PostgresMigrationService>();
    var postgresReport = await postgresMigrationService.RunAsync();
    Console.WriteLine(JsonSerializer.Serialize(postgresReport, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
    return;
}
```

Note: this requires `IDbContextFactory<BookWheelDbContext>` to already be registered and migrated for `PostgresMigrationService` to resolve — that registration lands in Task 8. This step only adds the CLI plumbing; it will not run correctly against a real database until Task 8 completes. Do not attempt to run `--migrate-to-postgres` end-to-end yet.

- [ ] **Step 8: Build**

Run: `dotnet build BookWheel.slnx`
Expected: builds clean (the `PostgresMigrationService` DI registration compiles even though `IDbContextFactory<BookWheelDbContext>` isn't registered yet — that failure would only surface at runtime, not build time).

- [ ] **Step 9: Commit**

```bash
git add BookWheel/Storage/JsonBookRepository.cs BookWheel/Storage/JsonCredentialRepository.cs BookWheel/Storage/JsonPasswordResetTokenRepository.cs BookWheel/Models/PostgresMigrationReport.cs BookWheel/Services/PostgresMigrationService.cs BookWheel/Program.cs BookWheel.Tests/Services/PostgresMigrationServiceTests.cs
git commit -m "Add one-shot JSON-to-Postgres migration service and --migrate-to-postgres CLI flag"
```

---

### Task 8: DI cutover, database health check, and full-suite Postgres wiring

**Files:**
- Modify: `BookWheel/Program.cs`
- Create: `BookWheel/HealthChecks/DatabaseHealthCheck.cs`
- Delete: `BookWheel/HealthChecks/StorageHealthCheck.cs`
- Modify: `BookWheel.Tests/BookWheelWebAppFactory.cs`
- Modify: `BookWheel.Tests/BookWheelHealthCheckTests.cs`

**Interfaces:**
- Consumes: `PostgresBookRepository`, `PostgresCredentialRepository`, `PostgresPasswordResetTokenRepository` (Tasks 4-6), `BookWheelDbContext` (Task 2)
- Produces: `IBookRepository`/`ICredentialRepository`/`IPasswordResetTokenRepository` now resolve to Postgres implementations app-wide; every existing API/browser/frontend/smoke test (`BookWheelApiTests.cs`, `BookWheelBrowserWorkflowTests.cs`, `BookWheelFrontendTests.cs`, `BookWheelPwaTests.cs`, `BookWheelSmokeTests.cs`) runs against Postgres via `BookWheelWebAppFactory` unmodified in its own test bodies.

- [ ] **Step 1: Register the DbContext factory and apply migrations at startup, in `Program.cs`**

```csharp
// BookWheel/Program.cs — add after the DataProtection block, before the existing repository registrations
var connectionString = builder.Configuration.GetConnectionString("BookWheel");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:BookWheel is not configured. Set it in appsettings.json, an environment variable (ConnectionStrings__BookWheel), or a deployment secret.");
}

builder.Services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(connectionString));
```

- [ ] **Step 2: Swap the repository DI registrations**

```csharp
// BookWheel/Program.cs — replace the existing block:
// builder.Services.AddSingleton<JsonBookRepository>();
// builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<JsonBookRepository>());
//
// builder.Services.AddSingleton<JsonCredentialRepository>();
// builder.Services.AddSingleton<ICredentialRepository>(sp => sp.GetRequiredService<JsonCredentialRepository>());
//
// builder.Services.AddSingleton<JsonPasswordResetTokenRepository>();
// builder.Services.AddSingleton<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<JsonPasswordResetTokenRepository>());
//
// with:
builder.Services.AddSingleton<JsonBookRepository>();
builder.Services.AddSingleton<JsonCredentialRepository>();
builder.Services.AddSingleton<JsonPasswordResetTokenRepository>();

builder.Services.AddSingleton<PostgresBookRepository>();
builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<PostgresBookRepository>());

builder.Services.AddSingleton<PostgresCredentialRepository>();
builder.Services.AddSingleton<ICredentialRepository>(sp => sp.GetRequiredService<PostgresCredentialRepository>());

builder.Services.AddSingleton<PostgresPasswordResetTokenRepository>();
builder.Services.AddSingleton<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<PostgresPasswordResetTokenRepository>());
```

`JsonBookRepository`, `JsonCredentialRepository`, `JsonPasswordResetTokenRepository` stay registered as concrete singletons — `DataMigrationService` and `PostgresMigrationService` (Task 7) still depend on them directly — but they are no longer bound to the three interfaces, so nothing in the live request path uses them anymore.

- [ ] **Step 3: Apply EF Core migrations at startup, before any command branch**

```csharp
// BookWheel/Program.cs — add immediately after `var app = builder.Build();`, before the
// `informationalVersion`/`appVersion` block
using (var migrationScope = app.Services.CreateScope())
{
    var dbContextFactory = migrationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
    await using var startupDbContext = await dbContextFactory.CreateDbContextAsync();
    await startupDbContext.Database.MigrateAsync();
}
```

This runs for every startup path, including `--migrate-data` and the new `--migrate-to-postgres`, so the schema always exists before `PostgresMigrationService.RunAsync()` reads/writes it.

- [ ] **Step 4: Update the health check registration**

```csharp
// BookWheel/Program.cs — replace:
// .AddCheck<StorageHealthCheck>("storage", tags: ["ready"])
// with:
.AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
```

- [ ] **Step 5: Replace `StorageHealthCheck` with `DatabaseHealthCheck`**

```csharp
// BookWheel/HealthChecks/DatabaseHealthCheck.cs
using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookWheel.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public DatabaseHealthCheck(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connectivity check failed.", ex);
        }
    }
}
```

Delete `BookWheel/HealthChecks/StorageHealthCheck.cs`.

- [ ] **Step 6: Update `BookWheelHealthCheckTests.cs`**

Replace the `Storage_HealthCheck_Returns_Unhealthy_When_Path_Is_Not_Directory` test (which constructs `StorageHealthCheck` directly against a stub `IWebHostEnvironment`) with:

```csharp
// BookWheel.Tests/BookWheelHealthCheckTests.cs — replace the Storage_HealthCheck test with:
using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;

[Fact]
public async Task Database_HealthCheck_Returns_Unhealthy_When_Connection_Fails()
{
    var optionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
    optionsBuilder.UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Username=nobody;Password=nobody;Timeout=1");
    var services = new ServiceCollection();
    services.AddSingleton(optionsBuilder.Options);
    services.AddPooledDbContextFactory<BookWheelDbContext>(o => o.UseNpgsql(
        "Host=127.0.0.1;Port=1;Database=unreachable;Username=nobody;Password=nobody;Timeout=1"));
    var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
    var check = new DatabaseHealthCheck(contextFactory);

    var result = await check.CheckHealthAsync(new HealthCheckContext());

    Assert.Equal(HealthStatus.Unhealthy, result.Status);
}
```

(Keep `Logging_HealthCheck_Returns_Unhealthy_When_Path_Is_Not_Directory` and its `StubEnvironment` helper unchanged — logging stays file-based.) Remove the now-unused `using BookWheel.HealthChecks;`-only `StorageHealthCheck` reference if any remains, and add `using Microsoft.Extensions.DependencyInjection;` at the top of the file if not already present.

A `Database_HealthCheck_Returns_Healthy_When_Reachable` positive-path test belongs with the Postgres-backed suite — add it to `PostgresTestFixtureTests.cs` (Task 3) instead:

```csharp
// BookWheel.Tests/Storage/Postgres/PostgresTestFixtureTests.cs — add alongside the existing test
[Fact]
public async Task DatabaseHealthCheck_Returns_Healthy_When_Reachable()
{
    var services = new ServiceCollection();
    services.AddPooledDbContextFactory<BookWheelDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
    var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
    var check = new DatabaseHealthCheck(contextFactory);

    var result = await check.CheckHealthAsync(new HealthCheckContext());

    Assert.Equal(HealthStatus.Healthy, result.Status);
}
```
(Add `using BookWheel.HealthChecks;`, `using Microsoft.Extensions.Diagnostics.HealthChecks;`, `using Microsoft.Extensions.DependencyInjection;` to that file's usings.)

- [ ] **Step 7: Rewire `BookWheelWebAppFactory` to run each test class against its own Testcontainers Postgres**

```csharp
// BookWheel.Tests/BookWheelWebAppFactory.cs — full replacement
using BookWheel.Services;
using BookWheel.Storage;
using BookWheel.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace BookWheel.Tests;

public sealed class BookWheelWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _tempContentRoot;
    private readonly TestLoggerProvider _loggerProvider = new();
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("bookwheel_test")
        .WithUsername("bookwheel_test")
        .WithPassword("bookwheel_test")
        .Build();

    public string ContentRootPath => _tempContentRoot;

    public string LogDirectoryPath => Path.Combine(_tempContentRoot, "App_Data", "logs");

    public TestLoggerProvider LoggerProvider => _loggerProvider;

    public BookWheelWebAppFactory()
    {
        _tempContentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempContentRoot);

        var tempWebRoot = Path.Combine(_tempContentRoot, "wwwroot");
        Directory.CreateDirectory(tempWebRoot);

        var sourceProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BookWheel"));
        var sourceWebRoot = Path.Combine(sourceProjectRoot, "wwwroot");
        CopyDirectory(sourceWebRoot, tempWebRoot);
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(_tempContentRoot);

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BookWheel"] = _postgresContainer.GetConnectionString()
            });
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(_loggerProvider);
            logging.AddProvider(new JsonFileLoggerProvider(LogDirectoryPath));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        if (Directory.Exists(_tempContentRoot))
        {
            try
            {
                Directory.Delete(_tempContentRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, destination, overwrite: true);
        }
    }
}
```

The old `JsonBookRepository`-specific DI override (`RemoveAll<JsonBookRepository>()` + custom `TestWebHostEnvironment`) is gone — it existed only because that repository needed a manually-constructed `IWebHostEnvironment`, and `IBookRepository` no longer resolves to a `Json*` type at all. `Database.MigrateAsync()` runs automatically inside `Program.cs` at host startup (Task 8, Step 3), so no separate migration step is needed here.

Each of the 5 test classes that use `IClassFixture<BookWheelWebAppFactory>` (`BookWheelApiTests`, `BookWheelBrowserWorkflowTests`, `BookWheelFrontendTests`, `BookWheelPwaTests`, `BookWheelSmokeTests`) now starts its own Postgres container — accepted overhead for full host-level isolation between test classes, distinct from the single shared container the repository-level tests use via `PostgresTestFixture`.

- [ ] **Step 8: Build and run the full test suite**

Run: `dotnet build BookWheel.slnx && dotnet test BookWheel.slnx --verbosity normal`
Expected: PASS — every existing test in `BookWheelApiTests.cs`, `BookWheelBrowserWorkflowTests.cs`, `BookWheelFrontendTests.cs`, `BookWheelPwaTests.cs`, `BookWheelSmokeTests.cs`, `BookWheelHealthCheckTests.cs` passes unmodified in its own test body, now running against Postgres. If any test fails, check first whether it depended on `JsonBookRepository`/`JsonCredentialRepository` file-corruption behavior (`CorruptedDataException`) — those tests live only in `BookWheel.Tests/Storage/Json*RepositoryTests.cs` and are unaffected by this task; they should still pass unchanged since the `Json*` classes themselves were not modified.

- [ ] **Step 9: Commit**

```bash
git add BookWheel/Program.cs BookWheel/HealthChecks/DatabaseHealthCheck.cs BookWheel.Tests/BookWheelWebAppFactory.cs BookWheel.Tests/BookWheelHealthCheckTests.cs BookWheel.Tests/Storage/Postgres/PostgresTestFixtureTests.cs
git rm BookWheel/HealthChecks/StorageHealthCheck.cs
git commit -m "Cut over IBookRepository/ICredentialRepository/IPasswordResetTokenRepository to Postgres"
```

---

### Task 9: `docker-compose.yml` — add a Postgres service

**Files:**
- Modify: `docker-compose.yml`

**Interfaces:**
- Consumes: `ConnectionStrings__BookWheel` env var read by `Program.cs` (Task 8)
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the `postgres` service, its volume, and wire `bookwheel` to depend on it**

```yaml
# docker-compose.yml — full replacement
version: "3.9"

services:
  postgres:
    image: postgres:16-alpine
    container_name: bookwheel-postgres
    environment:
      POSTGRES_DB: bookwheel
      POSTGRES_USER: bookwheel
      POSTGRES_PASSWORD: bookwheel
    volumes:
      - bookwheel_pg_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U bookwheel -d bookwheel"]
      interval: 5s
      timeout: 5s
      retries: 10
    restart: unless-stopped

  bookwheel:
    build:
      context: .
      dockerfile: Dockerfile
    image: bookwheel:latest
    container_name: bookwheel
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__BookWheel: "Host=postgres;Database=bookwheel;Username=bookwheel;Password=bookwheel"
    volumes:
      - bookwheel_app_data:/app/App_Data
      - bookwheel_dp_keys:/home/app/.aspnet/DataProtection-Keys
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped

volumes:
  bookwheel_app_data:
    name: bookwheel_app_data
  bookwheel_dp_keys:
    name: bookwheel_dp_keys
  bookwheel_pg_data:
    name: bookwheel_pg_data
```

The committed `POSTGRES_PASSWORD`/connection-string password (`bookwheel`) is a local-dev default, same posture as the rest of this compose file (no secrets manager integration exists today). Document overriding it via a `.env` file for anyone running this outside local dev (covered in Task 11).

- [ ] **Step 2: Manually verify the compose stack starts and the app is healthy**

Run: `docker compose up --build -d`
Then: `docker compose ps` (both containers should show healthy/running), and `curl -fsS http://localhost:8080/health/ready` should return success.
Cleanup: `docker compose down` (add `-v` only if you intend to discard the local Postgres volume — do not do this by default).

- [ ] **Step 3: Commit**

```bash
git add docker-compose.yml
git commit -m "Add postgres service to docker-compose.yml"
```

---

### Task 10: CI — Postgres for the container smoke test

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: nothing new
- Produces: nothing consumed by later tasks.

The `unit-tests` job needs **no changes** — `Testcontainers.PostgreSql` (Task 3) starts its own Postgres container using the Docker daemon already available on `ubuntu-latest` GitHub Actions runners. Only `container-smoke-test` needs a reachable Postgres, since it runs the built image standalone and hits `/health/ready`, which now checks DB connectivity instead of file writability.

- [ ] **Step 1: Add a Postgres container and a shared Docker network to the `container-smoke-test` job**

```yaml
# .github/workflows/ci.yml — inside the container-smoke-test job, replace the existing
# "Container startup smoke verification" and "Cleanup smoke container" steps with:
      - name: Start Postgres for smoke test
        run: |
          docker network create bookwheel-smoke-net
          docker run -d --name bookwheel-smoke-postgres --network bookwheel-smoke-net \
            -e POSTGRES_DB=bookwheel -e POSTGRES_USER=bookwheel -e POSTGRES_PASSWORD=bookwheel \
            postgres:16-alpine
          for _ in {1..20}; do
            if docker exec bookwheel-smoke-postgres pg_isready -U bookwheel -d bookwheel > /dev/null 2>&1; then
              echo "postgres ready"
              exit 0
            fi
            sleep 2
          done
          echo "Postgres failed readiness check"
          docker logs bookwheel-smoke-postgres
          exit 1
      - name: Container startup smoke verification
        run: |
          docker run -d --name bookwheel-smoke --network bookwheel-smoke-net \
            -e ConnectionStrings__BookWheel="Host=bookwheel-smoke-postgres;Database=bookwheel;Username=bookwheel;Password=bookwheel" \
            -p 18080:8080 bookwheel:${{ github.sha }}
          for _ in {1..20}; do
            if curl -fsS http://127.0.0.1:18080/health/ready > /dev/null; then
              echo "ready"
              exit 0
            fi
            sleep 2
          done
          echo "Container failed readiness check"
          docker logs bookwheel-smoke
          exit 1
      - name: Cleanup smoke container
        if: always()
        run: |
          docker rm -f bookwheel-smoke bookwheel-smoke-postgres || true
          docker network rm bookwheel-smoke-net || true
```

- [ ] **Step 2: Push the branch and confirm CI passes**

Run: `git push -u origin <branch-name>` and check the GitHub Actions run for this workflow.
Expected: `unit-tests` and `container-smoke-test` jobs both pass.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "Run the container smoke test against a Postgres sidecar container"
```

---

### Task 11: Documentation updates

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: nothing
- Produces: nothing (final task)

- [ ] **Step 1: Update the Prerequisites section**

Add a bullet: `- PostgreSQL 16+ (or use the bundled \`docker-compose.yml\` service) — required at startup; set \`ConnectionStrings:BookWheel\` or the \`ConnectionStrings__BookWheel\` environment variable`

- [ ] **Step 2: Rewrite the "Data Storage" section**

Replace the "Book data is stored in... Credential data is stored in..." paragraphs with:

```markdown
## Data Storage

Book, credential, and password-reset-token data is stored in PostgreSQL, configured via the `ConnectionStrings:BookWheel` setting (or the `ConnectionStrings__BookWheel` environment variable in containerized deployments). EF Core migrations run automatically at startup.

Log data is stored in:

- `BookWheel/App_Data/logs/bookwheel-YYYY-MM-DD.jsonl`

Each line is a JSON object with structured fields such as timestamp, level, category, message, request id, path, client IP, and user agent. Log storage did not move to PostgreSQL — it remains file-based, same as before.

Backup and restore guidance:

- Back up PostgreSQL with `pg_dump`/`pg_restore` (or your managed Postgres provider's snapshot tooling) on the schedule your data warrants.
- Back up `BookWheel/App_Data/logs/` and `BookWheel/App_Data/DataProtection-Keys/` (or your configured `DataProtection:KeyDirectory`) separately — these remain file-based.
- Restore by stopping the app, restoring the PostgreSQL database from a `pg_dump` backup, replacing the log/Data-Protection-key directories from their file backups, and starting the app again.
```

- [ ] **Step 3: Extend the "Legacy Data Migration Utility" section with the Postgres one-shot step**

Add after the existing `--migrate-data` documentation:

```markdown
### Migrating from file-based storage to PostgreSQL

If you are upgrading from a version of Book Wheel that stored data in `App_Data/books.json` and `App_Data/user.cred`, run the one-shot migration once, with `ConnectionStrings:BookWheel` pointed at your new PostgreSQL database and the existing `App_Data/` directory still in place:

```bash
dotnet run --project BookWheel/BookWheel.csproj -- --migrate-to-postgres
```

This normalizes any legacy JSON schema first (same as `--migrate-data`), then copies all users, books, and password reset tokens into PostgreSQL in a single transaction, and exits. It refuses to run if PostgreSQL already has user data, so it is safe to leave in a deployment script — a second run is a no-op error, not a duplicate-data hazard. The original `App_Data/books.json` and `App_Data/user.cred` files are left untouched on disk as a historical backup; the running application no longer reads them.
```

- [ ] **Step 4: Update "Upgrading Without Losing Data" and "Container Support"**

In "Upgrading Without Losing Data", add a bullet noting that `docker compose pull && docker compose up -d` now also needs the `postgres` volume (`bookwheel_pg_data`) preserved, same caution as the existing `App_Data`/Data-Protection-keys volumes — do not run `docker compose down -v` unless data loss is intended.

In "Container Support" → "The compose setup persists", add: `- PostgreSQL data (\`bookwheel_pg_data\` volume)`.

- [ ] **Step 5: Update Troubleshooting**

Replace `If the app starts but books are missing, check BookWheel/App_Data/books.json permissions.` with `If the app starts but books/users are missing, verify PostgreSQL connectivity via GET /health/ready and check the ConnectionStrings:BookWheel value.`

- [ ] **Step 6: Commit**

```bash
git add README.md
git commit -m "Document PostgreSQL storage, one-shot migration, and updated backup/restore guidance"
```

---

## Self-Review

**Spec coverage:**
- ORM = EF Core → Tasks 1-2 add `Npgsql.EntityFrameworkCore.PostgreSQL`/`Microsoft.EntityFrameworkCore.Design`, `BookWheelDbContext`, migrations. ✓
- Scope = books/credentials/tokens only, logs and DP keys stay on disk → Global Constraints state this explicitly; no task touches `JsonFileLoggerProvider`, `DataProtection:KeyDirectory`, or `App_Data/logs`. ✓
- One-shot cutover → Task 7's `PostgresMigrationService` is a single guarded run (refuses a second run), Task 8 is a hard DI swap with no dual-mode switch. ✓
- `IBookRepository`/`ICredentialRepository`/`IPasswordResetTokenRepository` unchanged, zero controller/`AuthService` changes → confirmed no task modifies `BookWheel/Controllers/` or `BookWheel/Services/AuthService.cs`. ✓
- Existing behavior/invariants preserved (case-insensitive username uniqueness, last-admin protection, first-account-cannot-be-removed, one-time password reset tokens, per-user book isolation) → replicated in Tasks 4-6 with matching test coverage. ✓
- CI/docker-compose/docs → Tasks 9-11. ✓

**Placeholder scan:** no `TBD`/`TODO`/"add appropriate" phrasing found; every step has concrete code or a concrete shell command.

**Type consistency:** `BookWheelDbContext`, `UserEntity`/`BookEntity`/`PasswordResetTokenEntity`, `PostgresBookRepository`/`PostgresCredentialRepository`/`PostgresPasswordResetTokenRepository`, `PostgresMigrationService`/`PostgresMigrationReport`, `DatabaseHealthCheck`, and `PostgresTestFixture`/`PostgresCollection` are named consistently across every task that references them (Tasks 2 → 4/5/6/7/8 all use the same type and method names introduced in Task 2).
