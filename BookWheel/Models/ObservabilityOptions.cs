namespace BookWheel.Models;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool EnableRequestCorrelationLogging { get; set; } = true;
    public bool EnableStartupDiagnostics { get; set; } = true;
    public LogShippingOptions LogShipping { get; set; } = new();

    public sealed class LogShippingOptions
    {
        public bool Enabled { get; set; }
        public string EndpointUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public int BatchSize { get; set; } = 100;
        public int PollIntervalSeconds { get; set; } = 30;
    }
}
