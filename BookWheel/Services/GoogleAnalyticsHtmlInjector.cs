namespace BookWheel.Services;

public static class GoogleAnalyticsHtmlInjector
{
    private const string PlaceholderToken = "__GOOGLE_ANALYTICS_ID__";
    private const string ScriptStartMarker = "<!--GOOGLE_ANALYTICS_SCRIPT_START-->";
    private const string ScriptEndMarker = "<!--GOOGLE_ANALYTICS_SCRIPT_END-->";

    public static string Apply(string html, string? googleAnalyticsId)
    {
        if (string.IsNullOrEmpty(googleAnalyticsId))
        {
            html = RemoveGoogleAnalyticsScript(html);
            return html.Replace(PlaceholderToken, string.Empty, StringComparison.Ordinal);
        }

        return html.Replace(PlaceholderToken, googleAnalyticsId, StringComparison.Ordinal);
    }

    private static string RemoveGoogleAnalyticsScript(string html)
    {
        var startIndex = html.IndexOf(ScriptStartMarker, StringComparison.Ordinal);
        var endIndex = html.IndexOf(ScriptEndMarker, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
        {
            return html;
        }

        var removeLength = endIndex + ScriptEndMarker.Length - startIndex;
        return html.Remove(startIndex, removeLength);
    }
}
