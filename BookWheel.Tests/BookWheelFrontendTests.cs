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
        Assert.Contains("id=\"booksTotalCount\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"selectedBook\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authMessage\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authSubmitBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"themeToggleBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"themeToggleIcon\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"importExportBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"deleteDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"confirmDeleteBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"transferDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"importJsonFile\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"importFileBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"downloadExportBtn\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Contain_Pagination_And_Selection_Behavior()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("const BOOKS_PER_PAGE = 10", script, StringComparison.Ordinal);
        Assert.Contains("booksPagination", script, StringComparison.Ordinal);
        Assert.Contains("booksTotalCount", script, StringComparison.Ordinal);
        Assert.Contains("Page ${currentPage} of ${totalPages}", script, StringComparison.Ordinal);
        Assert.Contains("trimmedTitle", script, StringComparison.Ordinal);
        Assert.Contains("Book title is required.", script, StringComparison.Ordinal);
        Assert.Contains("resetAuthForm", script, StringComparison.Ordinal);
        Assert.Contains("deleteDialog", script, StringComparison.Ordinal);
        Assert.Contains("shuffleArray", script, StringComparison.Ordinal);
        Assert.Contains("shuffleWheel", script, StringComparison.Ordinal);
        Assert.Contains("importExportBtn", script, StringComparison.Ordinal);
        Assert.Contains("setTransferTab", script, StringComparison.Ordinal);
        Assert.Contains("importJsonFile", script, StringComparison.Ordinal);
        Assert.Contains("importFileBtn", script, StringComparison.Ordinal);
        Assert.Contains("downloadExportBtn", script, StringComparison.Ordinal);
        Assert.Contains("downloadExportJsonFile", script, StringComparison.Ordinal);
        Assert.Contains("toLocaleLowerCase", script, StringComparison.Ordinal);
        Assert.Contains("Last selected:", script, StringComparison.Ordinal);
        Assert.Contains("normalizedRotation", script, StringComparison.Ordinal);
        Assert.Contains("rotationDelta", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/status", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/setup", script, StringComparison.Ordinal);
        Assert.Contains("Create your Book Wheel account", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Contain_Theme_Toggle_Behavior()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("THEME_STORAGE_KEY", script, StringComparison.Ordinal);
        Assert.Contains("themeToggleBtn", script, StringComparison.Ordinal);
        Assert.Contains("themeToggleIcon", script, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem", script, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem", script, StringComparison.Ordinal);
        Assert.Contains("data-theme", script, StringComparison.Ordinal);
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

    [Fact]
    public async Task Frontend_Styles_Should_Include_Light_And_Dark_Theme_Variables()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        Assert.Contains(":root", css, StringComparison.Ordinal);
        Assert.Contains("[data-theme=\"light\"]", css, StringComparison.Ordinal);
        Assert.Contains("color-scheme", css, StringComparison.Ordinal);
        Assert.Contains("--bg", css, StringComparison.Ordinal);
    }
}
