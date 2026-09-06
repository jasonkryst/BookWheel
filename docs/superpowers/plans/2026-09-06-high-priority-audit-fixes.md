# High-Priority Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close all 7 High-priority findings from the 2026-09-04/06 comprehensive audit (`docs/audits/SUMMARY.md`), each landing as its own tested, documented, version-bumped PR against `main`.

**Architecture:** No architectural rewrite. Each task is a scoped, independent change to existing files: frontend consent UI, CI action pinning, a LICENSE file, a global ASP.NET Core validation-localization hook, response compression + cache-header middleware config, an opt-in Postgres least-privilege role split, and a test-infrastructure refactor to share Testcontainers per test class instead of per test method.

**Tech Stack:** .NET 8 / ASP.NET Core, EF Core 9 + Npgsql, PostgreSQL 16, vanilla JS SPA, xUnit + Testcontainers, GitHub Actions, Docker/docker-compose.

**Spec:** `docs/audits/SUMMARY.md` (High-priority items 1-7), cross-referencing `SECURITY_AUDIT_REPORT.md`'s "Full Security Audit — 2026-09-04" section, `docs/audits/database.md`, `docs/audits/other-areas.md`, `docs/audits/i18n.md`, `docs/audits/performance.md`, and `docs/audits/testing.md`.

## Global Constraints

- Target framework: `net8.0`. `InformationalVersion` in `BookWheel/BookWheel.csproj` is currently `2.13.0` — CI validates this against `^[0-9]+(\.[0-9]+){0,3}$` (see the csproj comment); never change its *format*, only its value.
- Every task lands as its own branch off the current `main` tip and its own PR — do not stack tasks on top of each other's unmerged branches. Fetch and branch from `origin/main` fresh before starting each task.
- Every task that changes runtime behavior gets: (a) a test, (b) a version bump (patch-level `x.y.Z` for internal/config/test-only changes, minor-level `x.Y.0` for user-facing feature changes or changes that require operator action to adopt), (c) a README and/or `IMPROVEMENT_ROADMAP.md` update, (d) a `SECURITY_AUDIT_REPORT.md` "Status" update where the task closes a tracked finding.
- Do not touch the Medium/Low-priority findings from the audit in these tasks — each task's diff should be scoped to its own finding only, even if a related stale doc section is sitting right next to what you're editing.
- All new user-facing strings (frontend `i18n.js`, backend `SharedErrors*.resx`) must be added in all three supported locales: `en`, `es`, `pl`. Follow the project's existing convention of plain, non-technical translations (see `README.md`'s i18n section, which is explicit that es/pl are a first-pass machine/assistant translation, not a native-speaker review).
- Commit messages end with the standard Claude Code attribution trailer used throughout this session.

---

### Task 1: Google Analytics opt-out preference (closes SUMMARY.md item #1 / Security Finding #2)

