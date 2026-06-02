using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BookWheel.Logging;

public sealed class JsonFileLoggerProvider : ILoggerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _logDirectory;
    private readonly JsonFileLoggerOptions _options;
    private readonly ConcurrentDictionary<string, JsonFileLogger> _loggers = new();

    public JsonFileLoggerProvider(string logDirectory, JsonFileLoggerOptions? options = null)
    {
        _logDirectory = logDirectory;
        _options = options ?? new JsonFileLoggerOptions();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, static (name, state) => new JsonFileLogger(name, state._logDirectory, state._options), this);
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class JsonFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logDirectory;
        private readonly JsonFileLoggerOptions _options;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private DateOnly? _lastRetentionSweepDay;

        public JsonFileLogger(string categoryName, string logDirectory, JsonFileLoggerOptions options)
        {
            _categoryName = categoryName;
            _logDirectory = logDirectory;
            _options = options;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var record = new JsonLogEntry(
                TimestampUtc: DateTimeOffset.UtcNow,
                Category: _categoryName,
                Level: logLevel.ToString(),
                EventId: eventId.Id,
                Message: formatter(state, exception),
                Exception: exception?.ToString(),
                Properties: CaptureState(state));

            WriteEntry(record).GetAwaiter().GetResult();
        }

        private async Task WriteEntry(JsonLogEntry record)
        {
            await _writeGate.WaitAsync();
            try
            {
                Directory.CreateDirectory(_logDirectory);
                SweepRetentionUnsafe(record.TimestampUtc.UtcDateTime);
                var logFilePath = ResolveActiveLogFilePath(record.TimestampUtc.UtcDateTime);
                var json = JsonSerializer.Serialize(record, JsonOptions);
                await File.AppendAllTextAsync(logFilePath, json + Environment.NewLine);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private string ResolveActiveLogFilePath(DateTime utcNow)
        {
            var baseName = $"bookwheel-{utcNow:yyyy-MM-dd}";
            var index = 0;

            while (true)
            {
                var suffix = index == 0 ? string.Empty : $"-{index}";
                var path = Path.Combine(_logDirectory, $"{baseName}{suffix}.jsonl");
                if (!File.Exists(path))
                {
                    return path;
                }

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length < _options.MaxFileSizeBytes)
                {
                    return path;
                }

                index += 1;
            }
        }

        private void SweepRetentionUnsafe(DateTime utcNow)
        {
            var today = DateOnly.FromDateTime(utcNow);
            if (_lastRetentionSweepDay == today)
            {
                return;
            }

            _lastRetentionSweepDay = today;
            var retentionDays = Math.Max(1, _options.RetentionDays);
            var cutoffUtc = utcNow.Date.AddDays(-retentionDays);

            foreach (var filePath in Directory.GetFiles(_logDirectory, "bookwheel-*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.LastWriteTimeUtc < cutoffUtc)
                {
                    fileInfo.Delete();
                }
            }
        }

        private static Dictionary<string, object?> CaptureState<TState>(TState state)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var item in values)
                {
                    if (item.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    properties[item.Key] = NormalizeValue(item.Value);
                }
            }

            return properties;
        }

        private static object? NormalizeValue(object? value)
        {
            return value switch
            {
                null => null,
                string s => s,
                bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
                Guid or DateTime or DateTimeOffset or TimeSpan => value,
                _ => value.ToString()
            };
        }

        private sealed record JsonLogEntry(
            DateTimeOffset TimestampUtc,
            string Category,
            string Level,
            int EventId,
            string Message,
            string? Exception,
            Dictionary<string, object?> Properties);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
