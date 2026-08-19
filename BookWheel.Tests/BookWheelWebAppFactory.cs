using BookWheel.Services;
using BookWheel.Storage;
using BookWheel.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

        // Test call sites construct this factory directly (`new BookWheelWebAppFactory()`), not via
        // IClassFixture<T>, so there is no async lifecycle hook available — start synchronously.
        _postgresContainer.StartAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(_tempContentRoot);
        builder.UseSetting("ConnectionStrings:BookWheel", _postgresContainer.GetConnectionString());

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
