# Book Info Providers — Design

- **Issue:** [#70 — Book Info Providers](https://github.com/jasonkryst/BookWheel/issues/70)
- **Branch:** `fix/google-analytics-env-var` (to be renamed/branched for this work)
- **Date:** 2026-09-07

## Problem

Issue #70: *"Add Google Books API support. Pull same data as OpenLibrary/Hardcover. Add user preference for which API to use. Have a table which manages available APIs for book info. When a user adds a book have an FK to the book info source table as a FK."*

**Scoping note:** the issue's phrasing assumes a Hardcover integration already exists. It does not — `IBookMetadataLookupService` has exactly one implementation today, `OpenLibraryBookMetadataLookupService` (`BookWheel/Services/OpenLibraryBookMetadataLookupService.cs`), bound as the sole DI registration for the interface. This design treats the work as adding a **second** provider (Google Books) alongside OpenLibrary, built generically enough that a future Hardcover provider is a small addition, not a rework.

While scoping the "user preference" requirement, we found there is currently **no server-side user-preference storage at all** — theme and analytics-consent are `localStorage`-only (`BookWheel/wwwroot/js/app.js`). Per direction from the issue owner, this change also migrates those two existing preferences to server-side storage alongside the new provider preference, rather than building a one-off mechanism just for the provider choice.

## Scope

**In scope:**
- `GoogleBooksBookMetadataLookupService`, a second `IBookMetadataLookupService` implementation calling the Google Books API, keyless by default with an optional API-key override.
- `book_info_providers` lookup table (Open Library / Google Books), following this repo's established no-FK lookup-table convention (`book_types`).
- `books.BookInfoProviderId` (nullable) recording which provider actually supplied a given book's data; null for manually-entered books.
- Server-side per-user preferences: `Theme`, `AnalyticsConsentOptedOut`, `PreferredBookInfoProviderId`, added as columns on `users`, with new `GET`/`PUT /api/preferences` endpoints.
- A dispatcher that tries the user's preferred provider first and falls back to the other provider on failure/empty result.
- Frontend: provider-preference control in the Preferences settings panel; theme/analytics-consent read from and written to the server, with a `localStorage` cache used only to paint the correct theme before the pre-login auth check resolves.
- Test coverage for the new service, dispatcher, preferences endpoints, and provider stamping on book create/update.
- README and `docs/audits/database.md` updates.
- Minor version bump (`2.16.2` → `2.17.0`).

**Out of scope:**
- Hardcover integration (not present in the codebase; not part of this issue's real scope — see Scoping note above).
- A DB-level foreign-key constraint. This repo deliberately has none anywhere (`docs/audits/database.md`); the provider reference follows the same bare-int-column + index pattern as `BookTypeId` → `book_types`.
- Migrating any other localStorage-only UI state besides theme and analytics-consent (nothing else currently exists).
- Per-search provider override (e.g., an ad hoc dropdown at lookup time). The issue asks for a stored *preference*; the dispatcher always uses it.

## Architecture

```
BookWheel/
  Models/
    BookMetadataResult.cs              (+ ProviderId field)
    BookMetadataOptions.cs             (+ GoogleBooksApiKey)
    UpdateBookRequest.cs               (+ BookInfoProviderId, nullable)
    UpdatePreferencesRequest.cs        (new)
  Services/
    IBookMetadataLookupService.cs      (unchanged interface)
    OpenLibraryBookMetadataLookupService.cs   (unchanged; ProviderId = 1 on results)
    GoogleBooksBookMetadataLookupService.cs   (new; ProviderId = 2 on results)
    BookMetadataLookupDispatcher.cs    (new; plain class, not an IBookMetadataLookupService — see below)
  Controllers/
    BooksController.cs                 (lookup returns providerId; create/update accepts bookInfoProviderId)
    PreferencesController.cs           (new; GET/PUT /api/preferences)
  Storage/
    IUserPreferencesRepository.cs      (new; Theme/AnalyticsConsentOptedOut/PreferredBookInfoProviderId live on the same `users` row as credentials, but get their own small repository interface — consistent with this repo's one-interface-per-concern pattern — rather than extending ICredentialRepository, which JsonCredentialRepository also implements and has no legacy concept of preferences)
  Storage/Postgres/
    PostgresUserPreferencesRepository.cs (new)
    Entities/
      BookInfoProviderEntity.cs        (new)
      BookEntity.cs                    (+ BookInfoProviderId, nullable)
      UserEntity.cs                    (+ Theme, AnalyticsConsentOptedOut, PreferredBookInfoProviderId)
    BookWheelDbContext.cs              (new entity mapping + HasIndex additions)
  Migrations/
    <timestamp>_AddBookInfoProviders.cs (new)
  Program.cs                           (second AddHttpClient registration; keyed DI for the two providers; dispatcher wiring)
  wwwroot/
    index.html                         (new provider <select> in #settingsPreferencesPanel)
    js/app.js                          (server-sourced theme/consent/provider; localStorage as pre-login cache only)

BookWheel.Tests/
  Services/
    GoogleBooksBookMetadataLookupServiceTests.cs   (new)
    BookMetadataLookupDispatcherTests.cs           (new)
  Controllers/
    PreferencesControllerTests.cs                  (new)
  BookWheelFrontendTests.cs / integration fixtures  (updated for provider-aware fakes)

README.md                              (Features, API Overview, Testing sections)
docs/audits/database.md                (note new table/columns; still no FK)
BookWheel/BookWheel.csproj             (InformationalVersion 2.16.2 -> 2.17.0)
```

### Data model

`book_info_providers` (mirrors `book_types` exactly):

```csharp
public sealed class BookInfoProviderEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

Seeded via `HasData`: `{1, "Open Library"}`, `{2, "Google Books"}`.

`BookEntity` gains `public int? BookInfoProviderId { get; set; }` — nullable because manually-entered books (no lookup call was made) have no provider to record. Mapped with `entity.HasIndex(b => b.BookInfoProviderId)`, no `HasForeignKey`, consistent with `BookTypeId`.

`UserEntity` gains:
```csharp
public string? Theme { get; set; }
public bool AnalyticsConsentOptedOut { get; set; }
public int? PreferredBookInfoProviderId { get; set; }
```
`Theme` is nullable (no value = client picks its own default, same as today's `localStorage` absence). `AnalyticsConsentOptedOut` defaults `false`, matching current behavior (`localStorage.getItem(key) === 'false'` is only true when explicitly opted out). `PreferredBookInfoProviderId` is nullable; null means "use Open Library first" (the dispatcher's fixed default order), so existing users get identical behavior to today until they explicitly set a preference.

The migration (`AddBookInfoProviders`) follows `AddBookTypeTable`'s shape: `AddColumn` for the three new `users` columns and the nullable `books.BookInfoProviderId`, `CreateTable` for `book_info_providers`, `InsertData` for the two seed rows, `CreateIndex` on `books.BookInfoProviderId` and `book_info_providers.Name` (unique).

### Provider abstraction

`BookMetadataResult` gains `public int? ProviderId { get; set; }`, set by each provider implementation to its own `book_info_providers.Id` on every non-null/non-empty result.

`GoogleBooksBookMetadataLookupService` mirrors `OpenLibraryBookMetadataLookupService`'s shape: typed `HttpClient` (base address `https://www.googleapis.com/books/v1/`), ISBN lookup via `volumes?q=isbn:{isbn}`, title search via `volumes?q=intitle:{title}&maxResults={maxResults}`, mapping Google's `volumeInfo.{title,authors,industryIdentifiers,imageLinks.thumbnail}` into `BookMetadataResult`. Same defensive-degrade-to-null/empty behavior on `HttpRequestException`/`TaskCanceledException`/`JsonException`. Reads an optional API key from `BookMetadataOptions.GoogleBooksApiKey` (bound from `BookMetadata:GoogleBooks:ApiKey`, overridable by the `BookMetadata__GoogleBooks__ApiKey` env var — same override convention as `Observability:LogShipping:ApiKey`) and appends `&key=...` when present; omitted entirely when not configured, calling the public keyless endpoint.

`BookMetadataLookupDispatcher` is a plain class (not an `IBookMetadataLookupService` implementation itself — its methods need an extra `preferredProviderId` parameter that providers don't care about, so reusing the provider interface would pollute it). `Program.cs` registers both concrete providers normally (two typed `HttpClient` registrations, no keyed DI — this codebase doesn't use keyed services anywhere and two fixed providers don't need that machinery) and registers `BookMetadataLookupDispatcher` as a singleton whose constructor takes `OpenLibraryBookMetadataLookupService` and `GoogleBooksBookMetadataLookupService` directly, building an internal `Dictionary<int, IBookMetadataLookupService>` (`1` → Open Library, `2` → Google Books) keyed the same as `book_info_providers.Id`. `BooksController` is injected with `BookMetadataLookupDispatcher` directly (a concrete class, the same pattern as `AuthService`) instead of `IBookMetadataLookupService`, and resolves the current user's `PreferredBookInfoProviderId` itself (via the new preferences repository, see below) to pass into each call. Algorithm for both `LookupByIsbnAsync`/`LookupByTitleAsync` (each taking an added `int? preferredProviderId` parameter):
1. Try the preferred provider (or Open Library if no preference set).
2. If it throws internally it already degrades to `null`/empty per each provider's own contract — the dispatcher treats `null`/empty as "try the fallback."
3. Try the other provider.
4. Return whichever result is non-null/non-empty first; if both are empty, return `null`/empty with `ProviderId = null`.

`BooksController` resolves the authenticated user's preference itself (already has `_authService.GetAuthenticatedUser(HttpContext)`) and passes it into the dispatcher call — the dispatcher's methods gain a `preferredProviderId` parameter rather than reading it from ambient state, keeping it a plain, testable class.

### Preferences

`PreferencesController` (new, mirrors `AuthController`'s structure):
```csharp
[HttpGet]  // GET /api/preferences
[HttpPut]  // PUT /api/preferences, body: UpdatePreferencesRequest
```
Both require an authenticated user (401 otherwise, same pattern as `BooksController`). `UpdatePreferencesRequest`: `Theme` (string?, validated against the known theme set), `AnalyticsConsentOptedOut` (bool), `PreferredBookInfoProviderId` (`int?`, `[Range(1, 2)]` — `RangeAttribute` already skips validation when the value is null, so this accepts "no preference" while rejecting an unknown id; identical pattern to `UpdateBookRequest.BookTypeId`'s existing `[Range(1, 3)]`, chosen for consistency over a DB round-trip for a 2-row table).

`AuthenticatedUser` (the in-memory session record `AuthService` caches per login — see `AuthService.SessionRecord`) intentionally stays identity-only (`UserId`/`Username`/`IsAdmin`); adding preferences to it would mean keeping that cache in sync on every `PUT /api/preferences`, for no real benefit. Instead the frontend calls `GET /api/preferences` once as its own step right after `/api/auth/me` (bootstrap) or right after a successful login/setup — one extra fast same-origin call, always DB-fresh, no session-cache invalidation to get wrong.

### Book create/update

`BooksController`'s `lookup` action includes `providerId` in its JSON response (from `BookMetadataResult.ProviderId`) with no controller change needed — `BookMetadataResult` already serializes directly as the response body. `UpdateBookRequest` (used for both create and update — see existing `[Range(1,3)]` `BookTypeId` field) gains `public int? BookInfoProviderId { get; set; }` with `[Range(1, 2)]` (same nullable-skips-validation reasoning as the preferences field above). The frontend is the thing that threads a lookup result's `providerId` into the subsequent create/update call; a manually-typed entry simply omits it (`null`).

### Frontend

`index.html`: new `<select id="bookInfoProviderSelect">` in `#settingsPreferencesPanel`, alongside the existing theme selector and analytics-consent checkbox, with its two `<option>`s ("Open Library" / "Google Books", ids `1`/`2`) hardcoded directly in markup — the provider table is expected to change rarely, and every other lookup value in this UI (e.g. book types) is already hardcoded the same way rather than fetched.

`app.js`:
- On the pre-auth-check page load (`applyTheme(getPreferredTheme())`, `applyAnalyticsConsent()`), keep reading from `localStorage` exactly as today, unchanged — this is the pre-login cache path (it also gates the early Google Analytics opt-out flag) and must not require a network call.
- After authentication resolves (both the bootstrap `/api/auth/me` path and the login/setup submit handlers), call `GET /api/preferences` and apply the server's `theme`/`analyticsConsentOptedOut`/`preferredBookInfoProviderId` as the authoritative values, writing `theme` and `analyticsConsentOptedOut` back into `localStorage` so the *next* page load's pre-auth paint matches (per approved design: server is source of truth, localStorage is a same-device paint cache only).
- Preference control `change` handlers now call `PUT /api/preferences` (in addition to updating `localStorage`/DOM immediately for responsiveness), rather than writing to `localStorage` alone.
- Book add/edit flow: capture `providerId` from the `/api/books/lookup` response and include it as `bookInfoProviderId` when submitting the create/update request.

## Testing

- `GoogleBooksBookMetadataLookupServiceTests` — mirrors `OpenLibraryBookMetadataLookupServiceTests`: success, not-found, multi-author join, HTTP error status, malformed JSON, network failure; stubbed `HttpMessageHandler`, no real network calls.
- `BookMetadataLookupDispatcherTests` — preferred provider succeeds (no fallback call made); preferred provider throws/empty → fallback provider used and its `ProviderId` returned; both empty → null/empty result with `ProviderId = null`; no preference set → Open Library tried first.
- `PreferencesControllerTests` — GET returns current values (including defaults for a fresh user); PUT persists and is reflected on next GET; PUT with an unknown `preferredBookInfoProviderId` returns 400; unauthenticated requests return 401.
- Book create/update tests — `bookInfoProviderId` round-trips through create and update; omitted/`null` leaves it `null`; update to an unknown id is rejected (400).
- `FakeBookMetadataLookupService` (used by `BookWheelWebAppFactory` so integration tests never hit real provider APIs) becomes provider-aware or is joined by a second fake, so dispatcher-level integration tests can exercise both the preferred-provider and fallback paths without any real HTTP calls.
- `BookWheelFrontendTests.cs`-style checks: settings panel renders the new provider control; i18n completeness for its label across `en`/`es`/`pl`.

## Documentation

- `README.md`: Features bullet for Google Books support and provider preference; API Overview section documents `GET/PUT /api/preferences` and the `providerId`/`bookInfoProviderId` fields on the lookup/book endpoints; Testing section notes the new fakes.
- `docs/audits/database.md`: note the new `book_info_providers` table and the three new `users` columns, reaffirming they follow the existing no-FK, index-only convention documented there.
- `BookWheel/BookWheel.csproj`: bump `InformationalVersion` from `2.16.2` to `2.17.0` (new capability, no breaking change — minor bump per this project's semver convention).
