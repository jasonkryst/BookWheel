using System.Net;

namespace BookWheel.Tests;

public sealed class BookWheelFrontendTests
{
    [Fact]
    public async Task Home_Page_Should_Render_Main_UI_Structure()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"loginForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"wheelCanvas\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"activeBooks\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"selectedBook\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authMessage\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authSubmitBtn\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Contain_Pagination_And_Selection_Behavior()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("const BOOKS_PER_PAGE = 20", script, StringComparison.Ordinal);
        Assert.Contains("booksPagination", script, StringComparison.Ordinal);
        Assert.Contains("Last selected:", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/status", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/setup", script, StringComparison.Ordinal);
        Assert.Contains("Create your Book Wheel account", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Include_Selected_Book_Emphasis()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        Assert.Contains(".selected-book", css, StringComparison.Ordinal);
        Assert.Contains("font-size", css, StringComparison.Ordinal);
        Assert.Contains("text-shadow", css, StringComparison.Ordinal);
    }
}
