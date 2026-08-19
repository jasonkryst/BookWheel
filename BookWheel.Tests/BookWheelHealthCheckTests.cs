using BookWheel.HealthChecks;
using BookWheel.Storage.Postgres;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;

namespace BookWheel.Tests;

public sealed class BookWheelHealthCheckTests
{
    [Fact]
    public async Task Database_HealthCheck_Returns_Unhealthy_When_Connection_Fails()
    {
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<BookWheelDbContext>(o => o.UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=nobody;Password=nobody;Timeout=1"));
        var contextFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookWheelDbContext>>();
        var check = new DatabaseHealthCheck(contextFactory);

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