**Decision (per user, overriding the audit's two suggested options):** keep the existing hardcoded default `Analytics:GoogleAnalyticsId` and continue tracking by default — add a **Settings → Preferences** checkbox that lets an end user opt **out**. This is an opt-out model, not opt-in/consent-gated; it directly fixes the audit's "no consent mechanism" finding without disabling analytics for the app owner's own instance. Document the setting in the README so downstream forkers know it exists and how to blank it out for their own deployment (closes the Other Areas §5 documentation half of this same finding too).

**Files:**
- Modify: `BookWheel/wwwroot/index.html:227-242` (Preferences panel), `:419-427` (gtag script block)
- Modify: `BookWheel/wwwroot/js/app.js` (new consts near `THEME_STORAGE_KEY` at line 129, new functions near `applyTheme` at line 324, new wiring near the `langSelect` block at line 2242, one line added to the bootstrap IIFE at line 2586)
- Modify: `BookWheel/wwwroot/js/i18n.js` (new `settings.analyticsLabel` key in all three locale blocks)
- Modify: `README.md` (new "Analytics" subsection)
- Test: `BookWheel.Tests/BookWheelFrontendTests.cs` (new test asserting the checkbox markup and the opt-out script logic are present in the served HTML/JS)
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:**
- Produces: `localStorage` key `bookwheel-analytics-consent` (`'true'` | `'false'`; absent = opted in, matching current default behavior). Global function `applyAnalyticsConsent()` in `app.js`, called once at bootstrap and once on checkbox change.
- Consumes: `window.__BOOKWHEEL_GA_ID__` (new global, substituted server-side from the existing `__GOOGLE_ANALYTICS_ID__` placeholder — no backend change needed since `Program.cs`'s `WriteConfiguredIndexAsync` already does a blanket string replace of that placeholder wherever it appears in `index.html`).

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b feature/analytics-opt-out origin/main
```

- [ ] **Step 2: Rework the GA script block in `index.html` to be consent-aware**

Replace lines 419-427 (the `<!-- Google tag (gtag.js) -->` block) with:

```html
  <!-- Google tag (gtag.js) -->
  <script>
    window.__BOOKWHEEL_GA_ID__ = "__GOOGLE_ANALYTICS_ID__";
  </script>
  <script async src="https://www.googletagmanager.com/gtag/js?id=__GOOGLE_ANALYTICS_ID__"></script>
  <script>
    window.dataLayer = window.dataLayer || [];
    function gtag(){dataLayer.push(arguments);}
    gtag('js', new Date());

    gtag('config', '__GOOGLE_ANALYTICS_ID__');
  </script>
```

(This keeps the existing substitution mechanism and script loading exactly as-is — the opt-out is enforced via Google's own documented `ga-disable-<ID>` flag, set by `app.js` before any hit is sent, not by conditionally loading the script.)

- [ ] **Step 3: Add the checkbox to the Preferences panel**

In `index.html`, inside `<section id="settingsPreferencesPanel" ...>` (starts line 227), after the closing `</label>` of the language row (line 241) and before the panel's closing `</section>` (line 242), add:

```html
        <label class="checkbox-row settings-row">
          <input id="analyticsConsentCheckbox" type="checkbox" checked />
          <span data-i18n="settings.analyticsLabel">Allow anonymous usage analytics</span>
        </label>
```

(`checked` as the static HTML default matches "opted in unless the user has explicitly opted out" — `app.js` will override the checked state from `localStorage` on load, same pattern as `syncLangSelect()`.)

- [ ] **Step 4: Add the i18n key in all three locales**

In `BookWheel/wwwroot/js/i18n.js`, find the `settings` object in each of the three locale blocks (`en`, `es`, `pl` — same object shape as `settings.themeLabel`/`settings.languageLabel` already there) and add one new key to each:

```js
// en
analyticsLabel: 'Allow anonymous usage analytics',
```

```js
// es
analyticsLabel: 'Permitir análisis de uso anónimo',
```

```js
// pl
analyticsLabel: 'Zezwól na anonimową analitykę użytkowania',
```

- [ ] **Step 5: Write the failing frontend test**

Add to `BookWheel.Tests/BookWheelFrontendTests.cs` (mirror the file's existing style — it fetches `/` and `/js/app.js` as text and asserts on substrings; see e.g. `Home_Page_Should_Include_I18n_Attributes_And_Settings_Button`):

```csharp
[Fact]
public async Task Home_Page_Should_Include_Analytics_Opt_Out_Checkbox()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    var html = await client.GetStringAsync("/");

    Assert.Contains("id=\"analyticsConsentCheckbox\"", html, StringComparison.Ordinal);
    Assert.Contains("data-i18n=\"settings.analyticsLabel\"", html, StringComparison.Ordinal);
    Assert.Contains("window.__BOOKWHEEL_GA_ID__", html, StringComparison.Ordinal);
}

[Fact]
public async Task Frontend_Script_Should_Implement_Analytics_Opt_Out_Logic()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    var script = await client.GetStringAsync("/js/app.js?v=test");

    Assert.Contains("ANALYTICS_CONSENT_STORAGE_KEY", script, StringComparison.Ordinal);
    Assert.Contains("applyAnalyticsConsent", script, StringComparison.Ordinal);
    Assert.Contains("ga-disable-", script, StringComparison.Ordinal);
}
```

- [ ] **Step 6: Run the new tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Analytics"`
Expected: both FAIL (checkbox/markers don't exist yet).

- [ ] **Step 7: Implement the consent logic in `app.js`**

Near the theme constants (after `const THEME_LABEL_KEYS = ...` at line 135), add:

```js
const ANALYTICS_CONSENT_STORAGE_KEY = 'bookwheel-analytics-consent';
```

Near `applyTheme` (after it ends, around line 339), add:

```js
function isAnalyticsOptedOut() {
  return localStorage.getItem(ANALYTICS_CONSENT_STORAGE_KEY) === 'false';
}

function applyAnalyticsConsent() {
  const gaId = window.__BOOKWHEEL_GA_ID__;
  if (!gaId) {
    return;
  }

  const optedOut = isAnalyticsOptedOut();
  window[`ga-disable-${gaId}`] = optedOut;

  if (analyticsConsentCheckbox) {
    analyticsConsentCheckbox.checked = !optedOut;
  }
}

function setAnalyticsConsent(optedIn) {
  localStorage.setItem(ANALYTICS_CONSENT_STORAGE_KEY, optedIn ? 'true' : 'false');
  applyAnalyticsConsent();
}
```

Add the element reference alongside the other Settings-panel consts (near line 104, after `const settingsPreferencesPanel = ...`):

```js
const analyticsConsentCheckbox = document.getElementById('analyticsConsentCheckbox');
```

Wire the checkbox, alongside the existing `langSelect` change-listener block (after line 2246):

```js
if (analyticsConsentCheckbox) {
  analyticsConsentCheckbox.addEventListener('change', () => {
    setAnalyticsConsent(analyticsConsentCheckbox.checked);
  });
}
```

Call it once at bootstrap, in the async IIFE, right after `applyTheme(getPreferredTheme());` (line 2586):

```js
  applyTheme(getPreferredTheme());
  applyAnalyticsConsent();
  await loadAppVersion();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Analytics"`
Expected: PASS.

- [ ] **Step 9: Document the setting in the README**

In `README.md`, add a new `## Analytics` section directly after the `## Internationalization` section (find it via `grep -n "^## Internationalization" README.md` — insert after that section's content, before the next `##`). Content:

```markdown
## Analytics

BookWheel includes an optional Google Analytics (gtag.js) integration, configured via `Analytics:GoogleAnalyticsId` in `appsettings.json` (or the `Analytics__GoogleAnalyticsId` environment variable). The shipped default points at the upstream maintainer's own analytics property — **if you fork or self-host this project, set it to your own GA4 property ID, or blank it out (`""`) to disable analytics entirely.**

When a non-empty ID is configured, the script loads on every page (including the pre-login screen) and tracking is **on by default**. Users can opt out at any time via the "Allow anonymous usage analytics" checkbox in Settings → Preferences; the choice is stored in the browser's `localStorage` (`bookwheel-analytics-consent`) and takes effect immediately via Google's documented `window['ga-disable-<id>']` flag, without needing a page reload.
```

- [ ] **Step 10: Update `SECURITY_AUDIT_REPORT.md`'s finding status**

Find "Google Analytics is enabled by default with a real, hardcoded property ID" (Finding #2 in the "Full Security Audit — 2026-09-04" section) and append a status note directly after its Recommendations list:

```markdown
**Status (2026-09-06):** Addressed via an opt-out mechanism rather than removal — see `README.md`'s new "Analytics" section. The default ID is retained (tracking remains on by default for the maintainer's own deployment), but end users can now disable it via a Settings → Preferences checkbox, which sets Google's documented `ga-disable-<id>` flag immediately. Downstream forkers are now explicitly told in the README to override or blank the ID for their own deployment.
```

- [ ] **Step 11: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump `2.13.0` → `2.14.0` (minor: new user-facing preference).

- [ ] **Step 12: Run the full test suite**

Run: `dotnet test BookWheel.slnx --verbosity normal`
Expected: all pass (allow one retry if a failure is a Docker-daemon timeout unrelated to this change — see `docs/audits/testing.md` Finding H1, not yet fixed at this point in the task sequence).

- [ ] **Step 13: Commit and push**

```bash
git add BookWheel/wwwroot/index.html BookWheel/wwwroot/js/app.js BookWheel/wwwroot/js/i18n.js BookWheel.Tests/BookWheelFrontendTests.cs README.md SECURITY_AUDIT_REPORT.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
Add analytics opt-out preference and document the GA default for forkers

Adds a Settings > Preferences checkbox that lets end users disable
Google Analytics tracking at any time via Google's documented
ga-disable flag, and documents the Analytics:GoogleAnalyticsId
setting in the README so anyone forking the project knows to
override or blank it for their own deployment.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin feature/analytics-opt-out
gh pr create --base main --title "Add analytics opt-out preference and document GA default" --body "Closes SUMMARY.md High-priority item #1. See commit message for details."
```

---

### Task 2: Postgres least-privilege role split (closes SUMMARY.md item #2 / Database Finding #12)

**Decision (per user):** implement now, as an **opt-in** split that ships as the default for fresh `docker-compose up` installs, with a documented manual upgrade path (SQL script) for existing deployments — because rotating the app's live DB credentials is a breaking change for anyone already running the bundled `docker-compose.yml`, and Postgres only auto-runs `/docker-entrypoint-initdb.d/` scripts against a freshly-initialized (empty) data volume.

**Files:**
- Create: `scripts/postgres/init-least-privilege-roles.sh`
- Modify: `docker-compose.yml`
- Modify: `BookWheel/Program.cs:30-37,140-145`
- Modify: `README.md` (Data Storage section)
- Modify: `IMPROVEMENT_ROADMAP.md:54` (mark item 10 done, correct its description per the Database audit's finding that it's currently understated)
- Test: `BookWheel.Tests/BookWheelSmokeTests.cs` (new test: app still starts and migrates successfully when `ConnectionStrings:BookWheelMigrations` is unset and falls back to `ConnectionStrings:BookWheel`)
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:**
- Produces: new optional config key `ConnectionStrings:BookWheelMigrations`. When unset, falls back to `ConnectionStrings:BookWheel` (today's exact behavior — zero forced change for anyone not adopting the split).
- Consumes: none new.

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b feature/postgres-least-privilege origin/main
```

- [ ] **Step 2: Write the failing test**

Add to `BookWheel.Tests/BookWheelSmokeTests.cs` (mirrors its existing `Startup_Health_And_Version_Endpoints_Return_Success`-style pattern — this test relies on `BookWheelWebAppFactory` never setting `ConnectionStrings:BookWheelMigrations`, so it exercises the fallback path by construction):

```csharp
[Fact]
public async Task Startup_Migrates_Successfully_When_BookWheelMigrations_ConnectionString_Is_Unset()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    var readyResponse = await client.GetAsync("/health/ready");

    Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
}
```

- [ ] **Step 3: Run it to verify it currently passes (baseline) then implement the Program.cs split**

Run: `dotnet test --filter "FullyQualifiedName~Startup_Migrates_Successfully"`
Expected: PASS already (this test only pins down today's fallback-equivalent behavior; it must keep passing after Step 4's refactor — run it again after Step 4 as the real regression check).

- [ ] **Step 4: Split the migration connection from the runtime connection in `Program.cs`**

Replace lines 30-37:

```csharp
var connectionString = builder.Configuration.GetConnectionString("BookWheel");
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new InvalidOperationException(
		"ConnectionStrings:BookWheel is not configured. Set it in appsettings.json, an environment variable (ConnectionStrings__BookWheel), or a deployment secret.");
}

var migrationConnectionString = builder.Configuration.GetConnectionString("BookWheelMigrations");
if (string.IsNullOrWhiteSpace(migrationConnectionString))
{
	// No least-privilege split configured — migrations and runtime queries share
	// the same role, matching pre-2.15.0 behavior exactly.
	migrationConnectionString = connectionString;
}

builder.Services.AddPooledDbContextFactory<BookWheelDbContext>(options => options.UseNpgsql(connectionString));
```

Replace the migration block at lines 140-145:

```csharp
using (var migrationScope = app.Services.CreateScope())
{
	var migrationOptionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
	migrationOptionsBuilder.UseNpgsql(migrationConnectionString);
	await using var startupDbContext = new BookWheelDbContext(migrationOptionsBuilder.Options);
	await startupDbContext.Database.MigrateAsync();
}
```

(The DI-registered `IDbContextFactory<BookWheelDbContext>` — used by every repository at runtime — keeps using `connectionString` unchanged. Only the one-time startup migration now uses a separately-configurable, more-privileged connection.)

- [ ] **Step 5: Run the test again to confirm the fallback path still works**

Run: `dotnet test --filter "FullyQualifiedName~Startup_Migrates_Successfully"`
Expected: PASS (proves the refactor is behavior-preserving when `BookWheelMigrations` is unset).

- [ ] **Step 6: Write the role-provisioning script**

Create `scripts/postgres/init-least-privilege-roles.sh`:

```bash
#!/bin/bash
# Creates a least-privilege runtime role (bookwheel_app) and revokes superuser
# from the bootstrap role (bookwheel), which otherwise runs as a full Postgres
# superuser per the official postgres image's default initdb behavior.
#
# Runs automatically against a FRESH data volume (mounted into
# /docker-entrypoint-initdb.d/). For an EXISTING deployment's volume, run this
# manually once: docker exec -i bookwheel-postgres bash < scripts/postgres/init-least-privilege-roles.sh
# then update ConnectionStrings__BookWheel to use bookwheel_app and set
# ConnectionStrings__BookWheelMigrations to the original bookwheel connection
# string, and restart the app container.
set -euo pipefail

: "${POSTGRES_DB:?POSTGRES_DB must be set}"
: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${POSTGRES_APP_PASSWORD:?POSTGRES_APP_PASSWORD must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'bookwheel_app') THEN
            CREATE ROLE bookwheel_app WITH LOGIN PASSWORD '$POSTGRES_APP_PASSWORD';
        ELSE
            ALTER ROLE bookwheel_app WITH LOGIN PASSWORD '$POSTGRES_APP_PASSWORD';
        END IF;
    END
    \$\$;

    GRANT CONNECT ON DATABASE $POSTGRES_DB TO bookwheel_app;
    GRANT USAGE ON SCHEMA public TO bookwheel_app;
    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO bookwheel_app;
    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO bookwheel_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE $POSTGRES_USER IN SCHEMA public
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bookwheel_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE $POSTGRES_USER IN SCHEMA public
        GRANT USAGE, SELECT ON SEQUENCES TO bookwheel_app;

    ALTER ROLE $POSTGRES_USER WITH NOSUPERUSER;
EOSQL

echo "bookwheel_app role provisioned and $POSTGRES_USER superuser revoked."
```

Make it executable: `chmod +x scripts/postgres/init-least-privilege-roles.sh`.

- [ ] **Step 7: Wire the script and new connection strings into `docker-compose.yml`**

Replace the `postgres` service's `environment` block:

```yaml
    environment:
      POSTGRES_DB: bookwheel
      POSTGRES_USER: bookwheel
      # Local-dev default only. Override via a .env file (POSTGRES_PASSWORD=...) for anything
      # beyond local dev — see README's "Data Storage" section.
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-bookwheel}
      # Local-dev default only, same override guidance as POSTGRES_PASSWORD. This is the
      # least-privilege runtime role's password — the app's day-to-day DML connection uses
      # this role, not the bookwheel bootstrap role (see scripts/postgres/init-least-privilege-roles.sh).
      POSTGRES_APP_PASSWORD: ${POSTGRES_APP_PASSWORD:-bookwheel_app}
    volumes:
      - bookwheel_pg_data:/var/lib/postgresql/data
      - ./scripts/postgres/init-least-privilege-roles.sh:/docker-entrypoint-initdb.d/init-least-privilege-roles.sh:ro
```

Replace the `bookwheel` app service's `environment` block:

```yaml
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      # Least-privilege runtime role (DML only) — see scripts/postgres/init-least-privilege-roles.sh.
      ConnectionStrings__BookWheel: "Host=postgres;Database=bookwheel;Username=bookwheel_app;Password=${POSTGRES_APP_PASSWORD:-bookwheel_app}"
      # Schema-owning role, used only for the one-time startup migration.
      ConnectionStrings__BookWheelMigrations: "Host=postgres;Database=bookwheel;Username=bookwheel;Password=${POSTGRES_PASSWORD:-bookwheel}"
```

- [ ] **Step 8: Verify locally against a fresh volume**

```bash
docker compose down -v
docker compose up -d --build
docker exec bookwheel-postgres psql -U bookwheel -d bookwheel -c "SELECT rolname, rolsuper FROM pg_roles WHERE rolname IN ('bookwheel', 'bookwheel_app');"
```

Expected: `bookwheel_app` row exists with `rolsuper = f`; `bookwheel` row now shows `rolsuper = f` too (revoked). Then confirm the app itself works end-to-end:

```bash
curl -s http://localhost:32700/health/ready
```

Expected: `Healthy`. If it fails, check `docker compose logs bookwheel` for an Npgsql auth/permission error and re-check Step 7's connection strings before proceeding.

- [ ] **Step 9: Document the split and the upgrade path in `README.md`**

In the "Data Storage" section, after the paragraph ending "...before running `docker compose up`." (originally line 313), add:

```markdown
**Least-privilege database roles:** fresh deployments of the bundled `docker-compose.yml` automatically provision two Postgres roles: `bookwheel` (schema owner, used only for the one-time startup migration) and `bookwheel_app` (`SELECT`/`INSERT`/`UPDATE`/`DELETE` only, used for all runtime request handling). This is provisioned by `scripts/postgres/init-least-privilege-roles.sh`, which Postgres only runs automatically against a **fresh, empty** data volume. If you're upgrading an existing deployment's volume, run it manually once against your running Postgres container, then point `ConnectionStrings__BookWheel` at the new `bookwheel_app` role and `ConnectionStrings__BookWheelMigrations` at the original `bookwheel` connection string before restarting the app:

```bash
docker exec -i bookwheel-postgres bash < scripts/postgres/init-least-privilege-roles.sh
```

If `ConnectionStrings:BookWheelMigrations` is left unset, the app falls back to using `ConnectionStrings:BookWheel` for both migrations and runtime queries — today's original single-role behavior — so this upgrade is entirely opt-in.
```

- [ ] **Step 10: Correct and close out `IMPROVEMENT_ROADMAP.md` item 10**

Replace line 54:

```markdown
10. [Done] Split the runtime PostgreSQL role into a least-privilege DML-only role (`bookwheel_app`) and a separate schema-owning role used only for startup migrations. The prior wording of this item understated the actual risk: the live runtime role was a full PostgreSQL **superuser**, not merely DDL-capable (`docs/audits/database.md`, 2026-09-04, Finding #12) — this is now fixed for fresh deployments, with a documented manual upgrade path for existing ones.
```

- [ ] **Step 11: Update `SECURITY_AUDIT_REPORT.md`'s tracked finding status**

Find "Automatic EF Core migrations at startup require DDL privileges" under "Previously Reported Findings — Status" and change its status line from "**Open**" to:

```markdown
#### Automatic EF Core migrations at startup require DDL privileges (Low, reported 2026-08-19) — **Closed (2026-09-06)**

Closed via a least-privilege role split (`ConnectionStrings:BookWheelMigrations` for the schema-owning migration role, `ConnectionStrings:BookWheel` for a new DML-only `bookwheel_app` runtime role) for fresh deployments, with a documented manual upgrade path for existing ones. Note this closes a materially larger issue than originally described — `docs/audits/database.md` (2026-09-04, Finding #12) found the live runtime role was a full PostgreSQL superuser, not merely DDL-capable.
```

- [ ] **Step 12: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump to `2.15.0` (minor: new required-for-adoption configuration surface, even though it's backward-compatible by default).

- [ ] **Step 13: Run the full test suite**

Run: `dotnet test BookWheel.slnx --verbosity normal`
Expected: all pass.

- [ ] **Step 14: Commit and push**

```bash
git add scripts/postgres/init-least-privilege-roles.sh docker-compose.yml BookWheel/Program.cs BookWheel.Tests/BookWheelSmokeTests.cs README.md IMPROVEMENT_ROADMAP.md SECURITY_AUDIT_REPORT.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
Split runtime Postgres role from superuser to least-privilege DML-only

Fresh docker-compose deployments now provision a dedicated bookwheel_app
role (SELECT/INSERT/UPDATE/DELETE only) for runtime request handling,
separate from the schema-owning bookwheel role used only for the
one-time startup migration. The prior tracked finding understated the
risk: the live runtime role was a full PostgreSQL superuser, not just
DDL-capable. Existing deployments get a documented, opt-in manual
upgrade path since this is a live-credential change Postgres can't
apply automatically to an already-initialized data volume.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin feature/postgres-least-privilege
gh pr create --base main --title "Split runtime Postgres role from superuser to least-privilege" --body "Closes SUMMARY.md High-priority item #2. See commit message for details."
```

---

### Task 3: SHA-pin GitHub Actions in `docker-release.yml` (closes SUMMARY.md item #3 / Other Areas §1)

**Files:**
- Modify: `.github/workflows/docker-release.yml`
- Modify: `IMPROVEMENT_ROADMAP.md` (add to Priority 1 as a new done item)
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:** none (CI-only change, no application code affected).

**Exact SHAs to pin (resolved 2026-09-06 via `gh api repos/<org>/<repo>/commits/<tag>`, matching or exceeding the versions already in use):**

| Action | Tag | Commit SHA |
|---|---|---|
| `actions/checkout` | v7 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| `actions/setup-dotnet` | v6 | `a98b56852c35b8e3190ac28c8c2271da59106c68` |
| `docker/setup-buildx-action` | v3 | `8d2750c68a42422c14e847fe6c8ac0403b4cbd6f` |
| `docker/login-action` | v4 | `dbcb813823bdd20940b903addbd779551569679f` |
| `docker/build-push-action` | v7 | `53b7df96c91f9c12dcc8a07bcb9ccacbed38856a` (same SHA already pinned in `ci.yml` — confirms consistency) |

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b security/pin-docker-release-actions origin/main
```

- [ ] **Step 2: Re-verify the SHAs are still current before pinning**

```bash
gh api repos/actions/checkout/commits/v7 --jq .sha
gh api repos/actions/setup-dotnet/commits/v6 --jq .sha
gh api repos/docker/setup-buildx-action/commits/v3 --jq .sha
gh api repos/docker/login-action/commits/v4 --jq .sha
gh api repos/docker/build-push-action/commits/v7 --jq .sha
```

Expected: each matches the table above exactly. If any tag has moved (a new patch release retagged the same major version), use the new SHA instead and note the discrepancy in the PR description.

- [ ] **Step 3: Pin every action in `docker-release.yml`**

Replace each `uses:` line:

```yaml
      - name: Checkout
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7
```

```yaml
      - name: Setup .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6
```

```yaml
      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@8d2750c68a42422c14e847fe6c8ac0403b4cbd6f # v3
```

```yaml
      - name: Log in to Docker Hub
        uses: docker/login-action@dbcb813823bdd20940b903addbd779551569679f # v4
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@dbcb813823bdd20940b903addbd779551569679f # v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
```

```yaml
      - name: Build and push version tags
        uses: docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a # v7.3.0
```

(and the second `docker/build-push-action` occurrence for the `latest` tag step — same SHA.)

- [ ] **Step 4: Validate the workflow syntax**

Run: `docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:1.7.12 -color`
Expected: no errors (this is the exact command `ci.yml`'s own `actionlint` job runs).

- [ ] **Step 5: Confirm Dependabot will track these pins going forward**

Run: `cat .github/dependabot.yml` and confirm a `github-actions` ecosystem entry already exists covering `.github/workflows/` (it does, per the Other Areas audit — no change needed here, just confirming before claiming this is "maintainable" in the PR description).

- [ ] **Step 6: Update the roadmap**

In `IMPROVEMENT_ROADMAP.md`, add a new item to the end of the Priority 1 list (after item 10):

```markdown
11. [Done] SHA-pin all GitHub Actions in `docker-release.yml` — previously the only unpinned workflow despite holding `packages: write` and real Docker Hub/GHCR publish credentials (`docs/audits/other-areas.md`, 2026-09-04, §1).
```

- [ ] **Step 7: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump to `2.15.1` (patch: CI-only hardening, no runtime behavior change) — adjust to whatever the current version is if Task 2 has already merged and bumped to `2.15.0` by the time this task starts; otherwise bump from whatever `main` has at branch time.

- [ ] **Step 8: Commit and push**

```bash
git add .github/workflows/docker-release.yml IMPROVEMENT_ROADMAP.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
SHA-pin GitHub Actions in docker-release.yml

This was the only workflow in the repo with zero SHA-pinned actions,
despite holding packages: write and real Docker Hub/GHCR publish
credentials — the highest-privilege, least-hardened workflow found
in the 2026-09-04 audit (docs/audits/other-areas.md, §1).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin security/pin-docker-release-actions
gh pr create --base main --title "SHA-pin GitHub Actions in docker-release.yml" --body "Closes SUMMARY.md High-priority item #3. See commit message for details."
```

---

### Task 4: Add a `LICENSE` file (closes SUMMARY.md item #4 / Other Areas §7)

**Files:**
- Create: `LICENSE`
- Modify: `README.md` (add a License section/mention)
- Modify: `IMPROVEMENT_ROADMAP.md`
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:** none.

**Decision needed before starting this task:** the audit didn't pick a license — MIT and Apache-2.0 are both common for a project like this (self-hosted web app, publishes Docker images). **Ask the user which license they want before writing Step 2** if it hasn't already been decided; do not guess silently, since this is a legal/ownership decision only the repo owner can make.

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b docs/add-license origin/main
```

- [ ] **Step 2: Confirm license choice with the user, then create `LICENSE`**

Use `gh api repos/jasonkryst/BookWheel/license` or the standard SPDX text for the chosen license (e.g., for MIT: the standard MIT license template with `Copyright (c) 2026 Jason Kryst` — confirm the copyright year and holder name with the user rather than assuming). Write the full license text to `LICENSE` at the repo root, no file extension.

- [ ] **Step 3: Reference it in `README.md`**

Add near the top of `README.md` (immediately after the title/intro, before "## Features"):

```markdown
## License

[MIT](LICENSE) — see the `LICENSE` file for the full text.
```

(Adjust the license name/link if a different license was chosen in Step 2.)

- [ ] **Step 4: Update the roadmap**

Add to `IMPROVEMENT_ROADMAP.md`'s Priority 1 list:

```markdown
12. [Done] Add a `LICENSE` file — the project previously had no license anywhere in the repo despite publishing Docker images publicly, leaving redistribution rights ambiguous (`docs/audits/other-areas.md`, 2026-09-04, §7).
```

- [ ] **Step 5: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump the patch version (e.g. `2.15.2`, adjusted for whatever `main` is at branch time).

- [ ] **Step 6: Commit and push**

```bash
git add LICENSE README.md IMPROVEMENT_ROADMAP.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
Add a LICENSE file

The project previously shipped with no license anywhere in the repo
despite publishing Docker images publicly to Docker Hub and GHCR,
leaving redistribution and fork rights ambiguous under default
copyright law (docs/audits/other-areas.md, 2026-09-04, §7).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin docs/add-license
gh pr create --base main --title "Add a LICENSE file" --body "Closes SUMMARY.md High-priority item #4. See commit message for details."
```

---

### Task 5: Localize ASP.NET Core's automatic validation errors (closes SUMMARY.md item #5 / i18n Finding F1)

**Files:**
- Modify: `BookWheel/Program.cs` (add `using Microsoft.AspNetCore.Mvc;`, add `.ConfigureApiBehaviorOptions(...)` after `AddControllers()`)
- Modify: `BookWheel/Models/UpdateBookRequest.cs`, `LoginRequest.cs`, `CreateUserRequest.cs`, `UpdateUserAccountRequest.cs`, `CompletePasswordResetRequest.cs`, `ValidatePasswordResetTokenRequest.cs` (explicit `ErrorMessage` on every DataAnnotations attribute)
- Modify: `BookWheel/Services/ApiMessageLocalizer.cs` (9 new dictionary entries)
- Modify: `BookWheel/Resources/SharedErrors.resx`, `SharedErrors.es.resx`, `SharedErrors.pl.resx` (9 new keys each)
- Test: `BookWheel.Tests/BookWheelApiTests.cs` (new tests)
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:**
- Produces: 9 new `SharedErrors` resx keys: `BookTitleLength`, `IsbnLength`, `AuthorLength`, `CoverUrlLength`, `InvalidBookType`, `UsernameLength`, `PasswordRequired`, `PasswordLength`, `ResetTokenRequired`. Reuses 2 existing keys: `BookTitleRequired`, `UsernameRequired`.
- Consumes: `ApiMessageLocalizer.Localize(string)` (existing signature, unchanged).

**New literal strings (exact text — must match exactly between the C# `ErrorMessage` and the `<value>` in `SharedErrors.resx`, since the dictionary is keyed by exact English literal):**

| Key | en | es | pl |
|---|---|---|---|
| `BookTitleLength` | Book title must be between 1 and 200 characters. | El título del libro debe tener entre 1 y 200 caracteres. | Tytuł książki musi mieć od 1 do 200 znaków. |
| `IsbnLength` | ISBN must be 20 characters or fewer. | El ISBN debe tener 20 caracteres o menos. | ISBN musi mieć maksymalnie 20 znaków. |
| `AuthorLength` | Author must be 300 characters or fewer. | El autor debe tener 300 caracteres o menos. | Autor musi mieć maksymalnie 300 znaków. |
| `CoverUrlLength` | Cover URL must be 2048 characters or fewer. | La URL de la portada debe tener 2048 caracteres o menos. | Adres URL okładki musi mieć maksymalnie 2048 znaków. |
| `InvalidBookType` | Book type must be a valid type. | El tipo de libro debe ser un tipo válido. | Typ książki musi być prawidłowym typem. |
| `UsernameLength` | Username must be between 1 and 64 characters. | El nombre de usuario debe tener entre 1 y 64 caracteres. | Nazwa użytkownika musi mieć od 1 do 64 znaków. |
| `PasswordRequired` | Password is required. | La contraseña es obligatoria. | Hasło jest wymagane. |
| `PasswordLength` | Password must be at least 8 characters. | La contraseña debe tener al menos 8 caracteres. | Hasło musi mieć co najmniej 8 znaków. |
| `ResetTokenRequired` | A reset token is required. | Se requiere un token de restablecimiento. | Wymagany jest token resetowania. |

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b i18n/localize-validation-errors origin/main
```

- [ ] **Step 2: Write the failing tests**

Add to `BookWheel.Tests/BookWheelApiTests.cs` (near the existing `Login_WithBadCredentials_ReturnsSpanishMessage_WhenAcceptLanguageIsSpanish` test at line 97 — same file, same style, same `factory`/`client`/`ReadJsonAsync` helpers already in scope):

```csharp
[Fact]
public async Task AddBook_WithMissingTitle_ReturnsSpanishValidationMessage_WhenAcceptLanguageIsSpanish()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    await client.PostAsJsonAsync("/api/auth/setup", new
    {
        username = "test-admin",
        password = "test-password"
    });

    var request = new HttpRequestMessage(HttpMethod.Post, "/api/books")
    {
        Content = JsonContent.Create(new { title = "" })
    };
    request.Headers.Add("Accept-Language", "es");

    var response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    using var doc = await ReadJsonAsync(response);
    var titleErrors = doc.RootElement.GetProperty("errors").GetProperty("Title")
        .EnumerateArray()
        .Select(e => e.GetString())
        .ToList();

    Assert.Contains("El título del libro es obligatorio.", titleErrors);
    Assert.DoesNotContain(titleErrors, msg => msg != null && msg.Contains("required", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public async Task AddBook_WithOverlongIsbn_ReturnsLocalizedLengthMessage()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    await client.PostAsJsonAsync("/api/auth/setup", new
    {
        username = "test-admin",
        password = "test-password"
    });

    var response = await client.PostAsJsonAsync("/api/books", new
    {
        title = "Valid Title",
        isbn = new string('9', 21)
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    using var doc = await ReadJsonAsync(response);
    var isbnErrors = doc.RootElement.GetProperty("errors").GetProperty("Isbn")
        .EnumerateArray()
        .Select(e => e.GetString())
        .ToList();

    Assert.Contains("ISBN must be 20 characters or fewer.", isbnErrors);
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AddBook_WithMissingTitle|FullyQualifiedName~AddBook_WithOverlongIsbn"`
Expected: FAIL — the first asserts Spanish text but currently gets English framework text (per the audit's live repro); the second currently gets a generic English length message with a different exact wording than our new literal.

- [ ] **Step 4: Add the 9 new resx keys to all three files**

In `BookWheel/Resources/SharedErrors.resx`, add (following the exact `<data name="..." xml:space="preserve"><value>...</value></data>` pattern already used for e.g. `BookTitleRequired` at line 21):

```xml
  <data name="BookTitleLength" xml:space="preserve">
    <value>Book title must be between 1 and 200 characters.</value>
  </data>
  <data name="IsbnLength" xml:space="preserve">
    <value>ISBN must be 20 characters or fewer.</value>
  </data>
  <data name="AuthorLength" xml:space="preserve">
    <value>Author must be 300 characters or fewer.</value>
  </data>
  <data name="CoverUrlLength" xml:space="preserve">
    <value>Cover URL must be 2048 characters or fewer.</value>
  </data>
  <data name="InvalidBookType" xml:space="preserve">
    <value>Book type must be a valid type.</value>
  </data>
  <data name="UsernameLength" xml:space="preserve">
    <value>Username must be between 1 and 64 characters.</value>
  </data>
  <data name="PasswordRequired" xml:space="preserve">
    <value>Password is required.</value>
  </data>
  <data name="PasswordLength" xml:space="preserve">
    <value>Password must be at least 8 characters.</value>
  </data>
  <data name="ResetTokenRequired" xml:space="preserve">
    <value>A reset token is required.</value>
  </data>
```

Repeat the same 9 `<data>` blocks in `SharedErrors.es.resx` and `SharedErrors.pl.resx`, using the Spanish/Polish `<value>` text from the table above instead.

- [ ] **Step 5: Add the 9 new dictionary entries to `ApiMessageLocalizer.cs`**

Add to the `KeysByEnglishMessage` dictionary (after the existing `InvalidIsbn`/`IsbnOrTitleRequired` entries):

```csharp
["Book title must be between 1 and 200 characters."] = "BookTitleLength",
["ISBN must be 20 characters or fewer."] = "IsbnLength",
["Author must be 300 characters or fewer."] = "AuthorLength",
["Cover URL must be 2048 characters or fewer."] = "CoverUrlLength",
["Book type must be a valid type."] = "InvalidBookType",
["Username must be between 1 and 64 characters."] = "UsernameLength",
["Password is required."] = "PasswordRequired",
["Password must be at least 8 characters."] = "PasswordLength",
["A reset token is required."] = "ResetTokenRequired",
```

- [ ] **Step 6: Add explicit `ErrorMessage` to every DataAnnotations attribute**

`BookWheel/Models/UpdateBookRequest.cs` — full replacement:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdateBookRequest
{
    [Required(ErrorMessage = "Book title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Book title must be between 1 and 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "ISBN must be 20 characters or fewer.")]
    public string? Isbn { get; set; }

    [StringLength(300, ErrorMessage = "Author must be 300 characters or fewer.")]
    public string? Author { get; set; }

    [StringLength(2048, ErrorMessage = "Cover URL must be 2048 characters or fewer.")]
    public string? CoverUrl { get; set; }

    public bool AddedByScanner { get; set; }

    [Range(1, 3, ErrorMessage = "Book type must be a valid type.")]
    public int BookTypeId { get; set; } = 1;
}
```

`BookWheel/Models/LoginRequest.cs` — apply the same pattern to its `Username`/`Password` properties:

```csharp
[Required(ErrorMessage = "Username is required.")]
[StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
public string Username { get; set; } = string.Empty;

[Required(ErrorMessage = "Password is required.")]
[StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
public string Password { get; set; } = string.Empty;
```

`BookWheel/Models/CreateUserRequest.cs` and `BookWheel/Models/UpdateUserAccountRequest.cs` — apply the same `Username` pattern as above to each file's `Username` property.

`BookWheel/Models/CompletePasswordResetRequest.cs`:

```csharp
[Required(ErrorMessage = "A reset token is required.")]
public string Token { get; set; } = string.Empty;

[Required(ErrorMessage = "Password is required.")]
[StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
public string NewPassword { get; set; } = string.Empty;
```

`BookWheel/Models/ValidatePasswordResetTokenRequest.cs`:

```csharp
[Required(ErrorMessage = "A reset token is required.")]
public string Token { get; set; } = string.Empty;
```

(Preserve each file's existing property names, namespaces, and any properties not listed above exactly as they are today — only add the `ErrorMessage` argument to existing attributes.)

- [ ] **Step 7: Add the global `InvalidModelStateResponseFactory` in `Program.cs`**

Add `using Microsoft.AspNetCore.Mvc;` to the top of `BookWheel/Program.cs` (alongside the existing `using` block at lines 1-14).

Replace line 68 (`builder.Services.AddControllers();`) with:

```csharp
builder.Services.AddControllers()
	.ConfigureApiBehaviorOptions(options =>
	{
		options.InvalidModelStateResponseFactory = context =>
		{
			var localizer = context.HttpContext.RequestServices.GetRequiredService<ApiMessageLocalizer>();
			var problemDetails = new ValidationProblemDetails(context.ModelState)
			{
				Status = StatusCodes.Status400BadRequest,
				Instance = context.HttpContext.Request.Path
			};

			foreach (var key in problemDetails.Errors.Keys.ToList())
			{
				problemDetails.Errors[key] = problemDetails.Errors[key]
					.Select(localizer.Localize)
					.ToArray();
			}

			return new BadRequestObjectResult(problemDetails)
			{
				ContentTypes = { "application/json" }
			};
		};
	});
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AddBook_WithMissingTitle|FullyQualifiedName~AddBook_WithOverlongIsbn"`
Expected: PASS.

- [ ] **Step 9: Run the existing i18n coverage test to confirm the new keys are picked up**

Run: `dotnet test --filter "FullyQualifiedName~ApiMessageLocalizerTests"`
Expected: PASS — `Localize_HasNonEmptyTranslation_ForEverySupportedCulture` iterates `ApiMessageLocalizer.KnownMessageKeys`, which now includes the 9 new entries, and will fail if any locale's resx is missing one.

- [ ] **Step 10: Update `SECURITY_AUDIT_REPORT.md`... (not applicable — skip; this is an i18n finding, not a security finding). Update `docs/audits/i18n.md` instead**, appending a resolution note directly after Finding F1's Recommendation:

```markdown
**Status (2026-09-06):** Fixed. `Program.cs` now configures a custom `InvalidModelStateResponseFactory` that runs every DataAnnotations validation message through `ApiMessageLocalizer.Localize()`. All six request DTOs with DataAnnotations (`UpdateBookRequest`, `LoginRequest`, `CreateUserRequest`, `UpdateUserAccountRequest`, `CompletePasswordResetRequest`, `ValidatePasswordResetTokenRequest`) now specify explicit `ErrorMessage` literals that map through the same 9-key (plus 2 reused) dictionary. Live-verified via a new regression test (`AddBook_WithMissingTitle_ReturnsSpanishValidationMessage_WhenAcceptLanguageIsSpanish`).
```

- [ ] **Step 11: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump the patch version (adjust for whatever `main` has at branch time).

- [ ] **Step 12: Run the full test suite**

Run: `dotnet test BookWheel.slnx --verbosity normal`
Expected: all pass.

- [ ] **Step 13: Commit and push**

```bash
git add BookWheel/Program.cs BookWheel/Models/*.cs BookWheel/Services/ApiMessageLocalizer.cs BookWheel/Resources/SharedErrors*.resx BookWheel.Tests/BookWheelApiTests.cs docs/audits/i18n.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
Localize ASP.NET Core's automatic model-validation errors

Adds a global InvalidModelStateResponseFactory that runs every
DataAnnotations validation message through the existing
ApiMessageLocalizer, and gives all six request DTOs explicit
ErrorMessage literals so the framework's default English-only
templates never reach the client. Previously, a Spanish/Polish UI
user submitting a blank title or over-length field saw raw English
text mixed into an otherwise fully-localized error path
(docs/audits/i18n.md, 2026-09-04, Finding F1).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin i18n/localize-validation-errors
gh pr create --base main --title "Localize ASP.NET Core's automatic validation errors" --body "Closes SUMMARY.md High-priority item #5. See commit message for details."
```

---

### Task 6: Response compression + fix static-asset cache headers (closes SUMMARY.md item #6 / Performance F1/F2)

**Files:**
- Modify: `BookWheel/Program.cs`
- Test: `BookWheel.Tests/BookWheelSmokeTests.cs` (new tests)
- Modify: `docs/audits/performance.md` (resolution note)
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:** none new — pure middleware configuration.

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b perf/compression-and-cache-headers origin/main
```

- [ ] **Step 2: Write the failing tests**

Add to `BookWheel.Tests/BookWheelSmokeTests.cs`:

```csharp
[Fact]
public async Task Static_Assets_Are_Served_With_Long_Lived_Immutable_Cache_Headers()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/js/app.js?v=test");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
    Assert.Contains("max-age=31536000", cacheControl, StringComparison.Ordinal);
    Assert.Contains("immutable", cacheControl, StringComparison.Ordinal);
    Assert.DoesNotContain("no-store", cacheControl, StringComparison.Ordinal);
}

[Fact]
public async Task Index_Html_Still_Uses_No_Store_Cache_Headers()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
    Assert.Contains("no-store", cacheControl, StringComparison.Ordinal);
}

[Fact]
public async Task Static_Js_Response_Is_Compressed_When_Client_Accepts_Gzip()
{
    using var factory = new BookWheelWebAppFactory();
    using var client = factory.CreateClient();

    var request = new HttpRequestMessage(HttpMethod.Get, "/js/app.js?v=test");
    request.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));

    using var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("gzip", response.Content.Headers.ContentEncoding.FirstOrDefault());
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Static_Assets_Are_Served|FullyQualifiedName~Index_Html_Still_Uses|FullyQualifiedName~Static_Js_Response_Is_Compressed"`
Expected: the cache-header tests FAIL (current header is `no-cache, no-store, must-revalidate` for everything); the compression test FAILS (no `Content-Encoding` header today).

- [ ] **Step 4: Add response compression**

Add `using Microsoft.AspNetCore.ResponseCompression;` to `Program.cs`'s using block.

Add, right after the `builder.Services.AddRateLimiter(...)` block (before `var app = builder.Build();` at line 138):

```csharp
builder.Services.AddResponseCompression(options =>
{
	// This app has no reflected user-controlled secrets in compressible responses
	// (the BREACH-attack scenario response compression normally disables for HTTPS
	// by default) — it's a fixed-shape SPA shell plus JSON APIs scoped to the
	// authenticated caller's own data. Safe to enable for HTTPS here.
	options.EnableForHttps = true;
	options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
	{
		"application/javascript",
		"application/manifest+json"
	});
});
```

- [ ] **Step 5: Call `UseResponseCompression()` early in the pipeline**

Add immediately after `app.UseRateLimiter();` (line 242), before the `app.UseRequestLocalization(...)` call:

```csharp
app.UseResponseCompression();
```

- [ ] **Step 6: Fix the static-file cache headers**

Replace the `UseStaticFiles` block (lines 288-297):

```csharp
app.UseStaticFiles(new StaticFileOptions
{
	ContentTypeProvider = staticFileContentTypeProvider,
	OnPrepareResponse = context =>
	{
		// These assets are always requested with a ?v=<version> cache-busting query
		// string (see index.html/sw.js), so it's safe to cache them indefinitely —
		// a new deploy changes the URL, not the file's cached bytes.
		context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
	}
});
```

(`index.html` and `sw.js` already get `no-store` from their own separate `MapGet` handlers — `WriteConfiguredIndexAsync`/`WriteConfiguredServiceWorkerAsync` — which are untouched by this change.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Static_Assets_Are_Served|FullyQualifiedName~Index_Html_Still_Uses|FullyQualifiedName~Static_Js_Response_Is_Compressed"`
Expected: PASS.

- [ ] **Step 8: Update `docs/audits/performance.md`**

Append directly after Finding F1's recommendation text and after F2's recommendation text:

```markdown
**Status (2026-09-06): Fixed.** `Program.cs` now enables `AddResponseCompression`/`UseResponseCompression` (gzip/brotli negotiated automatically, enabled for HTTPS) and static assets served through `UseStaticFiles` now get `Cache-Control: public, max-age=31536000, immutable` instead of `no-store`, matching the app's existing `?v=` cache-busting scheme. `index.html`/`sw.js` are unaffected (they're served via separate `MapGet` handlers that intentionally keep `no-store`).
```

- [ ] **Step 9: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump the patch version (adjust for whatever `main` has at branch time).

- [ ] **Step 10: Run the full test suite**

Run: `dotnet test BookWheel.slnx --verbosity normal`
Expected: all pass.

- [ ] **Step 11: Commit and push**

```bash
git add BookWheel/Program.cs BookWheel.Tests/BookWheelSmokeTests.cs docs/audits/performance.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
Enable response compression and fix static-asset cache headers

Static JS/CSS/icon assets were served with the same no-store headers
as index.html even though they're already cache-busted via a ?v=
query string specifically so they could be cached forever — the
versioning scheme's entire benefit was being thrown away. Also
enables response compression, which was completely absent despite
every client sending Accept-Encoding: gzip. Measured in the audit at
a combined ~127KB savings per page load and most of a 7x LCP
regression under simulated Fast 3G (docs/audits/performance.md,
2026-09-04, F1/F2).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin perf/compression-and-cache-headers
gh pr create --base main --title "Enable response compression and fix static-asset cache headers" --body "Closes SUMMARY.md High-priority item #6. See commit message for details."
```

---

### Task 7: Share one Postgres Testcontainer per test class instead of per test method (closes SUMMARY.md item #7 / Testing Finding H1)

**Files:**
- Modify: `BookWheel.Tests/BookWheelWebAppFactory.cs` (add `ResetAsync()`, make container startup awaitable via `IAsyncLifetime` instead of the constructor's blocking `GetAwaiter().GetResult()`)
- Modify: `BookWheel.Tests/BookWheelApiTests.cs`, `BookWheelFrontendTests.cs`, `BookWheelBrowserWorkflowTests.cs`, `BookWheelPwaTests.cs`, `BookWheelSmokeTests.cs` (convert each to `IClassFixture<BookWheelWebAppFactory>` + per-test reset via the class's own `IAsyncLifetime`)
- Check: `BookWheel.Tests/BookWheelHealthCheckTests.cs` (confirm its 0 occurrences of `new BookWheelWebAppFactory()` mean it needs no change — verify this rather than assuming)
- Modify: `docs/audits/testing.md` (resolution note)
- Modify: `BookWheel/BookWheel.csproj:9` (version bump)

**Interfaces:**
- Produces: `BookWheelWebAppFactory.ResetAsync()` — truncates all four application tables (mirrors `PostgresTestFixture.ResetAsync()`'s exact SQL).
- Consumes: xUnit's `IClassFixture<T>` (one instance per test class, shared across all its `[Fact]`/`[Theory]` methods) and `IAsyncLifetime` (per-test-method `InitializeAsync`/`DisposeAsync` hooks on the test class itself, since xUnit creates a new test class instance per test method even when the class fixture is shared).

- [ ] **Step 1: Branch**

```bash
git fetch origin
git checkout -b test/share-postgres-container-per-class origin/main
```

- [ ] **Step 2: Confirm `BookWheelHealthCheckTests.cs` needs no change**

```bash
grep -n "BookWheelWebAppFactory" BookWheel.Tests/BookWheelHealthCheckTests.cs
```

If this shows 0 occurrences of `new BookWheelWebAppFactory()`, it's out of scope for this task (it may use a different fixture, or no Postgres-backed test at all) — confirm and move on without modifying it. If it DOES show occurrences the earlier grep missed, treat it identically to the other 5 files below.

- [ ] **Step 3: Add `ResetAsync()` and an async-safe startup path to `BookWheelWebAppFactory`**

In `BookWheel.Tests/BookWheelWebAppFactory.cs`, add `using Microsoft.EntityFrameworkCore;` and `using BookWheel.Storage.Postgres;` to the top.

Replace the constructor (lines 31-46) — remove the blocking container start from the constructor entirely, since `IClassFixture<T>` supports proper async initialization via `IAsyncLifetime`, unlike the old per-test `new BookWheelWebAppFactory()` call sites which had no lifecycle hook available:

```csharp
public BookWheelWebAppFactory()
{
    _tempContentRoot = Path.Combine(Path.GetTempPath(), $"bookwheel-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempContentRoot);

    var tempWebRoot = Path.Combine(_tempContentRoot, "wwwroot");
    Directory.CreateDirectory(tempWebRoot);

    var sourceProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BookWheel"));
    var sourceWebRoot = Path.Combine(sourceProjectRoot, "wwwroot");
    CopyDirectory(sourceWebRoot, tempWebRoot);
}

public async Task StartAsync()
{
    await _postgresContainer.StartAsync();
}

public async Task ResetAsync()
{
    var optionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
    optionsBuilder.UseNpgsql(_postgresContainer.GetConnectionString());
    await using var context = new BookWheelDbContext(optionsBuilder.Options);
    await context.Database.ExecuteSqlRawAsync(
        "TRUNCATE TABLE books, password_reset_tokens, users, spin_selections RESTART IDENTITY CASCADE;");
}
```

(Existing call sites that still do `new BookWheelWebAppFactory()` inline — none should remain after Step 4, but if this PR ever needs to coexist temporarily with unconverted files, note that `StartAsync()` must now be called explicitly; the old synchronous-start behavior is gone.)

- [ ] **Step 4: Run the existing suite once to confirm nothing else references the old synchronous constructor behavior**

Run: `dotnet build BookWheel.slnx`
Expected: build FAILS in the 5 test files (they still call `new BookWheelWebAppFactory()` inline expecting it to be immediately usable) — this is expected at this point; proceed to Step 5, which fixes all 5 in one pass since they can't compile independently of each other via this shared factory change.

- [ ] **Step 5: Convert each of the 5 test classes to the shared-fixture pattern**

For **each** of `BookWheelApiTests.cs`, `BookWheelFrontendTests.cs`, `BookWheelBrowserWorkflowTests.cs`, `BookWheelPwaTests.cs`, `BookWheelSmokeTests.cs`, apply this identical transformation:

1. Add `using Xunit;` if not already present (it is, implicitly, via the SDK's xUnit package — no action needed there).
2. Change the class declaration from:
   ```csharp
   public sealed class BookWheelApiTests
   ```
   to:
   ```csharp
   public sealed class BookWheelApiTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
   {
       private readonly BookWheelWebAppFactory _factory;

       public BookWheelApiTests(BookWheelWebAppFactory factory)
       {
           _factory = factory;
       }

       public async Task InitializeAsync()
       {
           await _factory.StartAsync();
           await _factory.ResetAsync();
       }

       public Task DisposeAsync() => Task.CompletedTask;
   ```
   (repeat for each of the 5 class names — `BookWheelFrontendTests`, `BookWheelBrowserWorkflowTests`, `BookWheelPwaTests`, `BookWheelSmokeTests` — using that file's own class name.)

   Note: calling `_factory.StartAsync()` on every test's `InitializeAsync()` is safe and cheap after the first call — `Testcontainers.PostgreSql`'s `StartAsync()` is idempotent against an already-running container (it returns immediately if already started); this avoids needing a separate one-time-only startup hook.

3. Within every test method in the file, replace the line `using var factory = new BookWheelWebAppFactory();` with:
   ```csharp
   var factory = _factory;
   ```
   Do this as a literal find-and-replace across the whole file — this line pattern is identical everywhere it appears (confirmed via `grep -c "using var factory = new BookWheelWebAppFactory();"` per file before editing), and no other code in any test method needs to change, since every subsequent reference to the local variable `factory` continues to resolve correctly.

- [ ] **Step 6: Run the full build to confirm it compiles**

Run: `dotnet build BookWheel.slnx`
Expected: builds cleanly.

- [ ] **Step 7: Run the full test suite and compare timing against the pre-refactor baseline**

Run: `dotnet test BookWheel.slnx --verbosity normal`
Expected: all 281+ tests pass (plus the 5 new tests added by Tasks 1/5/6 if this branch is created after those have merged to `main` — rebase onto latest `main` before starting this task to pick them up), and the total wall-clock time drops sharply from the ~34.3-minute baseline recorded in `docs/audits/testing.md` (expect low single-digit minutes, since each of the 5 classes now starts one Postgres container total instead of one per test method).

- [ ] **Step 8: Update `docs/audits/testing.md`**

Append directly after Finding H1's text:

```markdown
**Status (2026-09-06): Fixed.** `BookWheelWebAppFactory` now supports `IClassFixture<T>`-based sharing (one Testcontainer per test class) with a `ResetAsync()` truncation step run before every test method via each class's own `IAsyncLifetime.InitializeAsync()`, mirroring the pattern `PostgresTestFixture` already used. All 5 affected classes (`BookWheelApiTests`, `BookWheelFrontendTests`, `BookWheelBrowserWorkflowTests`, `BookWheelPwaTests`, `BookWheelSmokeTests`) were converted; `BookWheelHealthCheckTests` needed no change (confirmed it held no per-test container instantiations).
```

- [ ] **Step 9: Bump the version**

In `BookWheel/BookWheel.csproj:9`, bump the patch version (adjust for whatever `main` has at branch time) — this is a test-infrastructure-only change with no production runtime impact, but every task in this pass gets a version bump per the Global Constraints.

- [ ] **Step 10: Commit and push**

```bash
git add BookWheel.Tests/BookWheelWebAppFactory.cs BookWheel.Tests/BookWheelApiTests.cs BookWheel.Tests/BookWheelFrontendTests.cs BookWheel.Tests/BookWheelBrowserWorkflowTests.cs BookWheel.Tests/BookWheelPwaTests.cs BookWheel.Tests/BookWheelSmokeTests.cs docs/audits/testing.md BookWheel/BookWheel.csproj
git commit -m "$(cat <<'EOF'
Share one Postgres Testcontainer per test class instead of per test method

The prior per-test-method pattern (new BookWheelWebAppFactory() inside
almost every [Fact]) created 150+ separate Postgres containers over a
full local test run, driving it to 34.3 minutes with 7 Docker-daemon-
timeout failures — a real risk against CI's 15-minute job timeout.
Converts the 5 affected WebApplicationFactory-based test classes to
IClassFixture<BookWheelWebAppFactory> with a per-test TRUNCATE reset,
mirroring the shared-fixture pattern PostgresTestFixture already used
elsewhere in the same test project (docs/audits/testing.md, 2026-09-04,
Finding H1).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014goecJ3SY4U6zLAnGq8QAB
EOF
)"
git push -u origin test/share-postgres-container-per-class
gh pr create --base main --title "Share one Postgres Testcontainer per test class" --body "Closes SUMMARY.md High-priority item #7. See commit message for details."
```

---

## Sequencing note

Tasks 5 and 6 both modify `BookWheel/Program.cs`, and Task 7 modifies every test file Task 5 adds tests to. **Implement and merge these seven tasks in order (1 → 2 → 3 → 4 → 5 → 6 → 7), fetching and rebasing onto the latest `main` before branching each subsequent task**, rather than branching all seven from today's `main` simultaneously — this avoids merge conflicts on `Program.cs` and the shared test files without needing a stacked-PR workflow. Tasks 1, 3, and 4 touch entirely disjoint files and could be done in parallel if a stacked/rebase workflow is preferred instead, but sequential is simpler to review one PR at a time, which matches the user's "one PR per fix" preference.
