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
    private readonly ConcurrentDictionary<string, JsonFileLogger> _loggers = new();

    public JsonFileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, static (name, state) => new JsonFileLogger(name, state._logDirectory), this);
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class JsonFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logDirectory;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public JsonFileLogger(string categoryName, string logDirectory)
        {
            _categoryName = categoryName;
            _logDirectory = logDirectory;
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
                var logFilePath = Path.Combine(_logDirectory, $"bookwheel-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
                var json = JsonSerializer.Serialize(record, JsonOptions);
                await File.AppendAllTextAsync(logFilePath, json + Environment.NewLine);
            }
            finally
            {
                _writeGate.Release();
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
