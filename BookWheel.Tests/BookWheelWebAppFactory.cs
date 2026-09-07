using BookWheel.Services;
using BookWheel.Storage;
using BookWheel.Storage.Postgres;
using BookWheel.Logging;
using BookWheel.Tests.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace BookWheel.Tests;

public sealed class BookWheelWebAppFactory : WebApplicationFactory<Program>
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

    public async Task StartAsync()
    {
        await _postgresContainer.StartAsync();

        // Force the WebApplicationFactory host to build now (idempotent — a no-op on
        // subsequent calls once built). Building the host runs Program.cs's top-level
        // startup code, including the one-time MigrateAsync() call that creates every
        // table — this must happen before ResetAsync() can safely TRUNCATE them.
        _ = Server;
    }

    public async Task ResetAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
        optionsBuilder.UseNpgsql(_postgresContainer.GetConnectionString());
        await using var context = new BookWheelDbContext(optionsBuilder.Options);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE books, password_reset_tokens, users, spin_selections RESTART IDENTITY CASCADE;");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(_tempContentRoot);
        builder.UseSetting("ConnectionStrings:BookWheel", _postgresContainer.GetConnectionString());

        // Test classes now share one host (and therefore one in-process per-username
        // lockout counter in AuthService) across many test methods instead of getting a
        // fresh host per test. No test in this project exercises username-lockout
        // behavior directly (verified: no references to UsernameLockout/IsLockedOut/
        // LockedUntilUtc anywhere under BookWheel.Tests), so raise the threshold high
        // enough that the volume of intentional-failure logins across a shared class
        // never trips it as a side effect.
        builder.UseSetting("Security:UsernameLockoutThreshold", "100000");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(_loggerProvider);
            logging.AddProvider(new JsonFileLoggerProvider(LogDirectoryPath));
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BookMetadataLookupDispatcher>();
            services.AddSingleton(new BookMetadataLookupDispatcher(
                new FakeBookMetadataLookupService(),
                new FakeGoogleBooksMetadataLookupService()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            _postgresContainer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore cleanup failures in tests.
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
