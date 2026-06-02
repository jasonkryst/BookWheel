using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookWheel.HealthChecks;

public sealed class LoggingHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _environment;

    public LoggingHealthCheck(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var logDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "logs");
            Directory.CreateDirectory(logDirectory);
            var probePath = Path.Combine(logDirectory, ".logging-health");
            File.WriteAllText(probePath, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(probePath);
            return Task.FromResult(HealthCheckResult.Healthy("Logging path is writable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Logging path is not writable.", ex));
        }
    }
}
