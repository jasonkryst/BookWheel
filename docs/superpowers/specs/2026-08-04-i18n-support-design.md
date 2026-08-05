# I18N Support — Design

- **Issue:** [#28 — I18N Support](https://github.com/jasonkryst/BookWheel/issues/28)
- **Branch:** `28`
- **Date:** 2026-08-04

## Problem

Issue #28: *"Add i18n support"*. No further detail was given in the issue. Clarified with the requester:

- **Scope:** both the frontend UI and the handful of server-generated error/validation messages the API returns.
- **Languages:** English (existing default), Spanish (`es`), Polish (`pl`).
- **Language selection:** a manual toggle in the UI, persisted per-browser, defaulting to the browser's language on first visit.

Today there is no i18n infrastructure at all: `index.html` is a single static page with hardcoded English text, `app.js` (~1600 lines) builds UI strings (toasts, confirmation dialogs, pagination text, etc.) as inline English template literals, and the API returns hardcoded English strings for `Unauthorized`/`BadRequest` responses (either literal strings or `ex.Message` from exceptions thrown in `Services`/`Storage`).

## Scope

**In scope:**
- A frontend translation-key system covering all user-facing UI text, with a language switcher available both pre-login (login/reset-password screens) and post-login (main app).
- Localization of the ~20 distinct English strings the API currently returns to the client in error/validation responses, driven by the same language choice via the `Accept-Language` header.
- Positive/negative test coverage for both layers, matching the existing test style in `BookWheel.Tests`.
- README documentation of the new capability.

**Out of scope:**
- Translating log messages, operator-facing diagnostics, or the `IMPROVEMENT_ROADMAP.md`/`SECURITY_AUDIT_REPORT.md` content itself — these are not end-user-facing.
- Right-to-left language support.
- Pluralization-rule engines beyond simple count-based branching already used today (e.g. "1 book" vs "N books").
- Professional/human-reviewed translation quality — Spanish and Polish strings are machine-authored by the assistant and should be treated as a first pass, callable out in the PR description.

## Architecture

```
BookWheel/
  Resources/
    SharedErrors.cs           (marker class for IStringLocalizer<SharedErrors>)
    SharedErrors.resx         (neutral/English — mirrors existing hardcoded text)
    SharedErrors.es.resx
    SharedErrors.pl.resx
  Services/
    ApiMessageLocalizer.cs    (maps known English exception/response text -> resx key -> localized string)
  Controllers/
    AuthController.cs, BooksController.cs, UsersController.cs
                               (inject ApiMessageLocalizer, wrap outgoing `message` strings)
  Program.cs                  (AddLocalization + UseRequestLocalization, supported cultures en/es/pl)

wwwroot/
  js/
    i18n.js                   (new — locale catalogs, t(), setLocale(), applyStaticTranslations())
    app.js                    (existing — replace hardcoded strings with BookWheelI18n.t() calls;
                                add Accept-Language header to the shared fetch wrapper)
  index.html                  (data-i18n / data-i18n-placeholder / data-i18n-aria-label attributes;
                                new persistent language-toggle button)
  css/site.css                (minor styling for the language toggle, reusing .icon-btn)
```

### Backend: message localization without touching throw sites

Exception messages are thrown as plain English strings today from `Storage`/`Services` (e.g. `throw new InvalidOperationException("Book not found.")`) and surfaced via `BadRequest(new { message = ex.Message })`. Rewriting every throw site to carry a resource key would touch ~10 files for little benefit, since the only place that needs to know about culture is the controller boundary where the JSON response is built.

Instead, `ApiMessageLocalizer` holds a static `Dictionary<string, string>` mapping each known English message (there are ~20, several repeated across call sites) to a resx key, and looks up the translation for the current request's `CurrentUICulture` (set automatically per-request by `UseRequestLocalization`). Any message not present in the dictionary — e.g. a future exception message that hasn't been catalogued yet, or `CorruptedDataException` operator text — passes through unchanged as English. This is a deliberate fail-open default: an untranslated string is a minor UX gap, not a broken response.

```csharp
public sealed class ApiMessageLocalizer(IStringLocalizer<SharedErrors> localizer)
{
    private static readonly Dictionary<string, string> KeysByEnglishMessage = new(StringComparer.Ordinal)
    {
        ["Invalid username or password."] = "InvalidCredentials",
        ["Book title is required."] = "BookTitleRequired",
        // ...~18 more entries, one per distinct message currently thrown/returned
    };

    public string Localize(string englishMessage)
    {
        if (!KeysByEnglishMessage.TryGetValue(englishMessage, out var key))
        {
            return englishMessage;
        }

        var result = localizer[key];
        return result.ResourceNotFound ? englishMessage : result.Value;
    }
}
```

`Program.cs` registers `AddLocalization(o => o.ResourcesPath = "Resources")`, configures `RequestLocalizationOptions` with `en` (default), `es`, `pl` as supported cultures, and calls `app.UseRequestLocalization(...)` early in the pipeline (before `MapControllers`). The built-in `AcceptLanguageHeaderRequestCultureProvider` reads the `Accept-Language` header — no cookie or query-string plumbing is needed since the frontend sends this header explicitly on every API call (see below), rather than relying on the browser's own language settings.

### Frontend: catalog file + data attributes

`i18n.js` is a new plain script (no bundler/module system is used anywhere else in this project, so this follows suit) loaded before `app.js`:

- `SUPPORTED_LOCALES = ['en', 'es', 'pl']`, `DEFAULT_LOCALE = 'en'`.
- `TRANSLATIONS = { en: {...}, es: {...}, pl: {...} }` — flat key/value catalogs, one entry per UI string (~90 keys: labels, buttons, aria-labels, titles, toast/validation messages, pagination and count text with `{n}`-style interpolation).
- `detectInitialLocale()` — `localStorage.getItem('bookwheel-locale')`, else the first supported locale matching `navigator.language`/`navigator.languages`, else `'en'`.
- `t(key, params)` — looks up `TRANSLATIONS[currentLocale][key]`, falls back to `TRANSLATIONS.en[key]`, then to the key itself; interpolates `{placeholder}` tokens from `params`.
- `setLocale(locale)` — validates against `SUPPORTED_LOCALES`, persists to `localStorage`, updates `document.documentElement.lang`, re-runs `applyStaticTranslations()`, and dispatches a `bookwheel:locale-changed` event so `app.js` can re-render any already-rendered dynamic content (book list, toasts in flight, dialog text).
- `applyStaticTranslations(root)` — walks `[data-i18n]` (sets `textContent`), `[data-i18n-placeholder]`, `[data-i18n-aria-label]`, `[data-i18n-title]` under `root` (defaults to `document`, but also called with a dialog's root after it's opened, in case content is templated).
- Exposed on `window.BookWheelI18n` for `app.js` to consume (no import/export — matches the existing global-script style).

`index.html` gains `data-i18n*` attributes on the ~50 static text elements, and one persistent language-toggle button (outside both `#loginView` and `#appView`, always visible) that cycles EN → ES → PL, mirroring the existing `themeToggleBtn` icon-button pattern but showing the 2-letter locale code as its glyph rather than an icon (a flag emoji is not a reliable per-language symbol and is an accessibility anti-pattern; the `aria-label` carries the full state, e.g. "Change language, current: English").

`app.js` changes:
- Its central `fetch` wrapper adds `'Accept-Language': BookWheelI18n.getCurrentLocale()` to every request so server error messages match the UI language automatically.
- Every hardcoded user-facing string (toasts, confirmation-dialog text, pagination `Page X of Y`, book-count summaries, "Last selected: …", empty states, client-side validation messages) is replaced with a `BookWheelI18n.t('key', {params})` call.
- On load, and on `bookwheel:locale-changed`, re-run whatever dynamic rendering functions currently exist (book list, wheel summary, pagination footer) so already-rendered text updates immediately without a page reload.

## Data Flow

1. On first load, `i18n.js` determines the locale (`localStorage` → browser language → `en`) and calls `applyStaticTranslations()` before `app.js` runs its own init, so the login screen renders in the right language even before authentication.
2. The user can change language at any time via the toggle button; the choice is persisted immediately and applied to both static and dynamic content without a reload.
3. Every `app.js` API call sends `Accept-Language: <locale>`. ASP.NET Core's `RequestLocalization` middleware sets `CurrentUICulture` for that request from the header.
4. When a controller returns an error `message`, it routes through `ApiMessageLocalizer.Localize(...)`, which returns the resx-backed translation for the active culture, or the original English text if the message isn't catalogued.

## Error Handling

- Unknown frontend translation keys render as the key itself (visibly wrong, easy to spot in review/tests) rather than throwing — a missing translation must never break the app.
- Unknown backend messages (not in `KeysByEnglishMessage`) pass through as English unchanged — same fail-open principle, and covers exception types not meant to reach end users verbatim (e.g. `CorruptedDataException`) without needing every future exception to be catalogued up front.
- An unsupported `Accept-Language` value falls back to the configured default culture (`en`) via `RequestLocalizationOptions.DefaultRequestCulture`.

## Testing

Matches the existing `BookWheel.Tests` convention of explicit `// Positive:` / `// Negative:` comments on `Assert.Contains`/`Assert.DoesNotContain` checks against served static content (see `Frontend_Styles_Should_Include_High_Contrast_Theme_Variables`), plus xUnit `[Fact]`/`[Theory]` unit tests for pure logic.

- **`ApiMessageLocalizerTests` (new):** positive cases for known messages under `es`/`pl`/`en` cultures returning distinct, correctly-localized text; a negative case for an unmapped message passing through unchanged under a non-English culture; a coverage test asserting every dictionary key has a non-empty translation in both `es.resx` and `pl.resx` (catches drift when a new message is added to the dictionary without translations).
- **API integration test (extends `BookWheelApiTests.cs`):** a bad-login request sent with `Accept-Language: es` returns the Spanish message text (positive) and does not return the English text (negative).
- **`BookWheelFrontendTests.cs` updates:** the handful of existing assertions that check for literal English strings which move out of `app.js` and into `i18n.js` (`"Book title is required."`, `"Last selected:"`, `"Create your Book Wheel account"`) are relocated to a new test that checks `i18n.js`. New tests assert `i18n.js` is served, contains all three locale catalogs with distinct Spanish/Polish text for representative keys (positive), and that `index.html` carries `data-i18n` attributes and the language-toggle button (positive) while the button's initial label is not itself a full untranslated English sentence (negative — it should be a short language code, not a leftover placeholder).

## Documentation

- `README.md`: new "Internationalization" subsection under Features describing supported languages, the toggle + persisted-preference behavior, and how the server localizes error messages via `Accept-Language`; add a short note under "How to add a new language" (add a resx pair + a `TRANSLATIONS` entry + add the locale to `SUPPORTED_LOCALES`/`RequestLocalizationOptions`).
- `IMPROVEMENT_ROADMAP.md`: add a bullet under "Current Strengths" noting language support is now available.

## Versioning

Bump `BookWheel/BookWheel.csproj`'s `InformationalVersion` from `1.7.0` to `1.8.0` (new feature, minor version per the project's existing convention) once implementation and tests pass.
