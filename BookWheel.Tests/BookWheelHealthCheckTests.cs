using BookWheel.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;

namespace BookWheel.Tests;

public sealed class BookWheelHealthCheckTests
{
    [Fact]
    public async Task Storage_HealthCheck_Returns_Unhealthy_When_Path_Is_Not_Directory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var fileAsRoot = Path.Combine(tempRoot, "content-root-file");
        await File.WriteAllTextAsync(fileAsRoot, "blocked");

        var env = new StubEnvironment(fileAsRoot);
        var check = new StorageHealthCheck(env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Logging_HealthCheck_Returns_Unhealthy_When_Path_Is_Not_Directory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var fileAsRoot = Path.Combine(tempRoot, "content-root-file");
        await File.WriteAllTextAsync(fileAsRoot, "blocked");

        var env = new StubEnvironment(fileAsRoot);
        var check = new LoggingHealthCheck(env);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public StubEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
            WebRootPath = contentRootPath;
            EnvironmentName = "Testing";
            ApplicationName = "BookWheel";
        }

        public string ApplicationName { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
