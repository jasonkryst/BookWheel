using System.Net;

namespace BookWheel.Tests;

public sealed class BookWheelFrontendTests
{
    [Fact]
    public async Task Frontend_Should_Serve_I18n_Script_With_All_Locale_Catalogs()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        // Positive: the catalog structure, all three locales, and representative
        // translated strings for each supported language are present.
        Assert.Contains("SUPPORTED_LOCALES = ['en', 'es', 'pl']", script, StringComparison.Ordinal);
        Assert.Contains("BookWheelI18n", script, StringComparison.Ordinal);
        Assert.Contains("Iniciar sesión", script, StringComparison.Ordinal);
        Assert.Contains("Zaloguj się", script, StringComparison.Ordinal);
        Assert.Contains("Book title is required.", script, StringComparison.Ordinal);
        Assert.Contains("Create your Book Wheel account", script, StringComparison.Ordinal);
        Assert.Contains("Last selected: {title}", script, StringComparison.Ordinal);
        Assert.Contains("Version: {version}", script, StringComparison.Ordinal);
        Assert.Contains("Page {current} of {total}", script, StringComparison.Ordinal);
        Assert.Contains("Generate reset link", script, StringComparison.Ordinal);

        // Negative: locale data lives only in this catalog file, not duplicated
        // as a second hardcoded English-only copy anywhere in the same file.
        Assert.DoesNotContain("const LEGACY_EN_ONLY_STRINGS", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_I18n_Attributes_And_Settings_Button()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Positive: settings entry point exists and static text is externalized via data-i18n.
        Assert.Contains("id=\"settingsBtnLoggedOut\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsBtnLoggedIn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"langSelect\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"auth.loginSubmit\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"books.heading\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"js/i18n.js", html, StringComparison.Ordinal);

        // Negative: the i18n script must load before app.js, since app.js calls
        // BookWheelI18n at init time.
        var i18nIndex = html.IndexOf("src=\"js/i18n.js", StringComparison.Ordinal);
        var appJsIndex = html.IndexOf("src=\"js/app.js", StringComparison.Ordinal);
        Assert.True(i18nIndex >= 0 && appJsIndex >= 0 && i18nIndex < appJsIndex);
    }

    [Fact]
    public async Task Home_Page_Language_Select_Should_List_Supported_Locales()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        var selectStart = html.IndexOf("id=\"langSelect\"", StringComparison.Ordinal);
        var selectSnippet = html.Substring(selectStart, Math.Min(400, html.Length - selectStart));

        // Positive: the dropdown lists exactly the three supported locales.
        Assert.Contains("<option value=\"en\">English</option>", selectSnippet, StringComparison.Ordinal);
        Assert.Contains("<option value=\"es\">Español</option>", selectSnippet, StringComparison.Ordinal);
        Assert.Contains("<option value=\"pl\">Polski</option>", selectSnippet, StringComparison.Ordinal);

