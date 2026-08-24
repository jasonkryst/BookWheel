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
        Assert.Contains("id=\"settingsTabRow\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsManageUsersTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsImportExportTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsPreferencesTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsManageUsersPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsImportExportPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsPreferencesPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"closeSettingsBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserUsername\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserIsAdmin\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"userList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"userStatusFilter\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"clearUserFiltersBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"manageUsersTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserTabBtn\"", html, StringComparison.Ordinal);
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
    public async Task Home_Page_Should_Include_Isbn_Lookup_Controls_For_Add_And_Edit()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Positive: the add-book row and edit dialog both expose an ISBN input,
        // a Lookup action, and a metadata preview (cover + author).
        Assert.Contains("id=\"bookIsbn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookLookupBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookAddPreview\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookAddCoverImg\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookAddAuthorText\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"editBookIsbn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"editLookupBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"editBookPreview\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"books.isbnLabel\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"books.lookupBtn\"", html, StringComparison.Ordinal);

        // Negative: the ISBN field is optional metadata, unlike the title field,
        // so it must not carry a required attribute.
        var isbnInputStart = html.IndexOf("id=\"bookIsbn\"", StringComparison.Ordinal);
        var isbnInputSnippet = html.Substring(isbnInputStart, Math.Min(120, html.Length - isbnInputStart));
        Assert.DoesNotContain("required", isbnInputSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Wire_Up_Isbn_Lookup_And_Render_Metadata()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: both the add-book row and the edit dialog wire their Lookup
        // button to the shared metadata lookup call against the API endpoint.
        Assert.Contains("bookLookupBtn.addEventListener('click'", script, StringComparison.Ordinal);
        Assert.Contains("editLookupBtn.addEventListener('click'", script, StringComparison.Ordinal);
        Assert.Contains("/api/books/lookup?", script, StringComparison.Ordinal);
        Assert.Contains("renderMetadataPreview", script, StringComparison.Ordinal);

        // Positive: cover/author render in the active book list when present.
        Assert.Contains("book-cover-thumb", script, StringComparison.Ordinal);
        Assert.Contains("book-row-author", script, StringComparison.Ordinal);

        // Negative: the add-book submit must not silently drop a fetched ISBN or
        // author/cover — otherwise a successful Lookup would have no effect.
        Assert.Contains("isbn: bookIsbn.value.trim()", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_A_Lookup_Picker_For_Ambiguous_Title_Matches()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"lookupPickerDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"lookupPickerList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cancelLookupPickerBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"books.pickerTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"books.pickerHint\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Auto_Fill_On_A_Single_Match_And_Open_A_Picker_When_Ambiguous()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: a title lookup reads the `results` array from the API and
        // branches on how many candidates came back.
        Assert.Contains("data.results", script, StringComparison.Ordinal);
        Assert.Contains("results.length === 1", script, StringComparison.Ordinal);
        Assert.Contains("openLookupPicker(results, target)", script, StringComparison.Ordinal);
        Assert.Contains("cancelLookupPickerBtn.addEventListener('click'", script, StringComparison.Ordinal);

        // Negative: an ISBN lookup is an exact-key match and must go straight to
        // applyLookupResult — it must never be routed through the ambiguous-title
        // picker path, since an ISBN can't have multiple candidates.
        var isbnBranchStart = script.IndexOf("if (isbnValue) {", StringComparison.Ordinal);
        Assert.True(isbnBranchStart >= 0, "Expected an isbnValue branch in the lookup logic.");
        var isbnBranchSnippet = script.Substring(isbnBranchStart, Math.Min(200, script.Length - isbnBranchStart));
        Assert.Contains("await requestJson(`/api/books/lookup?isbn=", isbnBranchSnippet, StringComparison.Ordinal);
        Assert.DoesNotContain("openLookupPicker", isbnBranchSnippet, StringComparison.Ordinal);
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
        Assert.Contains("settingsDialog", script, StringComparison.Ordinal);
        Assert.Contains("settingsTabRow", script, StringComparison.Ordinal);
        Assert.Contains("settingsManageUsersTabBtn", script, StringComparison.Ordinal);
        Assert.Contains("settingsImportExportTabBtn", script, StringComparison.Ordinal);
        Assert.Contains("settingsPreferencesTabBtn", script, StringComparison.Ordinal);
        Assert.Contains("setSettingsTab", script, StringComparison.Ordinal);
        Assert.Contains("activateSettingsTab", script, StringComparison.Ordinal);
        Assert.Contains("handleSettingsTabKeydown", script, StringComparison.Ordinal);
        Assert.Contains("moveSettingsTabFocus", script, StringComparison.Ordinal);
        Assert.Contains("activateSettingsManageUsersTab", script, StringComparison.Ordinal);
        Assert.Contains("activateSettingsImportExportTab", script, StringComparison.Ordinal);
        Assert.Contains("resetUserManagementState", script, StringComparison.Ordinal);
        Assert.Contains("closeSettingsDialog", script, StringComparison.Ordinal);
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
        Assert.Contains("userStatusFilter", script, StringComparison.Ordinal);
        Assert.Contains("user-status-pills", script, StringComparison.Ordinal);
        Assert.Contains("setUserManagementTab", script, StringComparison.Ordinal);
        Assert.Contains("handleUserManagementTabKeydown", script, StringComparison.Ordinal);
        Assert.Contains("/api/version", script, StringComparison.Ordinal);
        Assert.Contains("loadAppVersion", script, StringComparison.Ordinal);
        Assert.Contains("toLocaleLowerCase", script, StringComparison.Ordinal);
        Assert.Contains("normalizedRotation", script, StringComparison.Ordinal);
        Assert.Contains("rotationDelta", script, StringComparison.Ordinal);
        Assert.DoesNotContain("const wheelBooks = [...activeBooks];", script, StringComparison.Ordinal);
        Assert.Contains("const selectedIndex = wheelBooks.findIndex", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/status", script, StringComparison.Ordinal);
        Assert.Contains("/api/auth/setup", script, StringComparison.Ordinal);

        // Negative: menu consolidation (GH #52) replaced the separate "Manage users"
        // and import/export toolbar buttons and their standalone dialogs with tabs
        // inside a single Settings dialog, so the old open/close functions for those
        // standalone dialogs must not linger as dead code.
        Assert.DoesNotContain("openUserManagementDialog", script, StringComparison.Ordinal);
        Assert.DoesNotContain("openTransferDialog", script, StringComparison.Ordinal);
        Assert.DoesNotContain("closeTransferDialog", script, StringComparison.Ordinal);
        Assert.DoesNotContain("closeUserManagementDialog", script, StringComparison.Ordinal);
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
    public async Task Frontend_Styles_Should_Keep_Settings_Dialog_Responsive()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n");

        // Positive: the consolidated Settings dialog (Manage users / Import-Export /
        // Preferences tabs, GH #52) stays in the visible viewport and its shell
        // scrolls independently when a tab's content is taller than the screen,
        // regardless of which tab is active.
        Assert.Contains(".user-management-dialog {\n  width: calc(100% - 24px);\n  max-height: min(90vh, 760px);", css, StringComparison.Ordinal);
        Assert.Contains(".user-management-shell {\n  gap: 14px;\n  padding: 20px;\n  max-height: min(90vh, 760px);", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", css, StringComparison.Ordinal);
        Assert.Contains(".user-management-dialog .tab-panel {\n    padding: 12px;\n  }", css, StringComparison.Ordinal);
        Assert.Contains(".user-management-dialog .modal-actions > button {\n    flex: 1 1 160px;\n  }", css, StringComparison.Ordinal);
        Assert.Contains(".user-management-dialog input[type=\"file\"] {\n  width: 100%;\n  max-width: 100%;\n  min-width: 0;", css, StringComparison.Ordinal);

        // Positive: on tablet and wider viewports the dialog widens to 80% of
        // the viewport instead of staying pinned to the narrow mobile inset.
        Assert.Contains("@media (min-width: 901px) {\n  .user-management-dialog {\n    width: 80%;\n  }\n}", css, StringComparison.Ordinal);

        // Negative: fixed-width dialogs overflow narrow mobile and tablet viewports.
        Assert.DoesNotContain(".user-management-dialog {\n  width: 1450px;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".user-management-dialog {\n  width: 1225px;", css, StringComparison.Ordinal);
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

    [Fact]
    public async Task Settings_Dialog_Should_Expose_Consolidated_Tab_Structure()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Positive: GH #52 consolidates the "Manage users", import/export, and
        // preferences entry points into one Settings dialog with three ARIA tabs,
        // each wired to its own panel via aria-controls / aria-labelledby.
        Assert.Contains("id=\"settingsTabRow\" class=\"tab-row\" role=\"tablist\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsManageUsersTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"settingsManageUsersPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"settingsImportExportPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"settingsPreferencesPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"settingsManageUsersTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"settingsImportExportTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"settingsPreferencesTabBtn\"", html, StringComparison.Ordinal);

        // Positive: the admin-only Manage users tab starts hidden in markup (JS
        // reveals it only for admins), while Preferences is the default active
        // tab, matching the dialog always opening on Preferences.
        Assert.Contains("id=\"settingsManageUsersTabBtn\" class=\"secondary tab-btn hidden\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsPreferencesTabBtn\" class=\"secondary tab-btn active\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsPreferencesPanel\" class=\"tab-panel\" role=\"tabpanel\"", html, StringComparison.Ordinal);

        // Negative: the old standalone dialogs and their toolbar entry points are
        // gone now that everything lives inside the single Settings dialog.
        Assert.DoesNotContain("id=\"userManagementDialog\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"transferDialog\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"userManagementBtn\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"importExportBtn\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Gate_Settings_Tabs_By_Login_And_Admin_State()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n");

        // Positive: applyCurrentUser hides the whole tab row when logged out
        // (only Preferences is relevant without a session), hides Manage users
        // unless the current user is an admin, and hides Import/Export unless
        // a session exists.
        Assert.Contains("settingsTabRow.classList.toggle('hidden', !isLoggedIn)", script, StringComparison.Ordinal);
        Assert.Contains("settingsManageUsersTabBtn.classList.toggle('hidden', !canManageUsers)", script, StringComparison.Ordinal);
        Assert.Contains("settingsImportExportTabBtn.classList.toggle('hidden', !isLoggedIn)", script, StringComparison.Ordinal);

        // Positive: opening the dialog always lands on Preferences without
        // eagerly loading the user directory; the user list is only fetched
        // once the Manage users tab is actually activated.
        Assert.Contains("function openSettingsDialog() {\n  setSettingsTab('preferences');\n  openDialog(settingsDialog, settingsPreferencesTabBtn);\n}", script, StringComparison.Ordinal);
        Assert.Contains("async function activateSettingsManageUsersTab() {\n  resetUserManagementMessages();\n  setUserManagementTab('directory');\n  await loadUsers();\n}", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_I18n_Should_Include_Settings_Tab_Labels_In_All_Locales()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: the new consolidated tab labels are translated for every
        // supported locale, not just English.
        Assert.Contains("manageUsersTab: 'Manage users'", script, StringComparison.Ordinal);
        Assert.Contains("importExportTab: 'Import / Export'", script, StringComparison.Ordinal);
        Assert.Contains("preferencesTab: 'Preferences'", script, StringComparison.Ordinal);
        Assert.Contains("manageUsersTab: 'Gestionar usuarios'", script, StringComparison.Ordinal);
        Assert.Contains("importExportTab: 'Importar / Exportar'", script, StringComparison.Ordinal);
        Assert.Contains("preferencesTab: 'Preferencias'", script, StringComparison.Ordinal);
        Assert.Contains("manageUsersTab: 'Zarządzaj użytkownikami'", script, StringComparison.Ordinal);
        Assert.Contains("importExportTab: 'Importuj / Eksportuj'", script, StringComparison.Ordinal);
        Assert.Contains("preferencesTab: 'Preferencje'", script, StringComparison.Ordinal);

        // Negative: the retired standalone dialog titles/kicker strings for the
        // old "User management" dialog should not linger as unused translations.
        Assert.DoesNotContain("managementDialogTitle", script, StringComparison.Ordinal);
        Assert.DoesNotContain("adminWorkspaceKicker", script, StringComparison.Ordinal);
    }
}
