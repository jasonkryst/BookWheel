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
