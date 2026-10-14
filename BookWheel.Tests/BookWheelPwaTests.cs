using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BookWheel.Tests;

public sealed class BookWheelPwaTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
{
    private readonly BookWheelWebAppFactory _factory;

    public BookWheelPwaTests(BookWheelWebAppFactory factory)
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
    public async Task Manifest_Should_Be_Served_With_Correct_Content_Type_And_Fields()
    {
        var factory = _factory;
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
    public async Task Manifest_Should_Declare_Add_Book_And_Scan_Shortcuts_Routed_Via_View_Param()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var shortcuts = document.RootElement.GetProperty("shortcuts").EnumerateArray().ToList();

        // Positive: the three shortcuts named in GH #243 (Spin, Add book, Scan)
        // exist and route through the existing ?view= handling, not a dedicated
        // standalone page.
        Assert.Contains(shortcuts, s => s.GetProperty("short_name").GetString() == "Spin Wheel" && s.GetProperty("url").GetString() == "/?view=wheel");
        Assert.Contains(shortcuts, s => s.GetProperty("short_name").GetString() == "Add book" && s.GetProperty("url").GetString() == "/?view=add-book");
        Assert.Contains(shortcuts, s => s.GetProperty("short_name").GetString() == "Scan" && s.GetProperty("url").GetString() == "/?view=scan");

        // Negative: every shortcut must resolve to a same-origin app route, never
        // an absolute external URL (the manifest.action validator below also
        // depends on this for share_target).
        Assert.All(shortcuts, s => Assert.StartsWith("/?view=", s.GetProperty("url").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifest_Should_Declare_A_Get_Share_Target_Into_Book_Search()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.TryGetProperty("share_target", out var shareTarget));
        Assert.Equal("/?view=search", shareTarget.GetProperty("action").GetString());
        Assert.Equal("GET", shareTarget.GetProperty("method").GetString());

        var shareParams = shareTarget.GetProperty("params");
        Assert.Equal("title", shareParams.GetProperty("title").GetString());
        Assert.Equal("text", shareParams.GetProperty("text").GetString());
        Assert.Equal("url", shareParams.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Manifest_Icons_Should_All_Resolve_To_Real_Files()
    {
        var factory = _factory;
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
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("rel=\"manifest\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"theme-color\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"apple-touch-icon\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_Fallback_Page_Should_Be_Served()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/offline.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Book Wheel is offline", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_Worker_Should_Be_Served_With_Current_Version_And_Lifecycle_Handlers()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var versionResponse = await client.GetAsync("/api/version");
        using var versionDocument = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync());
        var version = versionDocument.RootElement.GetProperty("version").GetString();
        Assert.NotNull(version);

        var swResponse = await client.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, swResponse.StatusCode);
        Assert.Contains("javascript", swResponse.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);

        var script = await swResponse.Content.ReadAsStringAsync();
        Assert.Contains("CACHE_NAME", script, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(version!), script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('install'", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('activate'", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('fetch'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_Worker_Should_Never_Intercept_Api_Requests()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sw.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: an explicit bypass guard for /api/ exists in the fetch handler.
        Assert.Contains("pathname.startsWith('/api/')", script, StringComparison.Ordinal);

        // Negative: within the fetch handler, the bypass must come before any cache
        // read/write for the request, otherwise API responses could still be served
        // from or written to the cache.
        var fetchHandlerIndex = script.IndexOf("addEventListener('fetch'", StringComparison.Ordinal);
        Assert.True(fetchHandlerIndex >= 0);

        var apiGuardIndex = script.IndexOf("pathname.startsWith('/api/')", fetchHandlerIndex, StringComparison.Ordinal);
        var firstCacheOpInFetchHandler = script.IndexOf("caches.match", fetchHandlerIndex, StringComparison.Ordinal);
        Assert.True(apiGuardIndex >= 0 && firstCacheOpInFetchHandler >= 0 && apiGuardIndex < firstCacheOpInFetchHandler);
    }

    [Fact]
    public async Task Service_Worker_Shell_Assets_Should_Use_Versioned_Urls()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var versionResponse = await client.GetAsync("/api/version");
        using var versionDocument = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync());
        var version = versionDocument.RootElement.GetProperty("version").GetString();
        Assert.NotNull(version);

        var response = await client.GetAsync("/sw.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: CACHE_VERSION is set to the real app version at serve time
        // (the server substitutes __ASSET_VERSION__), and the precache list
        // references each asset as a JS template literal using that constant —
        // so the runtime URL matches what index.html actually requests (GH #150).
        Assert.Contains($"const CACHE_VERSION = '{Uri.EscapeDataString(version!)}'", script, StringComparison.Ordinal);
        Assert.Contains("`/css/site.css?v=${CACHE_VERSION}`", script, StringComparison.Ordinal);
        Assert.Contains("`/js/app.js?v=${CACHE_VERSION}`", script, StringComparison.Ordinal);
        Assert.Contains("`/js/i18n.js?v=${CACHE_VERSION}`", script, StringComparison.Ordinal);

        // Negative: bare (unversioned) paths for CSS/JS would be cached as separate
        // entries from the versioned ones, silently defeating the cache-first strategy.
        Assert.DoesNotContain("'/css/site.css'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'/js/app.js'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'/js/i18n.js'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Register_Service_Worker_With_Feature_Detection()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: registration happens, guarded by a feature check.
        Assert.Contains("'serviceWorker' in navigator", script, StringComparison.Ordinal);
        Assert.Contains("navigator.serviceWorker.register('/sw.js')", script, StringComparison.Ordinal);

        // Negative: the feature check must come before the registration call,
        // not after — otherwise it would throw in browsers without SW support.
        var guardIndex = script.IndexOf("'serviceWorker' in navigator", StringComparison.Ordinal);
        var registerIndex = script.IndexOf("navigator.serviceWorker.register('/sw.js')", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && registerIndex >= 0 && guardIndex < registerIndex);
    }

    [Fact]
    public async Task Service_Worker_Should_Wait_For_SkipWaiting_Message_Instead_Of_Taking_Over_Immediately()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sw.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: the new worker waits in the 'waiting' state until told to
        // activate, and listens for that instruction from the page.
        Assert.Contains("addEventListener('message'", script, StringComparison.Ordinal);
        Assert.Contains("event.data === 'SKIP_WAITING'", script, StringComparison.Ordinal);
        Assert.Contains("self.skipWaiting()", script, StringComparison.Ordinal);

        // Negative: install must not call skipWaiting() unconditionally anymore —
        // that's what silently swapped the worker under an open tab (GH #233).
        // It should only ever appear inside the message handler's body.
        var skipWaitingCount = Regex.Matches(script, Regex.Escape("self.skipWaiting()")).Count;
        Assert.Equal(1, skipWaitingCount);
        var installHandlerIndex = script.IndexOf("addEventListener('install'", StringComparison.Ordinal);
        var activateHandlerIndex = script.IndexOf("addEventListener('activate'", StringComparison.Ordinal);
        Assert.True(installHandlerIndex >= 0 && activateHandlerIndex >= 0);
        var installHandlerBody = script.Substring(installHandlerIndex, activateHandlerIndex - installHandlerIndex);
        Assert.DoesNotContain("skipWaiting", installHandlerBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Show_An_Update_Toast_And_Reload_After_SkipWaiting()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: a waiting or newly-installed worker (only once this tab is
        // already controlled, i.e. an update rather than the first install)
        // triggers a toast whose action posts SKIP_WAITING to that worker; the
        // page reloads itself once the new worker actually takes control.
        Assert.Contains("registration.waiting && navigator.serviceWorker.controller", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('updatefound'", script, StringComparison.Ordinal);
        Assert.Contains("newWorker.state === 'installed' && navigator.serviceWorker.controller", script, StringComparison.Ordinal);
        Assert.Contains("worker.postMessage('SKIP_WAITING')", script, StringComparison.Ordinal);
        Assert.Contains("t('common.updateAvailableToast')", script, StringComparison.Ordinal);
        Assert.Contains("actionLabel: t('common.reloadBtn')", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('controllerchange'", script, StringComparison.Ordinal);
        Assert.Contains("window.location.reload()", script, StringComparison.Ordinal);

        // Negative: the controllerchange handler must guard against firing the
        // reload more than once (it can fire multiple times in some browsers).
        Assert.Contains("refreshingAfterSwUpdate", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I18n_Catalog_Should_Include_Update_Available_Toast_Strings_For_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("updateAvailableToast: 'Update available.'", script, StringComparison.Ordinal);
        Assert.Contains("reloadBtn: 'Reload'", script, StringComparison.Ordinal);
        Assert.Contains("updateAvailableToast: 'Actualización disponible.'", script, StringComparison.Ordinal);
        Assert.Contains("reloadBtn: 'Recargar'", script, StringComparison.Ordinal);
        Assert.Contains("updateAvailableToast: 'Dostępna aktualizacja.'", script, StringComparison.Ordinal);
        Assert.Contains("reloadBtn: 'Odśwież'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Notify_User_On_Connectivity_Changes()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("addEventListener('offline'", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('online'", script, StringComparison.Ordinal);
        Assert.Contains("common.offlineToast", script, StringComparison.Ordinal);
        Assert.Contains("common.onlineToast", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I18n_Catalog_Should_Include_Connectivity_Toast_Strings_For_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("You are offline. Some features will not work until you reconnect.", script, StringComparison.Ordinal);
        Assert.Contains("Back online.", script, StringComparison.Ordinal);
        Assert.Contains("Estás sin conexión. Algunas funciones no estarán disponibles hasta que te reconectes.", script, StringComparison.Ordinal);
        Assert.Contains("De nuevo en línea.", script, StringComparison.Ordinal);
        Assert.Contains("Jesteś offline. Niektóre funkcje będą niedostępne, dopóki nie połączysz się ponownie.", script, StringComparison.Ordinal);
        Assert.Contains("Znowu online.", script, StringComparison.Ordinal);
    }
}
