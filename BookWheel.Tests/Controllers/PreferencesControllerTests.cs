using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookWheel.Tests;

namespace BookWheel.Tests.Controllers;

public sealed class PreferencesControllerTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
{
    private readonly BookWheelWebAppFactory _factory;

    public PreferencesControllerTests(BookWheelWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_Returns_Unauthorized_When_Not_Logged_In()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Default_Preferences_For_A_Fresh_User()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.GetAsync("/api/preferences");
        response.EnsureSuccessStatusCode();
        var preferences = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, preferences.GetProperty("theme").ValueKind);
        Assert.False(preferences.GetProperty("analyticsConsentOptedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preferences.GetProperty("preferredBookInfoProviderId").ValueKind);
    }

    [Fact]
    public async Task Put_Persists_Preferences_And_Get_Reflects_Them()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var putResponse = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "light",
            analyticsConsentOptedOut = true,
            preferredBookInfoProviderId = 2
        });
        putResponse.EnsureSuccessStatusCode();

        var getResponse = await client.GetAsync("/api/preferences");
        getResponse.EnsureSuccessStatusCode();
        var preferences = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("light", preferences.GetProperty("theme").GetString());
        Assert.True(preferences.GetProperty("analyticsConsentOptedOut").GetBoolean());
        Assert.Equal(2, preferences.GetProperty("preferredBookInfoProviderId").GetInt32());
    }

    [Fact]
    public async Task Put_Rejects_Unknown_Provider_Id()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = (string?)null,
            analyticsConsentOptedOut = false,
            preferredBookInfoProviderId = 99
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Rejects_Unknown_Theme()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "not-a-real-theme",
            analyticsConsentOptedOut = false,
            preferredBookInfoProviderId = (int?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/setup", new { username = $"user-{Guid.NewGuid():N}", password = "correct horse battery staple" });
        response.EnsureSuccessStatusCode();
    }
}
