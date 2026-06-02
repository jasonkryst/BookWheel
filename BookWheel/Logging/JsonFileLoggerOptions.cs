namespace BookWheel.Logging;

public sealed class JsonFileLoggerOptions
{
    public int RetentionDays { get; set; } = 14;
    public long MaxFileSizeBytes { get; set; } = 5L * 1024L * 1024L;
}
