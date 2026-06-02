using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class StartupDiagnosticsService : IHostedService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IOptions<ObservabilityOptions> _observabilityOptions;
    private readonly ILogger<StartupDiagnosticsService> _logger;

    public StartupDiagnosticsService(
        IWebHostEnvironment environment,
        IOptions<ObservabilityOptions> observabilityOptions,
        ILogger<StartupDiagnosticsService> logger)
    {
        _environment = environment;
        _observabilityOptions = observabilityOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_observabilityOptions.Value.EnableStartupDiagnostics)
        {
            _logger.LogInformation("Startup diagnostics are disabled by configuration.");
            return Task.CompletedTask;
        }

        var appDataDirectory = Path.Combine(_environment.ContentRootPath, "App_Data");
        var logsDirectory = Path.Combine(appDataDirectory, "logs");
        var corruptDirectory = Path.Combine(appDataDirectory, "corrupt");

        ValidateDirectory(appDataDirectory, "App_Data");
        ValidateDirectory(logsDirectory, "App_Data/logs");
        ValidateDirectory(corruptDirectory, "App_Data/corrupt");

        _logger.LogInformation(
            "Startup diagnostics completed for environment {EnvironmentName}. Content root {ContentRootPath}",
            _environment.EnvironmentName,
            _environment.ContentRootPath);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void ValidateDirectory(string path, string logicalName)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probeFile = Path.Combine(path, ".startup-diagnostics");
            File.WriteAllText(probeFile, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(probeFile);

            _logger.LogInformation(
                "Startup diagnostics validated writable directory {LogicalName} at {Path}",
                logicalName,
                path);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Startup diagnostics failed for {LogicalName} at {Path}. Check volume mounts and filesystem permissions.",
                logicalName,
                path);
        }
    }
}
