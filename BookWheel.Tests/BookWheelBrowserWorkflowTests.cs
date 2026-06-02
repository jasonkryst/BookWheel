using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookWheel.Tests;

public sealed class BookWheelBrowserWorkflowTests
{
    [Fact]
    public async Task Login_Theme_And_Book_Workflow_Is_End_To_End_Reachable()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var homeResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        var html = await homeResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"themeToggleBtn\"", html, StringComparison.Ordinal);

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "ui-admin",
            password = "ui-password"
        });
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var addBookResponse = await client.PostAsJsonAsync("/api/books", new { title = "Workflow Book" });
        Assert.Equal(HttpStatusCode.OK, addBookResponse.StatusCode);

        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        using var spinDocument = await ParseAsync(spinResponse);
        Assert.Equal("Workflow Book", spinDocument.RootElement.GetProperty("selected").GetProperty("title").GetString());

        var scriptResponse = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        var script = await scriptResponse.Content.ReadAsStringAsync();
        Assert.Contains("toggleTheme", script, StringComparison.Ordinal);
        Assert.Contains("showToast", script, StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
