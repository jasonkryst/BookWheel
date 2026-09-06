# BookWheel Performance Audit

**Scope:** Frontend load performance (Chrome DevTools MCP traces + Lighthouse), network/caching/compression behavior, service worker caching strategy, backend/database query patterns, indexes, connection pooling, response payload shape, and Docker image size.
**Date:** 2026-09-04
**Environment:** Live instance at `http://localhost:32700` via `docker-compose` (containers `bookwheel`, `bookwheel-postgres`), not restarted during this audit.
**Test data:** Dedicated non-admin account `perfuser`, seeded with 18 books via `POST /api/books` (mix of bare titles and fuller records with ISBN/author/cover) to exceed the 10-book pagination threshold and populate the wheel realistically. No other test accounts (`audituser`, `a11yuser`) were modified — the browser's persistent profile was already authenticated as `a11yuser` when testing began; that session was logged out (no writes performed under it) before switching to `perfuser`.
**Method:** `performance_start_trace`/`performance_analyze_insight`/`lighthouse_audit` via chrome-devtools-mcp against both the pre-login screen and the logged-in app with 18 books; direct HTTP timing/header inspection via PowerShell and `curl`; static review of `Program.cs`, `Storage/Postgres/*`, `Dockerfile`, `wwwroot/sw.js`, `wwwroot/index.html`; live schema inspection via `docker exec bookwheel-postgres psql \di` / `\d+`.

---

## Executive Summary

BookWheel is fast in absolute terms today — LCP was 162ms on the login screen and 259ms on the logged-in app with 18 books, measured locally with no network latency — but that speed is achieved despite the delivery pipeline, not because of it, and the margin evaporates under realistic network conditions. Three infrastructure-level gaps stack on top of each other: **no response compression** (`app.js`/`i18n.js`/`site.css` transfer uncompressed even though the client sends `Accept-Encoding: gzip, br` on every request — gzip alone would cut the combined ~156KB JS/CSS payload to ~34KB, a 79% reduction, confirmed by compressing the live responses locally); **no minification/bundling step** anywhere in the build (`Dockerfile` just runs `dotnet publish`, which copies `wwwroot` verbatim — three unminified source files ship as-is); and a **caching-headers bug** where `Program.cs`'s `UseStaticFiles` middleware applies the exact same `Cache-Control: no-cache, no-store, must-revalidate` to every static asset that it correctly applies to `index.html` — even though those assets are already cache-busted via a `?v=2.13.0` query string specifically so they *could* be cached indefinitely. The `?v=` scheme's entire benefit is currently thrown away. Simulating a Fast 3G connection with 4x CPU throttling (a reasonable proxy for a phone on a home network) demonstrates the compounding cost concretely: LCP rose from 259ms to **1,843ms** — a 7x regression driven mostly by TTFB and render delay against uncompressed, non-cached payloads.

