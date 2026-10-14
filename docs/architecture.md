# Architecture and Contributing

Solution layout, the API surface, and how the test suite and CI are organized.
For deployment and configuration, see the [Self-Hosting Guide](self-hosting.md).

## Solution Structure

```text
Book Wheel/
  BookWheel.slnx
  README.md
  BookWheel/
    BookWheel.csproj
    Program.cs
    appsettings.json
    App_Data/
    Controllers/
    Models/
    Services/
    Storage/
    wwwroot/
  BookWheel.Tests/
    BookWheel.Tests.csproj
    BookWheelApiTests.cs
    BookWheelWebAppFactory.cs
    Storage/
```

- Frontend behavior is implemented in `BookWheel/wwwroot/js/app.js`.
- The wheel UI and styles are in `BookWheel/wwwroot/index.html` and `BookWheel/wwwroot/css/site.css`.

## API Overview

Base route: `/api`

Auth endpoints:

- `GET /api/auth/status` — whether first-run setup is required
- `POST /api/auth/setup` — creates the initial account when no user account exists in the database
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `POST /api/auth/password-reset/request` — self-service; body `{ username }`; always returns `200` with a generic message
- `POST /api/auth/forgot-username` — self-service; body `{ email }`; always returns `200` with a generic message
- `POST /api/auth/password-reset/validate`
- `POST /api/auth/password-reset/complete`
- `GET /health/live`
- `GET /health/ready`

Reference endpoint (no authentication required — non-sensitive, needed to render the Preferences tab even when logged out):

- `GET /api/reference` — returns `bookTypes` (`id`, `name`, `icon`, `i18nKey` — `i18nKey` is `null` for a type with no built-in translation) and `bookInfoProviders` (`id`, `name`), read live from the `book_types`/`book_info_providers` tables. The frontend builds every book-type/provider `<select>` from this instead of hard-coding options.

Preferences endpoints (authentication required):

- `GET /api/preferences` — returns the authenticated user's `theme`, `analyticsConsentOptedOut`, `preferredBookInfoProviderId`, and `locale` (all `null`/`false` by default for a user who has never set one)
- `PUT /api/preferences` — replaces all four preference values in one call; `theme` must be `system`, `dark`, `light`, or `high-contrast` (or omitted/`null`), `preferredBookInfoProviderId` must be a known provider id (or omitted/`null`), and `locale` must be one of the supported language codes (`en`, `es`, `pl`, or omitted/`null`)

Operational endpoint (admin only):

- `GET /api/metrics`

