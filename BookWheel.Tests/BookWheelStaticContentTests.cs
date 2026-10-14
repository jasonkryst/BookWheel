using System.Net;
using System.Text.RegularExpressions;

namespace BookWheel.Tests;

public sealed class BookWheelStaticContentTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
{
    private readonly BookWheelWebAppFactory _factory;

    public BookWheelStaticContentTests(BookWheelWebAppFactory factory)
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
    public async Task Frontend_Should_Serve_I18n_Script_With_All_Locale_Catalogs()
    {
        var factory = _factory;
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
        Assert.Contains("Selected: {title}", script, StringComparison.Ordinal);
        Assert.Contains("By Media Type", script, StringComparison.Ordinal);
        Assert.Contains("Version: {version}", script, StringComparison.Ordinal);
        Assert.Contains("Page {current} of {total}", script, StringComparison.Ordinal);
        Assert.Contains("Generate reset link", script, StringComparison.Ordinal);
        Assert.Contains("Fuente de información del libro", script, StringComparison.Ordinal);
        Assert.Contains("Źródło informacji o książce", script, StringComparison.Ordinal);

        // Negative: locale data lives only in this catalog file, not duplicated
        // as a second hardcoded English-only copy anywhere in the same file.
        Assert.DoesNotContain("const LEGACY_EN_ONLY_STRINGS", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_I18n_Attributes_And_Settings_Button()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Positive: settings entry point exists and static text is externalized via data-i18n.
        Assert.Contains("id=\"settingsBtnLoggedOut\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsBtnLoggedIn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"wheel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"books\"", html, StringComparison.Ordinal);
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
    public async Task Admin_User_Directory_Should_Expose_Last_Login_Sort_And_Delete_Book_Count()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var htmlResponse = await client.GetAsync("/");
        var html = await htmlResponse.Content.ReadAsStringAsync();

        // Positive: a sort control for the admin user directory exists, including
        // the two new last-login sort orders.
        Assert.Contains("id=\"userSortSelect\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"last-login-desc\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"last-login-asc\"", html, StringComparison.Ordinal);

        var scriptResponse = await client.GetAsync("/js/app.js");
        var script = await scriptResponse.Content.ReadAsStringAsync();

        // Positive: the directory row shows a last-login value (or "never"), the sort
        // select re-renders from the cached list, and the delete-confirmation dialog is
        // given the user's book count so it can state how many books will be removed.
        Assert.Contains("users.lastLoginOn", script, StringComparison.Ordinal);
        Assert.Contains("users.neverLoggedIn", script, StringComparison.Ordinal);
        Assert.Contains("userSortSelect.addEventListener('change'", script, StringComparison.Ordinal);
        Assert.Contains("users.deleteConfirmMessage", script, StringComparison.Ordinal);
        Assert.Contains("count: user.bookCount || 0", script, StringComparison.Ordinal);

        var i18nResponse = await client.GetAsync("/js/i18n.js");
        var i18n = await i18nResponse.Content.ReadAsStringAsync();

        // Negative: the delete-confirmation copy must not be the old count-free wording
        // in any of the three catalogs, since a stale copy would silently drop the count.
        Assert.DoesNotContain("and all of their books?", i18n, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Support_Shareable_App_View_Routes()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("setRequestedView('stats')", script, StringComparison.Ordinal);
        Assert.Contains("setRequestedView('preferences')", script, StringComparison.Ordinal);
        Assert.Contains("view === 'stats'", script, StringComparison.Ordinal);
        Assert.Contains("view === 'preferences'", script, StringComparison.Ordinal);
        Assert.Contains("wheel: document.getElementById('wheel')", script, StringComparison.Ordinal);
        Assert.Contains("books: document.getElementById('books')", script, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('popstate'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Route_Add_Book_And_Scan_Shortcuts_To_Their_Entry_Points()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: the "Add book" shortcut scrolls to the books panel and
        // focuses the quick-add title field (not a dedicated page); the "Scan"
        // shortcut opens Book Search and starts the scanner immediately,
        // targeting its ISBN field, reusing the dialog's own scan handler.
        Assert.Contains("view === 'add-book'", script, StringComparison.Ordinal);
        Assert.Contains("view === 'scan'", script, StringComparison.Ordinal);

        var addBookStart = script.IndexOf("view === 'add-book'", StringComparison.Ordinal);
        var addBookBranch = script.Substring(addBookStart, Math.Min(250, script.Length - addBookStart));
        Assert.Contains("targets.books.scrollIntoView", addBookBranch, StringComparison.Ordinal);
        Assert.Contains("bookTitle.focus()", addBookBranch, StringComparison.Ordinal);

        var scanStart = script.IndexOf("view === 'scan'", StringComparison.Ordinal);
        var scanBranch = script.Substring(scanStart, Math.Min(300, script.Length - scanStart));
        Assert.Contains("openBookInfoDialog()", scanBranch, StringComparison.Ordinal);
        Assert.Contains("openBarcodeScanner({", scanBranch, StringComparison.Ordinal);
        Assert.Contains("isbnInput: bookInfoIsbn", scanBranch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Prefill_Book_Search_From_A_Shared_Isbn_Or_Title()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: an ISBN-13 (978/979 prefix) found in the shared text, url,
        // or title auto-triggers the lookup (GH #243 acceptance criterion).
        Assert.Contains("function extractSharedIsbn(", script, StringComparison.Ordinal);
        Assert.Contains("SHARED_ISBN_13_PATTERN", script, StringComparison.Ordinal);
        Assert.Contains("function applySharedBookInfoRequest(params)", script, StringComparison.Ordinal);
        Assert.Contains("bookInfoIsbn.value = isbn;", script, StringComparison.Ordinal);
        Assert.Contains("runBookInfoSearch();", script, StringComparison.Ordinal);

        // Negative: a bare ISBN-10 (no "978"/"979" prefix) is only trusted right
        // after the literal word "ISBN", so an unrelated 10-digit run shared in
        // plain text (e.g. a phone number) can't be misread as a lookup.
        Assert.Contains("SHARED_ISBN_10_PATTERN = /\\bISBN", script, StringComparison.Ordinal);

        // Positive: the view=search route feeds share_target's title/text/url
        // params into the Book Search dialog and then scrubs them from the URL
        // so a later reload or back-navigation doesn't replay the same lookup.
        Assert.Contains("view === 'search'", script, StringComparison.Ordinal);
        Assert.Contains("applySharedBookInfoRequest(params)", script, StringComparison.Ordinal);
        Assert.Contains("url.searchParams.delete('title')", script, StringComparison.Ordinal);
        Assert.Contains("url.searchParams.delete('text')", script, StringComparison.Ordinal);
        Assert.Contains("url.searchParams.delete('url')", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Language_Select_Should_List_Supported_Locales()
    {
        var factory = _factory;
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
        var factory = _factory;
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
    public async Task Frontend_Script_Should_Round_Trip_Locale_Through_Server_Preferences()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: a locale change while signed in is persisted server-side
        // (same pattern as the theme toggle), and a fetched preference payload
        // is applied back onto the i18n module and the <select> on login/bootstrap.
        var langChangeHandlerStart = script.IndexOf("langSelect.addEventListener('change'", StringComparison.Ordinal);
        Assert.True(langChangeHandlerStart >= 0, "langSelect change handler not found");
        var langChangeHandlerBody = script.Substring(langChangeHandlerStart, 200);
        Assert.Contains("window.BookWheelI18n.setLocale(langSelect.value)", langChangeHandlerBody, StringComparison.Ordinal);
        Assert.Contains("savePreferencesIfAuthenticated()", langChangeHandlerBody, StringComparison.Ordinal);
        Assert.Contains("locale: window.BookWheelI18n.getCurrentLocale()", script, StringComparison.Ordinal);
        Assert.Contains("window.BookWheelI18n.setLocale(preferences.locale)", script, StringComparison.Ordinal);

        // Negative: an unsupported/unknown locale value from the server must not
        // be handed to setLocale (which itself also guards, but the call site
        // should not rely solely on that — the SUPPORTED_LOCALES check must be inline).
        Assert.Contains("window.BookWheelI18n.SUPPORTED_LOCALES.includes(preferences.locale)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Render_Main_UI_Structure()
    {
        var factory = _factory;
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
        var factory = _factory;
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
        var factory = _factory;
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
    public async Task Frontend_Should_Render_Configurable_Retailer_Links_With_Affiliate_Disclosure()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var htmlResponse = await client.GetAsync("/");
        var html = await htmlResponse.Content.ReadAsStringAsync();

        // Positive: GH #208 replaced the five hardcoded retailer anchors with
        // an empty container the script populates from the server's
        // (now-configurable) link list, plus a disclosure line.
        Assert.Contains("id=\"bookInfoLinks\" class=\"book-info-links\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookInfoAffiliateDisclosure\"", html, StringComparison.Ordinal);

        // Negative: none of the old fixed per-retailer anchors remain.
        Assert.DoesNotContain("id=\"bookInfoGoogleBooksLink\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"bookInfoAmazonLink\"", html, StringComparison.Ordinal);

        var scriptResponse = await client.GetAsync("/js/app.js");
        var script = await scriptResponse.Content.ReadAsStringAsync();

        // Positive: each link is rendered with its own rel, switching to
        // "sponsored noopener" only when the server marked it as an affiliate
        // link, and the disclosure note is shown only when at least one is.
        Assert.Contains("function renderBookInfoLinks(links) {", script, StringComparison.Ordinal);
        Assert.Contains("link.isAffiliate ? 'sponsored noopener' : 'noopener noreferrer'", script, StringComparison.Ordinal);
        Assert.Contains("!links.some(link => link.isAffiliate)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Render_Cover_And_Author_For_Spin_Selection()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: the spin result handler builds the selected-book display
        // through a dedicated renderer rather than a plain text assignment,
        // so it can include the cover image and author alongside the title.
        Assert.Contains("function renderSelectedBookResult(book)", script, StringComparison.Ordinal);
        Assert.Contains("renderSelectedBookResult(selected)", script, StringComparison.Ordinal);
        Assert.Contains("selected-book-cover", script, StringComparison.Ordinal);
        Assert.Contains("selected-book-details", script, StringComparison.Ordinal);

        // Negative: the cover image and author must stay conditional on the
        // book actually having that metadata (GH #57 fields are optional),
        // not rendered unconditionally.
        Assert.Contains("if (book.coverUrl)", script, StringComparison.Ordinal);
        Assert.Contains("if (book.author)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Should_Reveal_Spin_Result_On_Transitionend_With_Opt_In_Celebration_Effects()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var htmlResponse = await client.GetAsync("/");
        var html = await htmlResponse.Content.ReadAsStringAsync();

        // Positive: a Preferences toggle exists for the celebration effects,
        // defaulted on like the other opt-out preference checkboxes.
        Assert.Contains("id=\"spinCelebrationCheckbox\" type=\"checkbox\" checked", html, StringComparison.Ordinal);

        var scriptResponse = await client.GetAsync("/js/app.js");
        var script = await scriptResponse.Content.ReadAsStringAsync();

        // Positive: the spin result is revealed by a transitionend listener
        // (filtered to the wheel's own transform transition) rather than a
        // bare setTimeout, so it can't desync from the CSS animation; a
        // fallback timer still exists in case the event never fires.
        var spinHandlerStart = script.IndexOf("spinBtn.addEventListener('click'", StringComparison.Ordinal);
        Assert.True(spinHandlerStart >= 0, "Expected a spinBtn click handler.");
        var spinHandlerBody = script.Substring(spinHandlerStart, Math.Min(4000, script.Length - spinHandlerStart));
        Assert.Contains("addEventListener('transitionend'", spinHandlerBody, StringComparison.Ordinal);
        Assert.Contains("event.propertyName !== 'transform'", spinHandlerBody, StringComparison.Ordinal);
        Assert.Contains("fallbackTimer", spinHandlerBody, StringComparison.Ordinal);

        // Positive: celebration effects (tick audio, haptics, confetti) are
        // gated behind both the reduced-motion check and the preference toggle.
        Assert.Contains("function isSpinCelebrationEnabled() {", script, StringComparison.Ordinal);
        Assert.Contains("!isSpinCelebrationOptedOut() && !isReducedMotionEnabled()", script, StringComparison.Ordinal);
        Assert.Contains("navigator.vibrate(STOP_VIBRATE_PATTERN)", script, StringComparison.Ordinal);
        Assert.Contains("scheduleSpinTicks(rotationDelta, slice, WHEEL_TRANSITION_MS)", spinHandlerBody, StringComparison.Ordinal);

        // Negative: the tick/vibrate/confetti calls must be conditional on
        // celebrationEnabled, not unconditional on every spin.
        var celebrationGateCount = Regex.Matches(spinHandlerBody, Regex.Escape("if (celebrationEnabled) {")).Count;
        Assert.Equal(2, celebrationGateCount);
        Assert.Contains("scheduleSpinTicks(rotationDelta, slice, WHEEL_TRANSITION_MS);", spinHandlerBody, StringComparison.Ordinal);
        Assert.Contains("playSpinStopCelebration();", spinHandlerBody, StringComparison.Ordinal);

        var cssResponse = await client.GetAsync("/css/site.css");
        var css = await cssResponse.Content.ReadAsStringAsync();

        Assert.Contains(".confetti-piece", css, StringComparison.Ordinal);
        Assert.Contains("@keyframes confetti-burst", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Size_The_Selected_Book_Cover()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        Assert.Contains(".selected-book-cover", css, StringComparison.Ordinal);
        Assert.Contains(".selected-book-details", css, StringComparison.Ordinal);

        // Positive: the "Last selected" cover is shown noticeably larger than
        // the small 36x52 thumbnail used elsewhere (add/edit preview, active
        // book rows, lookup picker), at 1.5x the original 64x92 size so the
        // 2:3-ish book-cover aspect ratio is preserved (GH #63).
        var coverStart = css.IndexOf(".selected-book-cover {", StringComparison.Ordinal);
        Assert.True(coverStart >= 0, "Expected a .selected-book-cover rule.");
        var coverSnippet = css.Substring(coverStart, Math.Min(150, css.Length - coverStart));
        Assert.Contains("width: 96px", coverSnippet, StringComparison.Ordinal);
        Assert.Contains("height: 138px", coverSnippet, StringComparison.Ordinal);

        // Negative: must not still be the old, smaller thumbnail size.
        Assert.DoesNotContain("width: 64px", coverSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Derive_Selected_Book_Accent_From_The_Theme_Variable()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        // Positive: the spin-result highlight's glow and border derive from
        // the theme's --accent variable, so they follow light/dark/
        // high-contrast mode instead of a single fixed color (GH #63).
        var selectedBookStart = css.IndexOf("\n.selected-book {", StringComparison.Ordinal);
        Assert.True(selectedBookStart >= 0, "Expected a top-level .selected-book rule (not the high-contrast override).");
        var selectedBookSnippet = css.Substring(selectedBookStart, Math.Min(300, css.Length - selectedBookStart));
        Assert.Contains("color-mix(in srgb, var(--accent)", selectedBookSnippet, StringComparison.Ordinal);

        var notEmptyStart = css.IndexOf("\n.selected-book:not(:empty) {", StringComparison.Ordinal);
        Assert.True(notEmptyStart >= 0, "Expected a top-level .selected-book:not(:empty) rule (not the high-contrast override).");
        var notEmptySnippet = css.Substring(notEmptyStart, Math.Min(300, css.Length - notEmptyStart));
        Assert.Contains("color-mix(in srgb, var(--accent)", notEmptySnippet, StringComparison.Ordinal);

        var toastInfoStart = css.IndexOf(".toast-info {", StringComparison.Ordinal);
        Assert.True(toastInfoStart >= 0, "Expected a .toast-info rule.");
        var toastInfoSnippet = css.Substring(toastInfoStart, Math.Min(150, css.Length - toastInfoStart));
        Assert.Contains("color-mix(in srgb, var(--accent)", toastInfoSnippet, StringComparison.Ordinal);

        // Negative: none of these should still hardcode the dark-theme
        // accent blue, since that value doesn't shift with the active theme.
        Assert.DoesNotContain("rgba(56, 189, 248", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Give_Lookup_Picker_Rows_Readable_Text_In_Every_Theme()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        // Positive: picker rows are <button> elements, so without an
        // explicit color they inherit the accent-button text color instead
        // of the theme's body text color (GH #63).
        var pickerRowStart = css.IndexOf(".lookup-picker-row {", StringComparison.Ordinal);
        Assert.True(pickerRowStart >= 0, "Expected a .lookup-picker-row rule.");
        var pickerRowSnippet = css.Substring(pickerRowStart, Math.Min(200, css.Length - pickerRowStart));
        Assert.Contains("color: var(--text)", pickerRowSnippet, StringComparison.Ordinal);

        // Negative: it must not rely on --button-primary-text (meant for
        // solid accent-colored buttons), which is unreadable against a
        // transparent row background in both light and dark mode.
        Assert.DoesNotContain("--button-primary-text", pickerRowSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_A_Lookup_Picker_For_Ambiguous_Title_Matches()
    {
        var factory = _factory;
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
        var factory = _factory;
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
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("const DEFAULT_BOOKS_PER_PAGE = 10", script, StringComparison.Ordinal);
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
    public async Task Frontend_Script_Should_Contain_Import_Export_Isbn_Account_And_History_Behavior()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("parseImportBooks", script, StringComparison.Ordinal);
        Assert.Contains("'/api/books/import'", script, StringComparison.Ordinal);
        Assert.Contains("await requestJson('/api/books/export')", script, StringComparison.Ordinal);
        Assert.Contains("accountMismatchWarning", script, StringComparison.Ordinal);

        // Negative: import moved from a client-side per-title POST loop to a single
        // batched call against the new import endpoint, so the old parsing helper
        // and per-title dedupe logic must not linger as dead code.
        Assert.DoesNotContain("parseImportTitles", script, StringComparison.Ordinal);
        Assert.DoesNotContain("normalizeTitle", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Contain_Theme_Toggle_Behavior()
    {
        var factory = _factory;
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
        var factory = _factory;
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
        var factory = _factory;
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
    public async Task Frontend_Styles_Should_Prevent_The_Page_From_Growing_Wider_Than_The_Viewport()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();

        // Positive: a safety-net clip on the document so no descendant —
        // including transformed/absolutely-positioned content that isn't
        // clipped by its own containing block, such as the spin confetti
        // burst — can widen the page and cause horizontal scroll on mobile.
        var bodyStart = css.IndexOf("body {", StringComparison.Ordinal);
        Assert.True(bodyStart >= 0, "Expected a body rule.");
        var bodyRule = css.Substring(bodyStart, css.IndexOf('}', bodyStart) - bodyStart);
        Assert.Contains("overflow-x: hidden;", bodyRule, StringComparison.Ordinal);

        // Positive: the admin user-directory toolbar grid lists an explicit
        // column for all four of its fields (search, status filter, sort,
        // clear), not just three — an under-specified grid-template-columns
        // left the fourth field to an auto-sized implicit column that could
        // grow past the container on narrower-than-900px layouts where the
        // single-column mobile override doesn't apply.
        var toolsStart = css.IndexOf(".user-directory-tools {", StringComparison.Ordinal);
        Assert.True(toolsStart >= 0, "Expected a .user-directory-tools rule.");
        var toolsRule = css.Substring(toolsStart, css.IndexOf('}', toolsStart) - toolsStart);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) minmax(160px, 220px) minmax(160px, 220px) auto;", toolsRule, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Keep_Settings_Dialog_Responsive()
    {
        var factory = _factory;
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
        var factory = _factory;
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
        var factory = _factory;
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
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsStringAsync();

        // Positive: theme cycling and system-preference detection include
        // the new high-contrast state.
        Assert.Contains("HIGH_CONTRAST_THEME = 'high-contrast'", script, StringComparison.Ordinal);
        Assert.Contains("THEME_CYCLE = [SYSTEM_THEME, DARK_THEME, LIGHT_THEME, HIGH_CONTRAST_THEME]", script, StringComparison.Ordinal);
        Assert.Contains("prefers-contrast: more", script, StringComparison.Ordinal);
        Assert.Contains("--wheel-slice-", script, StringComparison.Ordinal);

        // Negative: the wheel renderer must no longer hardcode its slice
        // palette, since a hardcoded array can't be re-themed for
        // high-contrast mode.
        Assert.DoesNotContain("['#38bdf8', '#60a5fa', '#818cf8', '#f472b6', '#34d399', '#fbbf24']", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Add_A_Follow_System_Theme_That_Tracks_Live_Os_Changes()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: "system" is a real, storable preference distinct from a
        // resolved dark/light/high-contrast value (GH #232) — the preference
        // getter can return it, and it's the default when nothing is saved
        // yet, rather than immediately resolving and pinning a concrete theme.
        Assert.Contains("const SYSTEM_THEME = 'system'", script, StringComparison.Ordinal);
        Assert.Contains("function resolveSystemTheme() {", script, StringComparison.Ordinal);
        Assert.Contains("function resolveDisplayTheme(preference) {", script, StringComparison.Ordinal);
        Assert.Contains("return THEME_CYCLE.includes(persisted) ? persisted : SYSTEM_THEME;", script, StringComparison.Ordinal);

        // Positive: live matchMedia listeners re-resolve and re-apply the
        // theme when the OS color-scheme or contrast preference changes,
        // but only while the user is actually following the system setting.
        Assert.Contains("prefersColorSchemeDark.addEventListener('change', reapplySystemThemeIfFollowing)", script, StringComparison.Ordinal);
        Assert.Contains("prefersContrastMore.addEventListener('change', reapplySystemThemeIfFollowing)", script, StringComparison.Ordinal);
        Assert.Contains("function reapplySystemThemeIfFollowing() {", script, StringComparison.Ordinal);
        Assert.Contains("if (getPreferredTheme() === SYSTEM_THEME) {", script, StringComparison.Ordinal);

        // Negative: an explicit choice must keep persisting as that explicit
        // value, not get silently overwritten by the currently-resolved
        // display theme on an unrelated event (e.g. a locale change used to
        // reapply the DOM's data-theme attribute directly, which would have
        // pinned "system" to whatever it last resolved to).
        Assert.DoesNotContain("document.documentElement.getAttribute('data-theme') || DARK_THEME", script, StringComparison.Ordinal);
        Assert.Contains("applyTheme(getPreferredTheme());", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_Dialog_Header_Should_Not_Use_Semantic_Header_Element()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        var dialogStart = html.IndexOf("id=\"settingsDialog\"", StringComparison.Ordinal);
        Assert.True(dialogStart >= 0, "Expected settingsDialog to be present.");
        var dialogEnd = html.IndexOf("</dialog>", dialogStart, StringComparison.Ordinal);
        Assert.True(dialogEnd > dialogStart, "Expected a closing </dialog> tag after settingsDialog.");
        var dialogMarkup = html.Substring(dialogStart, dialogEnd - dialogStart);

        // Positive: the user-management-header is a <div>, which carries no
        // landmark role — it exists only as a CSS hook (GH #153).
        Assert.Contains("<div class=\"user-management-header\">", dialogMarkup, StringComparison.Ordinal);

        // Negative: <header> inside <dialog> is still announced as a "banner"
        // landmark by some assistive technologies despite the sectioning-ancestor
        // rule, creating confusing navigation noise.
        Assert.DoesNotContain("<header class=\"user-management-header\">", dialogMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_Dialog_Should_Expose_Consolidated_Tab_Structure()
    {
        var factory = _factory;
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
        var factory = _factory;
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
        Assert.Contains("function openSettingsDialog(updateUrl = true) {\n  setSettingsTab('preferences');\n  openDialog(settingsDialog, settingsPreferencesTabBtn);\n  if (updateUrl) {\n    setRequestedView('preferences');\n  }\n}", script, StringComparison.Ordinal);
        Assert.Contains("async function activateSettingsManageUsersTab() {\n  resetUserManagementMessages();\n  if (testEmailResult) {\n    testEmailResult.textContent = '';\n  }\n  setUserManagementTab('directory');\n  await Promise.all([loadUsers(), loadEmailReadiness()]);\n}", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_I18n_Should_Include_Settings_Tab_Labels_In_All_Locales()
    {
        var factory = _factory;
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

    [Fact]
    public async Task Frontend_I18n_Should_Include_Account_Mismatch_Warning_In_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("accountMismatchWarning: 'Note: this file was exported from a different account", script, StringComparison.Ordinal);
        Assert.Contains("accountMismatchWarning: 'Nota: este archivo se exportó desde otra cuenta", script, StringComparison.Ordinal);
        Assert.Contains("accountMismatchWarning: 'Uwaga: ten plik wyeksportowano z innego konta", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_Barcode_Scanner_Dialog_And_Scan_Buttons()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Positive: the scanner dialog and all its interactive children are present.
        Assert.Contains("id=\"scannerDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"scannerVideo\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"scannerStatus\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cancelScannerBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"scannerFlipBtn\"", html, StringComparison.Ordinal);

        // Positive: both the add-book row and the edit dialog expose a scan button.
        Assert.Contains("id=\"bookScanBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"editScanBtn\"", html, StringComparison.Ordinal);

        // Positive: scan buttons are properly labelled for accessibility.
        Assert.Contains("data-i18n-aria-label=\"scanner.scanBtnLabel\"", html, StringComparison.Ordinal);

        // Positive: the video element uses the attributes required for mobile autoplay.
        Assert.Contains("autoplay", html, StringComparison.Ordinal);
        Assert.Contains("playsinline", html, StringComparison.Ordinal);
        Assert.Contains("muted", html, StringComparison.Ordinal);

        // Negative: the scan button must not have a 'required' attribute; it is
        // a trigger, not an input field.
        var scanBtnStart = html.IndexOf("id=\"bookScanBtn\"", StringComparison.Ordinal);
        var scanBtnSnippet = html.Substring(scanBtnStart, Math.Min(200, html.Length - scanBtnStart));
        Assert.DoesNotContain("required", scanBtnSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Implement_Barcode_Scanner_Logic()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: core scanner functions are present and named consistently.
        Assert.Contains("function openBarcodeScanner(target)", script, StringComparison.Ordinal);
        Assert.Contains("function closeBarcodeScanner()", script, StringComparison.Ordinal);
        Assert.Contains("function startScanLoop()", script, StringComparison.Ordinal);
        Assert.Contains("async function onBarcodeDetected(isbn)", script, StringComparison.Ordinal);
        Assert.Contains("function stopScannerStream()", script, StringComparison.Ordinal);
        Assert.Contains("function flipScannerCamera()", script, StringComparison.Ordinal);

        // Positive: the scanner guards itself with a feature-detection check so
        // it shows a graceful message on unsupported browsers instead of throwing.
        Assert.Contains("BARCODE_SCANNER_SUPPORTED", script, StringComparison.Ordinal);
        Assert.Contains("typeof BarcodeDetector !== 'undefined'", script, StringComparison.Ordinal);

        // Positive: EAN-13 and EAN-8 formats are requested (ISBN barcodes are EAN).
        Assert.Contains("ean_13", script, StringComparison.Ordinal);
        Assert.Contains("ean_8", script, StringComparison.Ordinal);

        // Positive: camera flip toggles between environment and user facing modes.
        Assert.Contains("facingMode", script, StringComparison.Ordinal);
        Assert.Contains("environment", script, StringComparison.Ordinal);

        // Positive: both scan buttons wire to the same openBarcodeScanner function
        // with the correct form-specific targets (matching the Lookup button pattern).
        Assert.Contains("bookScanBtn.addEventListener('click'", script, StringComparison.Ordinal);
        Assert.Contains("editScanBtn.addEventListener('click'", script, StringComparison.Ordinal);

        // Positive: after detection the ISBN field is populated and the existing
        // lookup path fires automatically — no duplicate fetch logic.
        Assert.Contains("target.isbnInput.value = isbn", script, StringComparison.Ordinal);
        Assert.Contains("await runMetadataLookup(target)", script, StringComparison.Ordinal);

        // Positive: the scanner dialog's close event stops the camera stream so
        // the camera indicator light turns off when the dialog is dismissed.
        Assert.Contains("scannerDialog.addEventListener('close'", script, StringComparison.Ordinal);
        Assert.Contains("stopScannerStream()", script, StringComparison.Ordinal);

        // Positive: the addedByScanner flag is captured from the scanner and included
        // in the add-book form submission body.
        Assert.Contains("addedByScanner", script, StringComparison.Ordinal);
        Assert.Contains("trackScanner: true", script, StringComparison.Ordinal);

        // Positive: the scanner badge is rendered for books that were added by scan.
        Assert.Contains("book-scanner-badge", script, StringComparison.Ordinal);

        // Negative: the scanner must not reference BarcodeDetector directly
        // without guarding against the undefined case first.
        var detectorCheckIndex = script.IndexOf("BARCODE_SCANNER_SUPPORTED", StringComparison.Ordinal);
        var firstRawDetectorUse = script.IndexOf("new BarcodeDetector", StringComparison.Ordinal);
        Assert.True(detectorCheckIndex < firstRawDetectorUse,
            "The BarcodeDetector availability check must appear before the first 'new BarcodeDetector' call.");
    }

    [Fact]
    public async Task Frontend_Script_Should_Release_Camera_On_Scanner_Close()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: stopScannerStream cancels the rAF loop and stops all media
        // tracks — not just pausing the video — so the camera light turns off.
        var stopFnStart = script.IndexOf("function stopScannerStream()", StringComparison.Ordinal);
        Assert.True(stopFnStart >= 0, "Expected a stopScannerStream function.");
        var stopFnSnippet = script.Substring(stopFnStart, Math.Min(400, script.Length - stopFnStart));
        Assert.Contains("cancelAnimationFrame", stopFnSnippet, StringComparison.Ordinal);
        Assert.Contains("getTracks()", stopFnSnippet, StringComparison.Ordinal);
        Assert.Contains("track.stop()", stopFnSnippet, StringComparison.Ordinal);

        // Negative: closeBarcodeScanner must call stopScannerStream rather than
        // duplicating the stream teardown, to avoid divergence over time.
        var closeFnStart = script.IndexOf("function closeBarcodeScanner()", StringComparison.Ordinal);
        Assert.True(closeFnStart >= 0, "Expected a closeBarcodeScanner function.");
        var closeFnSnippet = script.Substring(closeFnStart, Math.Min(200, script.Length - closeFnStart));
        Assert.Contains("stopScannerStream()", closeFnSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_I18n_Should_Include_Scanner_Strings_In_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: all three locales carry a scanner section with the
        // strings needed by the scanner dialog and scan buttons.
        Assert.Contains("scanBtnTitle: 'Scan ISBN barcode'", script, StringComparison.Ordinal);
        Assert.Contains("scanBtnTitle: 'Escanear código ISBN'", script, StringComparison.Ordinal);
        Assert.Contains("scanBtnTitle: 'Skanuj kod ISBN'", script, StringComparison.Ordinal);

        Assert.Contains("notSupportedError: 'Barcode scanning is not supported", script, StringComparison.Ordinal);
        Assert.Contains("notSupportedError: 'El escaneo de códigos de barras", script, StringComparison.Ordinal);
        Assert.Contains("notSupportedError: 'Skanowanie kodów kreskowych", script, StringComparison.Ordinal);

        Assert.Contains("cameraAccessError: 'Camera access was denied", script, StringComparison.Ordinal);
        Assert.Contains("cameraAccessError: 'El acceso a la cámara fue denegado", script, StringComparison.Ordinal);
        Assert.Contains("cameraAccessError: 'Dostęp do kamery", script, StringComparison.Ordinal);

        // Positive: all three locales carry the scanner badge strings for books added by scan.
        Assert.Contains("scannedBadgeTitle: 'Added via barcode scan'", script, StringComparison.Ordinal);
        Assert.Contains("scannedBadgeTitle: 'Añadido mediante escaneo", script, StringComparison.Ordinal);
        Assert.Contains("scannedBadgeTitle: 'Dodano przez skanowanie", script, StringComparison.Ordinal);

        // Negative: scanner strings must not fall back to English text in the
        // non-English locales (would mean the translation was accidentally skipped).
        var esStart = script.IndexOf("es: {", StringComparison.Ordinal);
        var plStart = script.IndexOf("pl: {", StringComparison.Ordinal);
        Assert.True(esStart >= 0 && plStart >= 0);

        var esSection = script.Substring(esStart, plStart - esStart);
        Assert.DoesNotContain("scanBtnTitle: 'Scan ISBN barcode'", esSection, StringComparison.Ordinal);

        var plSection = script.Substring(plStart);
        Assert.DoesNotContain("scanBtnTitle: 'Scan ISBN barcode'", plSection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Styles_Should_Include_Scanner_Video_And_Reticle_Rules()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/site.css");
        var css = await response.Content.ReadAsStringAsync();

        // Positive: the scanner modal and video container have dedicated rules.
        Assert.Contains(".scanner-modal", css, StringComparison.Ordinal);
        Assert.Contains(".scanner-video-wrap", css, StringComparison.Ordinal);
        Assert.Contains("#scannerVideo", css, StringComparison.Ordinal);
        Assert.Contains(".scanner-reticle", css, StringComparison.Ordinal);
        Assert.Contains(".scanner-overlay", css, StringComparison.Ordinal);

        // Positive: video fills its container without letterboxing.
        var videoRuleStart = css.IndexOf("#scannerVideo {", StringComparison.Ordinal);
        Assert.True(videoRuleStart >= 0, "Expected a #scannerVideo rule.");
        var videoRuleSnippet = css.Substring(videoRuleStart, Math.Min(150, css.Length - videoRuleStart));
        Assert.Contains("object-fit: cover", videoRuleSnippet, StringComparison.Ordinal);

        // Positive: video container preserves a fixed aspect ratio so it doesn't
        // collapse to zero height before the stream arrives.
        var wrapRuleStart = css.IndexOf(".scanner-video-wrap {", StringComparison.Ordinal);
        Assert.True(wrapRuleStart >= 0, "Expected a .scanner-video-wrap rule.");
        var wrapRuleSnippet = css.Substring(wrapRuleStart, Math.Min(200, css.Length - wrapRuleStart));
        Assert.Contains("aspect-ratio", wrapRuleSnippet, StringComparison.Ordinal);

        // Positive: the reticle uses the theme accent so it stays visible
        // in both light and dark modes.
        var reticleStart = css.IndexOf(".scanner-reticle {", StringComparison.Ordinal);
        Assert.True(reticleStart >= 0, "Expected a .scanner-reticle rule.");
        var reticleSnippet = css.Substring(reticleStart, Math.Min(200, css.Length - reticleStart));
        Assert.Contains("var(--accent)", reticleSnippet, StringComparison.Ordinal);

        // Positive: the scanner badge has a dedicated rule so it renders as a
        // low-weight indicator beside the book title.
        Assert.Contains(".book-scanner-badge", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_Analytics_Opt_Out_Checkbox()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("id=\"analyticsConsentCheckbox\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"settings.analyticsLabel\"", html, StringComparison.Ordinal);
        Assert.Contains("window.__BOOKWHEEL_GA_ID__", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_Book_Info_Provider_Select()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("id=\"bookInfoProviderSelect\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"settings.bookInfoProviderLabel\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"1\">Open Library</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"2\">Google Books</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_Setup_Email_Field_And_Forgot_Links()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"setupEmail\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"auth.emailLabel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"forgotPasswordLinkBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"forgotUsernameLinkBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserEmail\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_I18n_Should_Include_Forgot_Password_And_Username_Strings_In_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("forgotPasswordLink: 'Forgot password?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotPasswordLink: '¿Olvidaste tu contraseña?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotPasswordLink: 'Nie pamiętasz hasła?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotUsernameLink: 'Forgot username?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotUsernameLink: '¿Olvidaste tu nombre de usuario?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotUsernameLink: 'Nie pamiętasz nazwy użytkownika?'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_Non_Dismissible_Email_Required_Dialog()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"emailRequiredDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"emailRequiredForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"emailRequiredInput\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"auth.emailRequiredDialogTitle\"", html, StringComparison.Ordinal);

        // Negative: no close/cancel button in the dialog markup — it must not be
        // dismissible without supplying an email.
        var dialogStart = html.IndexOf("id=\"emailRequiredDialog\"", StringComparison.Ordinal);
        var dialogEnd = html.IndexOf("</dialog>", dialogStart, StringComparison.Ordinal);
        var dialogMarkup = html.Substring(dialogStart, dialogEnd - dialogStart);
        Assert.DoesNotContain("type=\"button\"", dialogMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Prompt_Admins_Missing_Email_And_Block_Dialog_Dismissal()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("function needsEmail(user)", script, StringComparison.Ordinal);
        Assert.Contains("user.isAdmin && !user.email", script, StringComparison.Ordinal);
        Assert.Contains("emailRequiredDialog.addEventListener('cancel'", script, StringComparison.Ordinal);
    }

    private static string ExtractNonce(string csp)
    {
        var match = System.Text.RegularExpressions.Regex.Match(csp, "'nonce-([^']+)'");
        Assert.True(match.Success, "CSP should contain a nonce token: " + csp);
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Home_Page_Should_Send_ReportOnly_Csp_With_Nonce_Matching_Inline_Scripts()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy-Report-Only"));

        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' https: data:", csp, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));

        var nonce = ExtractNonce(csp);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"nonce=\"{nonce}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__CSP_NONCE__", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Csp_Nonce_Differs_Per_Request()
    {
        using var client = _factory.CreateClient();

        var first = ExtractNonce((await client.GetAsync("/")).Headers.GetValues("Content-Security-Policy-Report-Only").Single());
        var second = ExtractNonce((await client.GetAsync("/")).Headers.GetValues("Content-Security-Policy-Report-Only").Single());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Responses_Should_Send_Permissions_Policy_Allowing_Camera_For_Self()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var policy = Assert.Single(response.Headers.GetValues("Permissions-Policy"));

        Assert.Contains("camera=(self)", policy, StringComparison.Ordinal);
        Assert.Contains("microphone=()", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_Should_Not_Allow_Google_Tag_Manager_When_Analytics_Is_Unconfigured()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy-Report-Only"));

        Assert.DoesNotContain("googletagmanager.com", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("google-analytics.com", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_Should_Allow_Google_Tag_Manager_When_Analytics_Is_Configured()
    {
        using var gaFactory = _factory.WithWebHostBuilder(b => b.UseSetting("Analytics:GoogleAnalyticsId", "G-TEST123"));
        using var client = gaFactory.CreateClient();

        var response = await client.GetAsync("/");
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy-Report-Only"));

        Assert.Matches(@"script-src [^;]*https://www\.googletagmanager\.com", csp);
        Assert.Contains("https://*.google-analytics.com", csp, StringComparison.Ordinal);
        var nonce = ExtractNonce(csp);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, $"nonce=\"{System.Text.RegularExpressions.Regex.Escape(nonce)}\"").Count);
    }

    [Fact]
    public async Task Csp_Should_Be_Enforcing_When_ReportOnly_Is_Disabled()
    {
        using var enforcingFactory = _factory.WithWebHostBuilder(b => b.UseSetting("Security:ContentSecurityPolicyReportOnly", "false"));
        using var client = enforcingFactory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task Home_Page_Should_Not_Load_Google_Tag_Script_When_Id_Is_Unconfigured()
    {
        // appsettings.json ships an empty Analytics:GoogleAnalyticsId by default, and this
        // factory doesn't override it, so the served page must not make any request to
        // Google's tag manager (GH #92: no telemetry sent unless an operator opts in via
        // their own GA ID, set via appsettings or the Analytics__GoogleAnalyticsId env var).
        var factory = _factory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("googletagmanager.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("gtag(", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Implement_Analytics_Opt_Out_Logic()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/js/app.js?v=test");

        Assert.Contains("ANALYTICS_CONSENT_STORAGE_KEY", script, StringComparison.Ordinal);
        Assert.Contains("applyAnalyticsConsent", script, StringComparison.Ordinal);
        Assert.Contains("ga-disable-", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_Page_Should_Include_Stats_Button_And_Dialog_Structure()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        // Positive: stats entry point and dialog shell are present.
        Assert.Contains("id=\"statsBtnLoggedIn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsDialogTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"stats.dialogTitle\"", html, StringComparison.Ordinal);

        // Positive: stats dialog uses a tabbed layout with Summary and Book Lists panels.
        Assert.Contains("id=\"statsSummaryTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsBookListsTabBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsSummaryPanel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsBookListsPanel\"", html, StringComparison.Ordinal);

        // Positive: summary panel has chart canvas and ranked table.
        Assert.Contains("id=\"statsChartCanvas\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsTable\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsTableBody\"", html, StringComparison.Ordinal);

        // Positive: book-lists panel has unique-spun and never-spun sections.
        Assert.Contains("id=\"statsUniqueSpunList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"statsNeverSpunList\"", html, StringComparison.Ordinal);

        // Positive: admin aggregate section is present (hidden until admin unlocks it).
        Assert.Contains("id=\"statsAdminSection\"", html, StringComparison.Ordinal);

        // Positive: stats tabs are ARIA-wired (role, aria-controls).
        var dialogStart = html.IndexOf("id=\"statsDialog\"", StringComparison.Ordinal);
        var dialogMarkup = html.Substring(dialogStart, Math.Min(2000, html.Length - dialogStart));
        Assert.Contains("role=\"tablist\"", dialogMarkup, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"statsSummaryPanel\"", dialogMarkup, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"statsBookListsPanel\"", dialogMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Script_Should_Wire_Up_Stats_Dialog()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/js/app.js");

        // Positive: the stats button opens the stats dialog.
        Assert.Contains("statsBtnLoggedIn", script, StringComparison.Ordinal);
        Assert.Contains("statsDialog", script, StringComparison.Ordinal);

        // Positive: stat data is loaded from the stats API endpoints.
        Assert.Contains("/api/stats", script, StringComparison.Ordinal);
        Assert.Contains("statsContent", script, StringComparison.Ordinal);

        // Positive: summary panel renders key stat tiles and the chart.
        Assert.Contains("statsSummaryRow", script, StringComparison.Ordinal);
        Assert.Contains("statsChartCanvas", script, StringComparison.Ordinal);
        Assert.Contains("statsTableBody", script, StringComparison.Ordinal);

        // Positive: book-list panel renders unique-spun and never-spun lists.
        Assert.Contains("statsUniqueSpunList", script, StringComparison.Ordinal);
        Assert.Contains("statsNeverSpunList", script, StringComparison.Ordinal);

        // Positive: admin aggregate view is gated and displayed only for admins.
        Assert.Contains("statsAdminSection", script, StringComparison.Ordinal);
        Assert.Contains("/api/stats/aggregate", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_I18n_Should_Include_Stats_Strings_In_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/js/i18n.js");

        // Positive: all three locales carry stats dialog strings.
        Assert.Contains("dialogTitle: 'Wheel Stats'", script, StringComparison.Ordinal);
        Assert.Contains("totalSpins: 'Total Spins'", script, StringComparison.Ordinal);
        Assert.Contains("uniqueBooks: 'Unique Books Spun'", script, StringComparison.Ordinal);
        Assert.Contains("neverSpun: 'Never Spun'", script, StringComparison.Ordinal);
        Assert.Contains("typeBreakdownHeading: 'By Media Type'", script, StringComparison.Ordinal);

        // Positive: Spanish and Polish stats sections are present and translated.
        Assert.Contains("typeBreakdownHeading: 'Por tipo de soporte'", script, StringComparison.Ordinal);
        Assert.Contains("typeBreakdownHeading: 'Według formatu'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recent_Picks_Are_Reset_On_SignOut_And_Reloaded_On_SignIn()
    {
        // Regression guard for #263: Recent Picks and the last-spin line must be
        // scoped to the logged-in user. The API is owner-scoped (and covered by
        // Spin_History_Is_Isolated_Per_User); this pins the client behavior that no
        // prior account's picks linger across a sign-out / sign-in on the same tab.
        var factory = _factory;
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/js/app.js");

        // The reset helper exists and clears both the picks list and the last-spin line.
        Assert.Contains("function resetHomeView()", script, StringComparison.Ordinal);
        Assert.Contains("recentPicks = [];", script, StringComparison.Ordinal);

        // Sign-in refreshes this user's picks; sign-out clears the prior user's.
        Assert.Contains("await loadRecentPicks({ restoreSelection: true });", script, StringComparison.Ordinal);
        Assert.Contains("resetHomeView();", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_Should_Provide_Library_Search_Filter_Sort_And_PageSize_Controls()
    {
        // GH #180: the active book list has a client-side view layer (search, type filter,
        // sort, page size) persisted in localStorage.
        var factory = _factory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/index.html");
        var script = await client.GetStringAsync("/js/app.js");

        Assert.Contains("id=\"bookSearchInput\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"search\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookTypeFilter\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookSortSelect\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bookPageSizeSelect\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"bookSearchInput\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"bookTypeFilter\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"bookSortSelect\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"bookPageSizeSelect\"", html, StringComparison.Ordinal);

        Assert.Contains("function getVisibleBooks()", script, StringComparison.Ordinal);
        Assert.Contains("const DEFAULT_BOOK_SORT = 'title-asc'", script, StringComparison.Ordinal);
        Assert.Contains("BOOK_SEARCH_DEBOUNCE_MS = 200", script, StringComparison.Ordinal);
        Assert.Contains("'bookwheel.library.search'", script, StringComparison.Ordinal);
        Assert.Contains("'bookwheel.library.type'", script, StringComparison.Ordinal);
        Assert.Contains("'bookwheel.library.sort'", script, StringComparison.Ordinal);
        Assert.Contains("'bookwheel.library.pageSize'", script, StringComparison.Ordinal);
        Assert.Contains("lastSpunAtUtc", script, StringComparison.Ordinal);

        // Pagination and the count operate on the filtered view, not the raw list.
        Assert.Contains("books.countFiltered", script, StringComparison.Ordinal);
        Assert.Contains("books.noMatchesHeading", script, StringComparison.Ordinal);

        // Negative: the fixed page-size constant is gone.
        Assert.DoesNotContain("BOOKS_PER_PAGE;", script.Replace("DEFAULT_BOOKS_PER_PAGE", ""), StringComparison.Ordinal);

        var i18n = await client.GetStringAsync("/js/i18n.js");
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(i18n, "noMatchesHeading:").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(i18n, "countFiltered:").Count);
    }
}
