using System.Text;
using System.Text.Json;
using BookWheel.Models;
using Microsoft.Extensions.Options;

namespace BookWheel.Services;

public sealed class LogShippingService : BackgroundService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ObservabilityOptions> _observabilityOptions;
    private readonly ILogger<LogShippingService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private const int MaxBackoffSeconds = 300;

    private record ShippingState(string File, int LinesShipped);

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
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var success = false;
            try
            {
                success = await ShipLogsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Log shipping pass failed.");
            }

            if (success)
            {
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
            }

            var pollSeconds = Math.Max(5, _observabilityOptions.Value.LogShipping.PollIntervalSeconds);
            var backoffSeconds = consecutiveFailures > 0
                ? Math.Min(pollSeconds * (1 << Math.Min(consecutiveFailures - 1, 10)), MaxBackoffSeconds)
                : 0;
            var delaySeconds = Math.Max(pollSeconds, backoffSeconds);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task<bool> ShipLogsAsync(CancellationToken cancellationToken)
    {
        var options = _observabilityOptions.Value.LogShipping;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            return true;
        }

        var logsDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return true;
        }

        var latestFile = Directory
            .GetFiles(logsDirectory, "bookwheel-*.jsonl")
            .OrderBy(path => path, StringComparer.Ordinal)
            .LastOrDefault();

        if (latestFile is null)
        {
            return true;
        }

        var stateFile = Path.Combine(logsDirectory, "log-shipping-state.json");
        var state = await LoadStateAsync(stateFile, cancellationToken);

        var offset = string.Equals(state?.File, latestFile, StringComparison.Ordinal)
            ? state!.LinesShipped
            : 0;

        var batchSize = Math.Max(10, options.BatchSize);
        var allLines = await File.ReadAllLinesAsync(latestFile, cancellationToken);
        var lines = allLines
            .Skip(offset)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(batchSize)
            .ToArray();

        if (lines.Length == 0)
        {
            return true;
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
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Log shipping endpoint returned status {StatusCode}", response.StatusCode);
            return false;
        }

        var newOffset = offset + lines.Length;
        await SaveStateAsync(stateFile, new ShippingState(latestFile, newOffset), cancellationToken);
        _logger.LogInformation("Shipped {Count} log entries to centralized sink endpoint {Endpoint}", lines.Length, options.EndpointUrl);
        return true;
    }

    private static async Task<ShippingState?> LoadStateAsync(string stateFile, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(stateFile))
            {
                return null;
            }

            await using var stream = File.OpenRead(stateFile);
            return await JsonSerializer.DeserializeAsync<ShippingState>(stream, _jsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveStateAsync(string stateFile, ShippingState state, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            await File.WriteAllTextAsync(stateFile, json, cancellationToken);
        }
        catch
        {
            // State persistence is best-effort; a failure just means the next pass re-ships from the old offset
        }
    }
}
