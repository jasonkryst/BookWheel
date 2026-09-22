namespace BookWheel.Logging;

public static class LogSanitizer
{
    // Replace control characters (newlines, carriage returns, tabs, etc.) with a
    // safe placeholder so user-controlled values cannot forge log entries or corrupt
    // the JSON log file when embedded as structured-logging arguments.
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (!ContainsControlChar(value))
            return value;

        return string.Create(value.Length, value, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = char.IsControl(src[i]) ? '_' : src[i];
        });
    }

    private static bool ContainsControlChar(string value)
    {
        foreach (var c in value)
            if (char.IsControl(c)) return true;
        return false;
    }
}