User-management endpoints — see the [Admin Guide](admin-guide.md#managing-users).

Migration utility endpoints — see the [Admin Guide](admin-guide.md#legacy-data-migration-utility).

Book endpoints (authentication required):

- `GET /api/books` — returns the authenticated user's active books ordered by `CreatedAtUtc` (then `Id`); each book carries `lastSpunAtUtc` (newest spin timestamp for that book, `null` if never spun)
- `POST /api/books`
- `PUT /api/books/{id}`
- `DELETE /api/books/{id}`
- `POST /api/books/spin`
- `GET /api/books/spin-history` — returns the authenticated user's own spin selections, newest first
- `GET /api/books/lookup?isbn={isbn}` or `GET /api/books/lookup?title={title}` — queries the authenticated user's preferred book-info provider (Open Library or Google Books, falling back to the other provider if the preferred one has no match) for a book's title, author, ISBN, and cover URL, plus which provider answered (`providerId`); returns `404` when nothing matches and `400` when neither `isbn` nor `title` is supplied or the ISBN fails checksum validation

Stats endpoints (authentication required):

- `GET /api/stats` — returns per-user spin analytics: total spins, unique books spun, never-spun count, longest/shortest time a book has been on the wheel (derived from `CreatedAtUtc`), a ranked top-books list with spin counts and percentages, and the full list of never-spun active books
- `GET /api/stats/aggregate` — administrator only; returns cross-user totals: total spins across all users, total user count, active user count (not disabled/locked, with a login or spin in the last 30 days), and top users by spin count

`POST /api/books` and `PUT /api/books/{id}` accept optional `isbn`, `author`, `coverUrl`, and `bookInfoProviderId` fields alongside the required `title`. A supplied `isbn` is validated (ISBN-10 or ISBN-13, hyphens/spaces ignored) and rejected with `400` if it fails checksum validation.

`DELETE /api/books/{id}` soft-deletes the book (sets a `DeletedAtUtc` timestamp) instead of removing the row. Soft-deleted books are excluded from `GET /api/books`, spin selection, and the `totalBookCount` metric, and re-deleting or updating an already-deleted book returns `404`. Soft-deleted books can be listed via `GET /api/books/deleted` and brought back with `POST /api/books/{id}/restore` (both owner-scoped), which also restores the book with its spin history intact. A full user-account removal (`DELETE /api/users/{id}`) still hard-deletes all of that user's books, including any already soft-deleted, along with their spin history.

`POST /api/books/spin` records a row (user, book, timestamp) in the spin-history log every time it selects a book, readable back via `GET /api/books/spin-history`. Spin-history entries still show the book's title even if that book is later soft-deleted.

## Testing

Run all tests:

```bash
dotnet test BookWheel.slnx
```

Run only the test project:

```bash
dotnet test BookWheel.Tests/BookWheel.Tests.csproj
```

Current integration tests cover:

- First-run account setup
- Auth protection for books endpoint
- Login and access to protected endpoints after setup
- First-user administrator assignment and admin-only user-management access control
- Admin create/update/delete user flows
- Password reset link generation and one-time token completion flow
- Admin user removal flow with first-account protection
- User removal cascade cleanup for user-scoped books
- Book list isolation across different users
- Spin behavior preserving active book count
- Book update and remove flow
- Book soft delete: excluded from the active list, spin selection, and total book count after removal; re-deleting or updating an already-removed book returns `404`; a full user-account removal still hard-purges all of that user's books, including any already soft-deleted
- Spin-history recording and retrieval: each spin persists a user/book/timestamp entry, the read endpoint requires authentication, returns entries newest-first, keeps a book's title even after that book is later soft-deleted, and is isolated per user
- ISBN validation (valid/invalid ISBN-10 and ISBN-13, with and without separators), ISBN/author/cover persistence on add and edit, and the `/api/books/lookup` endpoint's success, not-found, and validation-error responses
- Title lookup requests Open Library's general search parameter (`q=`) rather than the strict title-field parameter, so titles not indexed verbatim still return matches
- Book info provider preference: `/api/books/lookup` uses the authenticated user's preferred provider (Open Library or Google Books) and falls back to the other provider when the preferred one has no match; `POST`/`PUT /api/books` persist which provider actually supplied a book's data (`null` for manual entries); `GET`/`PUT /api/preferences` round-trip theme, analytics-consent, and provider preference, rejecting unknown values with `400`
- Spin selection response includes author/cover when the selected book has that metadata, and omits them (rather than erroring) when it doesn't
- Security regression checks for encrypted credential storage, failed-login audit logging, and rate limiting
- Proxy-aware rate-limit behavior using forwarded client IP headers
- Corrupt/missing data file handling with quarantine and recovery responses
- Health check behavior for writable and unhealthy storage scenarios
- Metrics endpoint behavior and access control
- Container and startup smoke checks for runtime paths and health probes
- Persistent log file creation and structured audit logging checks
- CI dependency-audit gate (`scripts/check-vulnerable-packages.sh`): passes clean `dotnet list --vulnerable` output through unchanged and exits 0, and exits 1 when the report contains a vulnerable-packages finding
- PWA manifest, icon, and service-worker behavior, including that `/api/*` requests are never intercepted or cached by the service worker
- Spin Wheel Stats endpoint access control: `GET /api/stats` requires authentication (401 for unauthenticated callers); `GET /api/stats/aggregate` additionally requires admin (403 for non-admin authenticated users)
- Stats data correctness: total-spin count, unique-books-spun count, and never-spun count after add/spin/delete sequences; top-books list is ordered by spin count descending; percentage values sum to 100; soft-deleted books preserve their spin history contribution but are excluded from the never-spun list; multi-user aggregate totals and top-user ranking reflect cross-user spin activity; the aggregate's active-user count only includes users who are not disabled/locked and who logged in or spun within the last 30 days, verified against a disabled user with recent activity and a never-active user

Frontend-focused tests also verify that the HTML, JavaScript, and CSS expose the account setup mode, selected-book UI (including the cover/author shown alongside the "Last selected" title), pagination summary, delete confirmation flow, logout form reset behavior, icon-based dark/light/high-contrast theme toggle behavior, the consolidated Settings dialog's tab structure and visibility rules, file-based import/export behavior, and the ISBN lookup controls (input, Lookup button, cover/author preview) on both the add-book row and the edit dialog. Additional checks confirm the spin-result highlight, ambiguous-match toast, and lookup-picker rows derive their color from the active theme's CSS variables rather than a fixed dark-mode color.

Integration tests never call the real Open Library or Google Books APIs: `BookWheelWebAppFactory` substitutes a `BookMetadataLookupDispatcher` built from two deterministic `IBookMetadataLookupService` fakes (one per provider), and `OpenLibraryBookMetadataLookupServiceTests`/`GoogleBooksBookMetadataLookupServiceTests` exercise each provider's real HTTP-parsing logic against a stubbed `HttpMessageHandler` (success, not-found, malformed JSON, and network-failure cases). `BookMetadataLookupDispatcherTests` covers the preferred-provider/fallback selection logic in isolation, with no HTTP involved at all.

The frontend also includes import/export interactions (a JSON tabbed panel inside the Settings dialog) and wheel shuffle behavior when books are added.

## Development Notes

- The test host uses a temporary content root so tests do not mutate real app data.
- The test host also mirrors `wwwroot` into a temporary folder so frontend behavior is exercised against real static assets.
- The test host captures structured logs so security audit events can be asserted in tests.
- The test host also verifies that log entries are written to persistent JSONL files in the temp `App_Data/logs` folder.
- CI runs build, full tests, vulnerability scans, security-focused regressions, smoke tests, and Docker startup verification.
- CI also runs secret scanning via gitleaks and workflow linting via actionlint to prevent accidental token/credential commits and malformed workflow changes.
- Trivy container scanning runs on every CI build, applying a hard failure gate for fixable CRITICAL/HIGH vulnerabilities and uploading a full SARIF report (all severities, including unfixed) to the GitHub Security tab.
- CodeQL static analysis (`.github/workflows/codeql.yml`) scans .NET source code for security vulnerabilities on every push and PR to `main`, and on a weekly Monday schedule. Results appear alongside Trivy findings in the GitHub Security → Code Scanning tab.
- Dependabot (`.github/dependabot.yml`) scans NuGet packages and GitHub Actions versions weekly, opening up to 5 PRs per ecosystem when updates are available.
- EF Core packages (`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`) are on the 9.x line; `Microsoft.EntityFrameworkCore`/`Microsoft.EntityFrameworkCore.Relational` are pinned explicitly in `BookWheel.csproj` to keep both in lockstep, since the Npgsql provider and the Design tooling can otherwise resolve to different patch versions and fail to build (`CS1705`).
- CI enforces a per-ref concurrency group (newer pushes cancel in-progress runs for the same branch/PR) and per-job timeouts, and Docker layer builds are cached via GitHub Actions cache (`type=gha`).

## Project Documents

Additional project documentation is available in:

- [CHANGELOG.md](../CHANGELOG.md) for the GitHub-issue-numbered feature history
- `SECURITY_AUDIT_REPORT.md` for the latest audit summary, findings, and remediation priorities
- `IMPROVEMENT_ROADMAP.md` for a forward-looking roadmap covering security, UX, operations, and product enhancements
