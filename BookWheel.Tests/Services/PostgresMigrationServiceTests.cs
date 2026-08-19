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
