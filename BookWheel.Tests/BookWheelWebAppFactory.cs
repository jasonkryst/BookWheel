using BookWheel.Services;
using BookWheel.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace BookWheel.Tests;

public sealed class BookWheelWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _tempContentRoot;
    private readonly TestLoggerProvider _loggerProvider = new();

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(_tempContentRoot);

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(_loggerProvider);
            logging.AddProvider(new JsonFileLoggerProvider(LogDirectoryPath));
        });

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