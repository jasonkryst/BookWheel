using System.Text;
using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class LogShippingService : BackgroundService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ObservabilityOptions> _observabilityOptions;
    private readonly ILogger<LogShippingService> _logger;

    public LogShippingService(
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        IOptions<ObservabilityOptions> observabilityOptions,
        ILogger<LogShippingService> logger)
    {
        _environment = environment;
        _httpClientFactory = httpClientFactory;
        _observabilityOptions = observabilityOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ShipLogsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Log shipping pass failed.");
            }

            var pollSeconds = Math.Max(5, _observabilityOptions.Value.LogShipping.PollIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
        }
    }

    private async Task ShipLogsAsync(CancellationToken cancellationToken)
    {
        var options = _observabilityOptions.Value.LogShipping;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            return;
        }

        var logsDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return;
        }

        var latestFile = Directory
            .GetFiles(logsDirectory, "bookwheel-*.jsonl")
            .OrderBy(path => path, StringComparer.Ordinal)
            .LastOrDefault();

        if (latestFile is null)
        {
            return;
        }

        var batchSize = Math.Max(10, options.BatchSize);
        var lines = (await File.ReadAllLinesAsync(latestFile, cancellationToken))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(batchSize)
            .ToArray();

        if (lines.Length == 0)
        {
            return;
        }

        var payload = "[" + string.Join(',', lines) + "]";
        using var request = new HttpRequestMessage(HttpMethod.Post, options.EndpointUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", options.ApiKey);
        }

        var client = _httpClientFactory.CreateClient("central-log-shipper");
        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Shipped {Count} log entries to centralized sink endpoint {Endpoint}", lines.Length, options.EndpointUrl);
        }
        else
        {
            _logger.LogWarning("Log shipping endpoint returned status {StatusCode}", response.StatusCode);
        }
    }
}
