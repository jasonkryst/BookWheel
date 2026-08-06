using System.Net;
using System.Text.Json;

namespace BookWheel.Tests;

public sealed class BookWheelPwaTests
{
    [Fact]
    public async Task Manifest_Should_Be_Served_With_Correct_Content_Type_And_Fields()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/manifest+json", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Book Wheel", root.GetProperty("name").GetString());
        Assert.Equal("Book Wheel", root.GetProperty("short_name").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.True(icons.Count >= 3);
        Assert.Contains(icons, icon => icon.TryGetProperty("purpose", out var purpose) && purpose.GetString() == "maskable");
    }

    [Fact]
    public async Task Manifest_Icons_Should_All_Resolve_To_Real_Files()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var manifestResponse = await client.GetAsync("/manifest.webmanifest");
        using var document = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync());

        foreach (var icon in document.RootElement.GetProperty("icons").EnumerateArray())
        {
            var src = icon.GetProperty("src").GetString();
            Assert.NotNull(src);

            var iconResponse = await client.GetAsync("/" + src);
            Assert.Equal(HttpStatusCode.OK, iconResponse.StatusCode);
            Assert.Equal("image/png", iconResponse.Content.Headers.ContentType?.MediaType);

            var bytes = await iconResponse.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 0, $"{src} was empty");
        }
    }

    [Fact]
    public async Task Home_Page_Should_Reference_Manifest_Theme_Color_And_Icons()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("rel=\"manifest\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"theme-color\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"apple-touch-icon\"", html, StringComparison.Ordinal);
    }
}
