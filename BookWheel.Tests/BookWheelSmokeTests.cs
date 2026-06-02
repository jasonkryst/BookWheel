using System.Net;
using System.Net.Http.Json;

namespace BookWheel.Tests;

public sealed class BookWheelSmokeTests
{
    [Fact]
    public async Task Startup_Health_And_Version_Endpoints_Return_Success()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var readyResponse = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        var versionResponse = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);
    }

    [Fact]
    public async Task Writable_App_Data_Paths_Are_Available_During_Runtime()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        var appDataPath = Path.Combine(factory.ContentRootPath, "App_Data");
        var logsPath = Path.Combine(appDataPath, "logs");
        var probePath = Path.Combine(appDataPath, "smoke-probe.txt");

        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(logsPath);

        await File.WriteAllTextAsync(probePath, "ok");
        var content = await File.ReadAllTextAsync(probePath);

        Assert.Equal("ok", content);
        Assert.True(Directory.Exists(logsPath));
    }

    [Fact]
    public async Task Docker_Artifacts_Define_Persistent_Data_And_Runtime_Probe_Configuration()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var composePath = Path.Combine(solutionRoot, "docker-compose.yml");
        var dockerfilePath = Path.Combine(solutionRoot, "Dockerfile");

        var compose = await File.ReadAllTextAsync(composePath);
        var dockerfile = await File.ReadAllTextAsync(dockerfilePath);

        Assert.Contains("bookwheel_app_data", compose, StringComparison.Ordinal);
        Assert.Contains("/app/App_Data", compose, StringComparison.Ordinal);
        Assert.Contains("/home/app/.aspnet/DataProtection-Keys", compose, StringComparison.Ordinal);

        Assert.Contains("VOLUME", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/app/App_Data", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/home/app/.aspnet/DataProtection-Keys", dockerfile, StringComparison.Ordinal);
    }
}