        // Negative: no extra placeholder/empty option was left in the dropdown.
        Assert.DoesNotContain("<option value=\"\">", selectSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Re_Render_Dynamic_Content_On_Locale_Change()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: dynamic UI re-renders (not just a page reload) when the
        // language changes, so an in-progress session doesn't show mixed languages.
        Assert.Contains("bookwheel:locale-changed", script, StringComparison.Ordinal);
        Assert.Contains("syncLangSelect", script, StringComparison.Ordinal);

        // Positive: the login/setup screen's dynamically-set title, subtitle, and
        // submit-button text (driven by setAuthMode, not a static data-i18n
        // attribute alone) get refreshed too. Without this, switching languages
        // while still on the login screen left the title reset to the generic
        // "login" default via the blanket data-i18n re-apply, while the subtitle
        // — which has no data-i18n attribute at all — stayed in the old language,
        // producing a screen with three lines in two different languages.
        Assert.Contains("setAuthMode(authMode)", script, StringComparison.Ordinal);

        // Negative: the language dropdown must drive locale changes through the
        // single setLocale() entry point, not by writing localStorage directly —
        // otherwise static text and the Accept-Language header could fall out of sync.
        Assert.DoesNotContain("langSelect.addEventListener('change', () => localStorage.setItem", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Render_Main_UI_Structure()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"loginForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetPasswordForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetPassword\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetPasswordConfirm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetPasswordSubmitBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"wheelCanvas\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookForm\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"bookTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"activeBooks\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"booksTotalCount\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"selectedBook\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authMessage\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authSubmitBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"userGreeting\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"themeToggleBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"themeToggleIcon\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"importExportBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"userManagementBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"userManagementDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserUsername\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserIsAdmin\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"userList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"closeUserManagementBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"deleteUserDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"deleteUserConfirmMessage\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"confirmDeleteUserBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cancelDeleteUserBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetLinkDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetLinkValue\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"copyResetLinkBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"closeResetLinkBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"deleteDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"confirmDeleteBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"transferDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"importJsonFile\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"importFileBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"downloadExportBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"appVersion\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"toastRegion\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"skip-link\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"wheelSummary\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"wheelBooksSrList\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"tab\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"tabpanel\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", html, StringComparison.Ordinal);
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
        Assert.Contains("trimmedTitle", script, StringComparison.Ordinal);
        Assert.Contains("resetAuthForm", script, StringComparison.Ordinal);
        Assert.Contains("deleteDialog", script, StringComparison.Ordinal);
        Assert.Contains("shuffleArray", script, StringComparison.Ordinal);
        Assert.Contains("shuffleWheel", script, StringComparison.Ordinal);
        Assert.Contains("importExportBtn", script, StringComparison.Ordinal);
        Assert.Contains("userManagementBtn", script, StringComparison.Ordinal);
        Assert.Contains("userManagementDialog", script, StringComparison.Ordinal);
        Assert.Contains("createUserForm", script, StringComparison.Ordinal);
        Assert.Contains("deleteUserDialog", script, StringComparison.Ordinal);
        Assert.Contains("confirmDeleteUser", script, StringComparison.Ordinal);
        Assert.Contains("/api/users/${pendingDeleteUser.userId}", script, StringComparison.Ordinal);
        Assert.Contains("/password-reset-link", script, StringComparison.Ordinal);
        Assert.Contains("resetLinkDialog", script, StringComparison.Ordinal);
        Assert.Contains("copyResetLinkBtn", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/password-reset/complete", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/password-reset/validate", script, StringComparison.Ordinal);
        Assert.Contains("resetToken", script, StringComparison.Ordinal);
        Assert.Contains("applyCurrentUser", script, StringComparison.Ordinal);
        Assert.Contains("showToast", script, StringComparison.Ordinal);
        Assert.Contains("forcePasswordReset", script, StringComparison.Ordinal);
        Assert.Contains("isDisabled", script, StringComparison.Ordinal);
        Assert.Contains("isLocked", script, StringComparison.Ordinal);
        Assert.Contains("/api/users", script, StringComparison.Ordinal);
        Assert.Contains("isAdmin", script, StringComparison.Ordinal);
        Assert.Contains("setTransferTab", script, StringComparison.Ordinal);
        Assert.Contains("handleTransferTabKeydown", script, StringComparison.Ordinal);
        Assert.Contains("moveTransferTabFocus", script, StringComparison.Ordinal);
        Assert.Contains("openDialog", script, StringComparison.Ordinal);
        Assert.Contains("closeDialog", script, StringComparison.Ordinal);
        Assert.Contains("renderWheelAccessibilitySummary", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", script, StringComparison.Ordinal);
        Assert.Contains("shouldHandlePaginationHotkey", script, StringComparison.Ordinal);
        Assert.Contains("importJsonFile", script, StringComparison.Ordinal);
        Assert.Contains("importFileBtn", script, StringComparison.Ordinal);
        Assert.Contains("downloadExportBtn", script, StringComparison.Ordinal);
        Assert.Contains("downloadExportJsonFile", script, StringComparison.Ordinal);
        Assert.Contains("document.createElement('details')", script, StringComparison.Ordinal);
        Assert.Contains("user-row-controls", script, StringComparison.Ordinal);
        Assert.Contains("/api/version", script, StringComparison.Ordinal);
        Assert.Contains("loadAppVersion", script, StringComparison.Ordinal);
        Assert.Contains("toLocaleLowerCase", script, StringComparison.Ordinal);
        Assert.Contains("normalizedRotation", script, StringComparison.Ordinal);
        Assert.Contains("rotationDelta", script, StringComparison.Ordinal);
        Assert.DoesNotContain("const wheelBooks = [...activeBooks];", script, StringComparison.Ordinal);
        Assert.Contains("const selectedIndex = wheelBooks.findIndex", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/status", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/setup", script, StringComparison.Ordinal);
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
    public async Task Frontend_Styles_Should_Keep_Mobile_Wheel_Inside_Its_Card()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        // Positive: the wheel uses the padded card's available width rather
        // than the viewport width, so it remains within the mobile layout.
        Assert.Contains("@media (max-width: 900px)", css, StringComparison.Ordinal);
        Assert.Contains("width: min(100%, 380px);", css, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 1;", css, StringComparison.Ordinal);
        Assert.Contains("padding: 16px;", css, StringComparison.Ordinal);

        // Negative: viewport-relative sizing ignored the card padding and
        // caused horizontal scrolling on narrow displays.
        Assert.DoesNotContain("width: min(92vw, 380px);", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Keep_Import_Export_Modal_Responsive()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n");

        // Positive: the dialog stays in the visible viewport and its form
        // scrolls independently when import content is taller than the screen.
        Assert.Contains(".transfer-modal {\n  width: min(900px, calc(100% - 24px));\n  max-height: calc(100vh - 24px);", css, StringComparison.Ordinal);
        Assert.Contains(".transfer-modal form {\n  max-height: calc(100vh - 24px);", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", css, StringComparison.Ordinal);
        Assert.Contains("width: calc(100% - 20px);", css, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 160px;", css, StringComparison.Ordinal);
        Assert.Contains(".transfer-modal input[type=\"file\"] {\n  width: 100%;\n  max-width: 100%;\n  min-width: 0;", css, StringComparison.Ordinal);

        // Negative: fixed-width dialogs overflow narrow mobile and tablet viewports.
        Assert.DoesNotContain(".transfer-modal {\n  width: 900px;", css, StringComparison.Ordinal);
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
        Assert.Contains(".tab-panel.hidden", css, StringComparison.Ordinal);
        Assert.Contains(".sr-only", css, StringComparison.Ordinal);
        Assert.Contains(".skip-link", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Include_High_Contrast_Theme_Variables()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        // Positive: the high-contrast theme block exists and defines every
        // color token the dark/light themes define, plus the wheel slice
        // palette that drawWheel() reads via getComputedStyle.
        Assert.Contains("[data-theme=\"high-contrast\"]", css, StringComparison.Ordinal);
        Assert.Contains("--bg: #000000", css, StringComparison.Ordinal);
        Assert.Contains("--text: #ffffff", css, StringComparison.Ordinal);
        Assert.Contains("--wheel-slice-1", css, StringComparison.Ordinal);
        Assert.Contains("--wheel-slice-6", css, StringComparison.Ordinal);
        Assert.Contains("[data-theme=\"high-contrast\"] button:focus-visible", css, StringComparison.Ordinal);

        // Negative: the shared card/dialog surfaces must not keep relying on
        // the low-opacity color-mix blends used by dark/light — those wash
        // out against a pure black background.
        Assert.DoesNotContain("[data-theme=\"high-contrast\"] .card { background: color-mix", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Contain_High_Contrast_Theme_Cycle()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        // Positive: theme cycling and system-preference detection include
        // the new high-contrast state.
        Assert.Contains("HIGH_CONTRAST_THEME = 'high-contrast'", script, StringComparison.Ordinal);
        Assert.Contains("THEME_CYCLE = [DARK_THEME, LIGHT_THEME, HIGH_CONTRAST_THEME]", script, StringComparison.Ordinal);
        Assert.Contains("prefers-contrast: more", script, StringComparison.Ordinal);
        Assert.Contains("--wheel-slice-", script, StringComparison.Ordinal);

        // Negative: the wheel renderer must no longer hardcode its slice
        // palette, since a hardcoded array can't be re-themed for
        // high-contrast mode.
        Assert.DoesNotContain("['#38bdf8', '#60a5fa', '#818cf8', '#f472b6', '#34d399', '#fbbf24']", script, StringComparison.Ordinal);
    }
}
