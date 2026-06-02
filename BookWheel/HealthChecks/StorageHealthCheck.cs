using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookWheel.HealthChecks;

public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _environment;

    public StorageHealthCheck(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var appDataPath = Path.Combine(_environment.ContentRootPath, "App_Data");
            Directory.CreateDirectory(appDataPath);
            var probePath = Path.Combine(appDataPath, ".storage-health");
            File.WriteAllText(probePath, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(probePath);
            return Task.FromResult(HealthCheckResult.Healthy("Storage is writable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage path is not writable.", ex));
        }
    }
}
