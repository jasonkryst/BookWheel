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

        var stripped = ContainsControlChar(value)
            ? string.Create(value.Length, value, static (span, src) =>
            {
                for (var i = 0; i < src.Length; i++)
                    span[i] = char.IsControl(src[i]) ? '_' : src[i];
            })
            : value;

        // Explicitly strip CR/LF as the final step. These are the actual log-forging
        // vectors, and keeping the newline removal as the last operation lets static
        // analysis (CodeQL cs/log-forging) recognize this method as a sanitizer — it
        // cannot see the control-char replacement above as a barrier. Functionally a
        // no-op here, since control chars (incl. CR/LF) are already replaced above.
        return stripped.Replace("\r", "_").Replace("\n", "_");
    }

    // Mask an email for logging: keep the first local-part character and the domain
    // (e.g. "a***@example.com"), so operational logs show the target domain without
    // storing the full address (PII). Output is control-char sanitized.
    public static string MaskEmail(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var at = value.IndexOf('@');
        if (at <= 0 || at == value.Length - 1)
            return "***";

        var firstChar = value[0];
        var domain = value[(at + 1)..];
        return Sanitize($"{firstChar}***@{domain}");
    }

    private static bool ContainsControlChar(string value)
    {
        foreach (var c in value)
            if (char.IsControl(c)) return true;
        return false;
    }
}
