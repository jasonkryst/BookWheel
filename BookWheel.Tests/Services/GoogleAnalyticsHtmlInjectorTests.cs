using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class GoogleAnalyticsHtmlInjectorTests
{
    private const string SampleHtml =
        """
        <html>
        <body>
          <script>
            window.__BOOKWHEEL_GA_ID__ = "__GOOGLE_ANALYTICS_ID__";
          </script>
          <!--GOOGLE_ANALYTICS_SCRIPT_START-->
          <script async src="https://www.googletagmanager.com/gtag/js?id=__GOOGLE_ANALYTICS_ID__"></script>
          <script>
            gtag('config', '__GOOGLE_ANALYTICS_ID__');
          </script>
          <!--GOOGLE_ANALYTICS_SCRIPT_END-->
        </body>
        </html>
        """;

    [Fact]
    public void Apply_Substitutes_Id_And_Keeps_Script_When_Id_Is_Set()
    {
        var result = GoogleAnalyticsHtmlInjector.Apply(SampleHtml, "G-ABC123");

        Assert.Contains("window.__BOOKWHEEL_GA_ID__ = \"G-ABC123\"", result, StringComparison.Ordinal);
        Assert.Contains("googletagmanager.com/gtag/js?id=G-ABC123", result, StringComparison.Ordinal);
        Assert.Contains("gtag('config', 'G-ABC123')", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Apply_Removes_Google_Tag_Script_When_Id_Is_Empty(string? id)
    {
        var result = GoogleAnalyticsHtmlInjector.Apply(SampleHtml, id);

        Assert.DoesNotContain("googletagmanager.com", result, StringComparison.Ordinal);
        Assert.DoesNotContain("gtag(", result, StringComparison.Ordinal);
        Assert.DoesNotContain("GOOGLE_ANALYTICS_SCRIPT", result, StringComparison.Ordinal);

        // The consent-checkbox JS logic in app.js reads window.__BOOKWHEEL_GA_ID__
        // regardless of whether analytics is enabled, so that assignment must survive.
        Assert.Contains("window.__BOOKWHEEL_GA_ID__ = \"\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Leaves_Html_Unchanged_When_Markers_Are_Missing()
    {
        var html = "<html><body>no markers here</body></html>";

        var result = GoogleAnalyticsHtmlInjector.Apply(html, null);

        Assert.Equal(html, result);
    }
}