On the backend, query patterns are reasonable at BookWheel's current scale (18 books, one user under test) but contain avoidable inefficiencies that will matter as data grows: `AsNoTracking()` is never used on any read-only EF Core query across the entire `Storage/Postgres` layer; `POST /api/books/spin` fetches the user's full book list from the database twice in a single request (once inside `SelectRandomAsync` to pick a random book in C#, again via `GetAllAsync` to build the response); `GET /api/stats` issues five separate round trips where two or three would do; and `GET /api/books` doubles its own response payload by returning the identical book array twice under two different JSON keys (`books` and `activeBooks`). The soft-delete filter (`DeletedAtUtc IS NULL`) applied to every books query has no supporting index — this is also flagged in `docs/audits/database.md` from a data-integrity angle; this report reconfirms it as a performance concern for the same underlying gap. None of this is urgent at 18 rows; all of it is straightforward to fix before it becomes urgent.

**Overall assessment: a snappy small-scale app whose frontend delivery pipeline (compression, minification, cache headers) is the highest-leverage fix — cheap, mechanical, and worth the most milliseconds — with a handful of low-risk backend query consolidations behind it.**

---

## Findings

### Frontend / Network

#### High severity

**F1. No HTTP response compression configured anywhere.**
`Program.cs` never calls `AddResponseCompression`/`UseResponseCompression`, and no reverse proxy sits in front of Kestrel in this deployment. Confirmed directly: requesting `/js/app.js`, `/js/i18n.js`, and `/css/site.css` with `Accept-Encoding: gzip, deflate, br` returns 200 with **no `Content-Encoding` header at all** — the server ignores the client's stated compression support and sends raw bytes. `GET /api/books` and `GET /api/stats` JSON responses are likewise sent uncompressed.
- Measured impact: compressing the three static files locally with standard gzip (optimal level) shows:
  | File | Raw bytes | Gzip bytes | Reduction |
  |---|---|---|---|
  | `js/app.js` | 90,961 | 17,671 | 80.6% |
  | `js/i18n.js` | 41,703 | 10,630 | 74.5% |
  | `css/site.css` | 27,300 | 5,512 | 79.8% |
  | **Total** | **159,964** | **33,813** | **78.9%** (126KB saved per full load) |
- This is a single-line fix (`builder.Services.AddResponseCompression(...)` + `app.UseResponseCompression()`) with essentially no downside for a text-heavy app like this.

**Status (2026-09-06): Fixed.** `Program.cs` now calls `AddResponseCompression`/`UseResponseCompression` with `EnableForHttps = true` and an explicit MIME-type list covering `application/javascript`/`application/manifest+json` in addition to the framework defaults. Live-verified with a new regression test (`Static_Js_Response_Is_Compressed_When_Client_Accepts_Gzip`).

**F2. Static assets and `index.html` are served with identical `no-store` cache headers, defeating the app's own cache-busting scheme.**
`Program.cs` sets `Cache-Control: no-store, no-cache, must-revalidate` (plus `Pragma: no-cache`, `Expires: 0`) in three places: on the hand-written `index.html`/`sw.js` responses (lines ~260, ~274 — correct, index.html must always revalidate so users pick up new asset versions), and again inside `UseStaticFiles`'s `OnPrepareResponse` callback (~line 293), which applies to **every** file served from `wwwroot` — `js/app.js`, `js/i18n.js`, `css/site.css`, icons, the manifest. Confirmed via response headers on all three JS/CSS files and via the DevTools performance trace's own "Use efficient cache lifetimes" insight.
- `index.html` itself references these assets with a cache-busting query string — `css/site.css?v=2.13.0`, `js/i18n.js?v=2.13.0`, `js/app.js?v=2.13.0` — which exists specifically so that versioned URLs *can* be cached with a long `max-age` and `immutable`, because a new deploy changes the URL and therefore the cache key. Right now every one of those requests instead gets `no-store`, meaning **the browser HTTP cache never stores them at all**, and even the weaker fallback of a 304-via-ETag revalidation (an `ETag` header is present) is defeated by `no-store` taking precedence. Every navigation — not just every deploy — re-downloads all three files in full.
- Fix: give `UseStaticFiles` its own `OnPrepareResponse` logic that sets `Cache-Control: public, max-age=31536000, immutable` for anything under `/js/`, `/css/`, `/icons/` (all of which are versioned via `?v=`), and reserve the current `no-store` treatment for `index.html`/`sw.js` only (which is already handled by separate `MapGet` handlers, not `UseStaticFiles`, so this is a clean split).

**Status (2026-09-06): Fixed.** `UseStaticFiles`'s `OnPrepareResponse` now sets exactly `Cache-Control: public, max-age=31536000, immutable` and nothing else; `index.html`/`sw.js` are untouched (still `no-store` via their own `MapGet` handlers). Live-verified with two new regression tests (`Static_Assets_Are_Served_With_Long_Lived_Immutable_Cache_Headers`, `Index_Html_Still_Uses_No_Store_Cache_Headers`).

#### Medium severity

**F3. No JS/CSS minification or bundling step in the build or Docker image.**
`Dockerfile` (repo root) runs `dotnet publish BookWheel/BookWheel.csproj -c Release -o /app/publish`, which copies `wwwroot` into the publish output unchanged — there is no `esbuild`/`terser`/`cssnano` step, and `scripts/` contains only `check-vulnerable-packages.sh` and `generate-pwa-icons.py`, neither of which touches JS/CSS. `app.js` (90,961 bytes), `i18n.js` (41,703 bytes), and `site.css` (27,300 bytes) ship as raw, human-formatted source with full variable names and comments — confirmed by reading the live response bodies.
- Minification alone (before gzip) typically saves 20-40% on JS/CSS of this style; combined with F1's compression the two together are why the "Total raw → gzip" table above still understates the achievable reduction — a minified-then-gzipped `app.js` would be smaller still.
- This is lower urgency than F1/F2 because the files are small in absolute terms (under 100KB each) and the app is self-hosted for a small number of users, but it's a straightforward addition to the existing Docker build stage (e.g., an `npm`/`esbuild` layer before `dotnet publish` copies `wwwroot`).

**F4. Service worker precache list uses unversioned URLs that the app never actually requests.**
`wwwroot/sw.js`'s `SHELL_ASSETS` array precaches `/css/site.css`, `/js/app.js`, `/js/i18n.js` (no query string) on `install`, but `index.html` requests them as `css/site.css?v=2.13.0` etc. — a different cache key. `Cache.match()` does exact URL matching by default, so **the precached entries are never hit by a real page load**; they're pure wasted bandwidth/storage on install, and the runtime `fetch` handler's stale-while-revalidate logic (cache-first, with a background network refresh) independently caches the *versioned* URL the first time it's actually requested. Confirmed in the DevTools trace: on reload, `app.js`/`i18n.js`/`site.css` all show `fromServiceWorker: true`, so the SW is functioning for repeat visits within a deployed version — it's just doing so via the lazily-populated versioned entries, not the precached unversioned ones the install step deliberately fetched.
- Net effect: not broken, but half of what `install` downloads is dead weight, and the "app works offline immediately after first visit" guarantee the precache step is meant to provide doesn't actually hold for CSS/JS (only for whatever happens to already be cached from a prior navigation).
- The service worker is also the one thing currently compensating for F2 on repeat visits — its `cache.put` calls store the response in the Cache Storage API independent of the `Cache-Control` header, so returning users with the SW active get cache-first CSS/JS even though the server says not to cache. This only helps on repeat visits with the SW already installed and active; it doesn't help first visits, non-PWA contexts, or any client that doesn't run the SW's fetch handler exactly as written.
- Fix: either template `SHELL_ASSETS` with the same `__ASSET_VERSION__` substitution `sw.js` itself already receives (so precache keys match real request keys), or drop CSS/JS from the precache list entirely and rely on the existing runtime cache-first behavior.

#### Low severity / informational

**F5. Render-blocking CSS/JS add a measurable but small slice to LCP.**
The DevTools "RenderBlocking" insight on the logged-in trace flagged `app.js?v=2.13.0`, `i18n.js?v=2.13.0`, and `site.css?v=2.13.0` as render-blocking with an estimated 115ms combined savings on FCP/LCP if deferred or inlined. In absolute terms this is minor next to F1/F2 (115ms out of a 259ms LCP locally), but it compounds with them under real network latency — see the Fast 3G measurement below, where the same requests contribute far more of the 1,843ms LCP. Not worth restructuring script loading order on its own; worth revisiting once F1/F2/F3 are fixed and the remaining bottleneck is re-measured.

**F6. Google Analytics tag load fails in this environment.**
`https://www.googletagmanager.com/gtag/js?id=G-JQXW826H1F` fails with `net::ERR_ADDRESS_INVALID` in the test environment (likely a DNS/network restriction specific to this sandbox, not an app bug). Recorded for completeness; not a BookWheel defect and not scored as a finding.

---

### Backend / Database

#### Medium severity

**B1. `AsNoTracking()` is never used on any read-only query.**
Verified with a full grep across `BookWheel/Storage/Postgres/*.cs`: zero matches. `PostgresBookRepository.GetAllAsync`, `GetAllForExportAsync`, `SelectRandomAsync`; `PostgresSpinStatsRepository.GetForUserAsync`, `GetAggregateAsync`; `PostgresSpinHistoryRepository.GetForUserAsync` — every one of these is a pure read whose results are immediately projected into a DTO (`BookRecord`, `SpinStatsRecord`, etc.) and discarded, yet EF Core's change tracker sets up full tracking infrastructure (snapshot copies, identity map entries) for each entity returned. At 18 rows this is unmeasurable; at hundreds or thousands of books per user (or many concurrent users) it's needless CPU and allocation on every list/spin/stats request. Low-risk, mechanical fix — add `.AsNoTracking()` to each of these query chains.

**B2. `POST /api/books/spin` fetches the user's full book list from the database twice.**
`BooksController.Spin()` calls `_store.SelectRandomAsync(user.UserId)`, which internally does `context.Books.Where(b => b.UserId == userId).ToListAsync()` and picks a random element in C# — then, a few lines later, calls `_store.GetAllAsync(user.UserId)` again to build the `activeBooks` field of the response, issuing the *same* query a second time. For a user with N active books, spinning the wheel costs two full table scans of that user's books plus the spin-history insert, when one fetch (reused for both the random pick and the response) would do. Confirmed by reading `BooksController.cs:284-313` and `PostgresBookRepository.cs:73-84`.

**B3. `GET /api/stats` issues five separate database round trips that could be consolidated.**
`PostgresSpinStatsRepository.GetForUserAsync` runs, in sequence: (1) `COUNT` of spin selections, (2) `COUNT(DISTINCT BookId)` of spins, (3) a full fetch of the user's active books (id/title/createdAt), (4) a `DISTINCT` fetch of all spun book IDs (largely redundant with query 2 — same predicate, same column, just not counted), and (5) a grouped join of spins to books for the top-books/spin-count breakdown. Measured live: first call after container idle took ~770-960ms (JIT/connection warm-up), steady-state repeat calls settled to ~70-110ms — acceptable today, but five round trips per stats view is more network/latency overhead than necessary. Queries (2) and (4) in particular compute overlapping information from the same table and predicate and could share one fetch; queries (1)/(2) could likely be combined into a single grouped aggregate.

**B4. `GET /api/books` response duplicates its entire payload under two JSON keys.**
`BooksController.GetAll()` returns `new { books, activeBooks = books.ToList() }` — both fields are the exact same list, serialized twice. Confirmed via a live response with 18 test books: the JSON body is 12,491 bytes for what is, in substance, ~6.2KB of unique book data. This roughly doubles the transfer size (and JSON parse cost) of every `GET /api/books` call for no apparent functional reason — `POST /api/books/spin` returns the same redundant shape (`selected` + `activeBooks`, where `activeBooks` duplicates what a plain books list would already contain client-side). If the frontend genuinely needs both an "all books" and an "active books" view and they're expected to diverge (e.g., once soft-deleted books are ever included in one but not the other), that's a reasonable API shape — but today they're identical, so it's worth confirming whether `activeBooks` is actually consumed differently by `app.js`, and if not, dropping it.

**B5. Soft-delete filter column (`DeletedAtUtc`) has no supporting index; only a single-column index exists on `UserId`.**
Confirmed live via `\d+ books`: indexes exist on `Id` (PK), `UserId`, `BookTypeId`, `CreatedByUserId`, `LastUpdatedByUserId` — none on `DeletedAtUtc`. Every books query goes through EF Core's global query filter (`HasQueryFilter(b => b.DeletedAtUtc == null)`, `BookWheelDbContext.cs:44`) ANDed with `UserId == :id`, so the effective predicate on every list/spin query is `WHERE "UserId" = @p0 AND "DeletedAtUtc" IS NULL`. The existing single-column `UserId` index lets Postgres narrow to one user's rows efficiently; filtering out soft-deleted rows within that set is then a cheap in-memory filter at today's per-user row counts. This is immaterial at 18 rows and is already documented from the data-integrity/indexing angle in `docs/audits/database.md`; flagged here too because it's the same underlying gap and it *is* a real query-performance question once a user's book count (or the deleted-row backlog, which is never purged) grows meaningfully. A composite index on `(UserId, DeletedAtUtc)` would let Postgres satisfy the whole predicate from the index rather than a user-scoped scan plus filter.

#### Low severity / informational

**B6. Npgsql connection pooling left at library defaults.**
The connection string (`Host=postgres;Database=bookwheel;Username=bookwheel;Password=...` in `docker-compose.yml`) sets no `Pooling`, `Minimum Pool Size`, `Maximum Pool Size`, or `Connection Idle Lifetime` parameters, and `Program.cs` registers the context via `AddPooledDbContextFactory` (EF Core-level context pooling) without customizing the underlying Npgsql connection pool. Npgsql's defaults (`Pooling=true`, `MaxPoolSize=100`, `MinPoolSize=0`) are almost certainly fine for a single-container, small-user-count deployment like this one — noted for completeness, not a recommended change at current scale.

**B7. Cold-start latency on the first request to a given endpoint after container idle.**
Observed directly: the first `GET /api/books` after the container had been idle took 509ms; five immediate repeats settled to 93-115ms. The first `GET /api/stats` took 772-960ms across two separate cold measurements; repeats settled to 68-109ms. The first `POST /api/books/spin` took 790ms; repeats settled to 94-138ms. This pattern (one slow request, then fast) is consistent with .NET JIT warm-up and/or first-use Npgsql connection/prepared-statement setup rather than a sustained per-request cost, and isn't unusual for a small self-hosted ASP.NET Core app — noted as a characteristic to expect after each deploy or restart, not a bug.

**B8. Docker image size.**
`docker images` shows the `bookwheel:latest` image at 343MB, built from `mcr.microsoft.com/dotnet/aspnet:8.0` (Debian-based). Switching the final stage to an Alpine-based ASP.NET Core runtime image (`mcr.microsoft.com/dotnet/aspnet:8.0-alpine`) would shrink this meaningfully (typically to well under 150MB) at the cost of using musl libc, which occasionally surfaces subtle native-dependency differences. Purely informational — image size affects pull/deploy time, not runtime request performance, and 343MB is unremarkable for a non-Alpine .NET 8 image.

---

## Metrics captured

### Frontend (Chrome DevTools MCP, unthrottled, local network)

| Scenario | LCP | TTFB | Render delay | CLS |
|---|---|---|---|---|
| Pre-login screen | 162ms | 40ms | 122ms | 0.03 |
| Logged-in app, 18 books | 259ms | 127ms | 132ms | 0.03 |

### Frontend under simulated real-world conditions (Fast 3G + 4x CPU throttle, logged-in app)

| Metric | Value |
|---|---|
| LCP | 1,843ms (up from 259ms unthrottled — a 7.1x regression) |
| TTFB | 603ms |
| Render delay | 1,240ms |
| CLS | 0.01 |

### Static asset sizes and compression opportunity

| File | Raw (as served) | Gzip (computed locally) | `Content-Encoding` header on live response |
|---|---|---|---|
| `js/app.js` | 90,961 B | 17,671 B (-80.6%) | *(none)* |
| `js/i18n.js` | 41,703 B | 10,630 B (-74.5%) | *(none)* |
| `css/site.css` | 27,300 B | 5,512 B (-79.8%) | *(none)* |
| **Total JS+CSS** | **159,964 B** | **33,813 B (-78.9%)** | *(none)* |

### API response timing (perfuser, 18 books, warm vs. cold)

| Endpoint | First call (cold) | Steady-state (5 calls) | Response size |
|---|---|---|---|
| `GET /api/books` | 509ms | 93-115ms | 12,491 B (18 books, duplicated `books`+`activeBooks`) |
| `GET /api/stats` | 772-960ms | 68-109ms | 1,655 B |
| `POST /api/books/spin` | 790ms | 94-138ms | ~6,590-6,604 B |
| `GET /api/stats/aggregate` | — | 403 Forbidden (correct: `perfuser` is non-admin) | — |

### Backend/database schema

- Indexes present (live `\di`): `books` → `UserId`, `BookTypeId`, `CreatedByUserId`, `LastUpdatedByUserId` (+ PK); `spin_selections` → `UserId`, `BookId` (+ PK); `users` → `Username` (unique); `book_types` → `Name` (unique); `password_reset_tokens` → `TokenHash` (unique), `UserId`.
- No index on `books.DeletedAtUtc` (the soft-delete filter column applied to every books query).
- `AsNoTracking()`: 0 occurrences across `Storage/Postgres/*.cs`.

### Lighthouse (non-performance categories, desktop, navigation mode, logged-in app)

| Category | Score |
|---|---|
| Accessibility | 97 |
| Best Practices | 96 |
| SEO | 91 |
| Agentic Browsing | 100 |

(Performance category is intentionally excluded from `lighthouse_audit`'s scope in this tooling; the trace-based LCP/TTFB/CLS metrics above are the performance measurements for this audit.)

### Docker image

- `bookwheel:latest`: 343MB (built on `mcr.microsoft.com/dotnet/aspnet:8.0`).

---

## Positive Observations

- **LCP is genuinely fast in absolute terms** (162-259ms unthrottled) — the app has no heavyweight framework, no large hero images, and a small DOM, so the actual rendering work is minimal once bytes arrive.
- **The service worker's runtime caching strategy already mitigates the server's overly aggressive `no-store` headers for repeat visits.** Its `cache.put` calls populate the Cache Storage API independent of `Cache-Control`, and the trace confirmed `app.js`/`i18n.js`/`site.css` were served `fromServiceWorker: true` on reload — a real, working safety net that softens the impact of F2 for any returning user with the SW active, even though it doesn't fix the underlying header bug.
- **The SW's cache-name versioning (`bookwheel-shell-v${CACHE_VERSION}`) correctly avoids cross-deploy staleness at the cache-store level** — a new deploy gets an entirely new cache name, and `activate` cleans up the old one, so there's no risk of indefinitely serving a pre-deploy asset from a stale cache bucket once the new SW takes over.
- **CoverUrl is stored and returned as a plain string URL (`varchar(2048)`), not an inline base64 image blob** — confirmed in `BookRecord.cs` and the live schema, so there's no over-fetching risk from embedded image data in `GET /api/books` responses.
- **`GET /api/stats/aggregate` correctly enforces admin-only access** (403 for `perfuser`), consistent with the README's description of that endpoint — verified live, not just by reading the code.
- **Indexes exist on every foreign-key-equivalent column that's actually used as a query predicate today** (`UserId` on both `books` and `spin_selections`, `BookId` on `spin_selections`) — the one gap (`DeletedAtUtc`) is a genuinely minor, scale-dependent one, not a systemic pattern of missing indexes.
- **No N+1 query pattern (per-row query in a loop) was found anywhere in the repository layer** — the inefficiencies present (B2, B3, B4) are redundant *whole-set* fetches or oversized payloads, not the much worse per-item query multiplication pattern the audit specifically checked for.
- **Response times are healthy in steady state** — every endpoint tested settled to well under 150ms after the first (JIT/connection-warmup) call, comfortably fast for an interactive app at this scale.

---

## Recommendations

Ordered roughly by impact-to-effort ratio:

1. **Enable response compression** (`AddResponseCompression`/`UseResponseCompression` in `Program.cs`, covering at minimum `text/javascript`, `text/css`, `application/json`, `text/html`). Single largest win available: ~126KB saved per uncached page load today, and the JSON API responses would compress well too.
2. **Split the static-file cache-control logic**: keep `no-store` for `index.html`/`sw.js` (already isolated in their own `MapGet` handlers), but give everything served through `UseStaticFiles` (`/js/`, `/css/`, `/icons/`, `/manifest.webmanifest`) a long-lived, immutable cache header (`public, max-age=31536000, immutable`) since they're already cache-busted via `?v=`. This makes the existing versioning scheme actually pay for itself.
3. **Add a minification step to the build** for `app.js`, `i18n.js`, and `site.css` before they land in `wwwroot` inside the Docker image (an `esbuild`/`terser`+`cssnano` step ahead of `dotnet publish` in the `Dockerfile`'s build stage is a natural fit). Stacks multiplicatively with #1.
4. **Fix the service worker's precache list to use the same versioned URLs the app actually requests** (template `SHELL_ASSETS` with `__ASSET_VERSION__` the same way `sw.js`'s own cache name already is), so the `install` step's bandwidth isn't spent on entries that never get matched.
5. **Add `.AsNoTracking()` to every read-only query in `Storage/Postgres/*.cs`** — mechanical, zero behavior change, removes unnecessary EF change-tracking overhead.
6. **Reuse a single book fetch in `POST /api/books/spin`** instead of calling `SelectRandomAsync` (which fetches the full list) and then `GetAllAsync` (which fetches it again) — fetch once, pick the random element and build the response from the same in-memory list.
7. **Consolidate the five round trips in `GetForUserAsync` (stats)** — at minimum, merge the total-spins and unique-books-spun counts (and the never-spun-ID lookup that overlaps with it) into fewer queries.
8. **Confirm whether `activeBooks` needs to be a separate field from `books` in `GET /api/books` and `POST /api/books/spin` responses**; if the frontend doesn't rely on them differing, drop the duplicate field and roughly halve those payloads.
9. **Add a composite index on `books("UserId", "DeletedAtUtc")`** to keep the (currently free) soft-delete filter cheap as per-user book counts grow — same recommendation as `docs/audits/database.md`, restated here from the query-cost angle.
10. *(Optional, low priority)* Consider an Alpine-based final image (`aspnet:8.0-alpine`) to shrink the 343MB image, and re-measure the render-blocking CSS/JS insight (F5) once #1-#4 are in place, since its 115ms estimate will shift once the underlying payload sizes and cache behavior change.

---

## Limitations

- All frontend timing was captured on a local, unthrottled network (loopback to `localhost:32700`); the Fast-3G/4x-CPU measurement is a single simulated data point, not a range across multiple runs or real device/network conditions.
- Load was tested with one user (`perfuser`) and 18 books — enough to exercise pagination and wheel rendering, but not representative of larger per-user book counts, many concurrent users, or the soft-deleted-row backlog that `DeletedAtUtc` filtering has to work around at scale (B5's impact is inferred from schema/query-plan reasoning, not measured under load, since reproducing a large dataset was out of scope and the constraint against touching other accounts' data limited how much volume could be generated).
- API endpoint timings are single-machine, single-request-at-a-time measurements (sequential `Invoke-WebRequest` calls), not a concurrent load test — no conclusions are drawn here about behavior under simultaneous multi-user traffic.
- The browser's persistent profile arrived already authenticated as `a11yuser` from a prior audit session; that session was read (`GET /api/books`, one `/api/auth/me` check) before being logged out in favor of `perfuser`, per this audit's constraint not to touch other test accounts' data — no writes were made under `a11yuser`.
- Lighthouse's performance category is excluded by the `lighthouse_audit` tool in this environment ("This excludes performance. For performance audits, run performance_start_trace") — the Lighthouse scores reported here (Accessibility/Best Practices/SEO/Agentic Browsing) are supplementary context, not this audit's performance measurement, which instead comes from the trace-based LCP/TTFB/CLS data.
- The Google Analytics tag failure (F6) appears environment-specific (DNS/network restriction in this sandbox) and was not investigated further as it falls outside BookWheel's own code.
- No source code was modified and no containers were restarted during this audit, per the task constraints; all findings are based on static code review and read-only interaction with the already-running instance.
