# BookWheel — Catch-All Audit: CI/CD, Containers, Observability, Docs, Licensing, and Project Health

Date: 2026-09-04
Scope: Everything not covered by the parallel security, testing, accessibility, i18n, performance, database, and core-functionality audits — CI/CD pipeline, Dockerfile/container hygiene, observability, error handling, documentation quality, third-party integrations (non-security angle), licensing, release process, and code organization.

This report references but does not duplicate `SECURITY_AUDIT_REPORT.md`'s existing "Addendum — CI Security Scanning" section; where this report expands on it, that's called out explicitly.

---

## 1. CI/CD Pipeline

### What gates are enforced before merge

`.github/workflows/ci.yml` runs 8 jobs on every push/PR to `main`, all gating (no `continue-on-error`):

| Job | Purpose |
|---|---|
| `actionlint` | Lints all workflow YAML for syntax/logic errors |
| `secret-scan` | Gitleaks secret scanning across full history (`fetch-depth: 0`) |
| `build` | `dotnet build` |
| `unit-tests` | `dotnet test` |
| `dependency-audit` | Runs `scripts/check-vulnerable-packages.sh` over both `.csproj` files |
| `docker-build` | Builds the production Docker image |
| `container-smoke-test` | Boots the built image against a real Postgres container and polls `/health/ready` |
| `trivy` | Scans the built image; hard-fails on fixable CRITICAL/HIGH CVEs, uploads full SARIF (all severities) to the Security tab |

`codeql.yml` runs separately (own workflow, not a `ci.yml` job) on push/PR to `main` **and** a weekly Monday 08:00 UTC schedule, using the `security-extended` query suite — broader than the default `security-and-quality` set. This is a sound configuration: extended queries catch more SAST classes, and the weekly cron catches newly-disclosed CVEs against unchanged code between pushes.

`Dependabot` (`.github/dependabot.yml`) is present and covers both ecosystems that matter here: `nuget` (weekly, Monday, 5 PR cap) and `github-actions` (same cadence/cap) — the latter is what keeps SHA-pinned actions current, which matters given the finding below.

Concurrency (`cancel-in-progress` per workflow+ref) and per-job timeouts are configured in both `ci.yml` and `codeql.yml`, which is good CI hygiene (avoids wasted runner minutes on superseded pushes and hung jobs).

### Action pinning — inconsistent, and worst on the highest-privilege workflow

The repo's own security posture calls for SHA-pinning (evidenced by `ci.yml`'s Docker/security-tool actions being pinned), but pinning is inconsistent both across and within workflows:

- **`ci.yml`**: `docker/setup-buildx-action`, `docker/build-push-action`, `gitleaks/gitleaks-action`, `aquasecurity/trivy-action`, and `github/codeql-action/upload-sarif` are all pinned to full commit SHAs. But `actions/checkout@v7` and `actions/setup-dotnet@v6` are floating major-version tags.
- **`codeql.yml`**: `github/codeql-action/init` and `/analyze` are SHA-pinned, but `actions/checkout@v4` and `actions/setup-dotnet@v4` are floating — and notably pinned to *older* major versions than the same two actions in `ci.yml` (v4 vs v7, v4 vs v6), a sign the two workflows have drifted out of sync rather than being maintained together.
- **`docker-release.yml`**: **none** of its actions are SHA-pinned — `actions/checkout@v7`, `docker/setup-buildx-action@v3`, `docker/login-action@v4` (used twice), and `docker/build-push-action@v7` are all floating tags. This is the workflow with `packages: write` permission and real registry credentials (`DOCKERHUB_TOKEN`, `GITHUB_TOKEN`) that pushes images consumed by end users of the project — it is simultaneously the most sensitive workflow and the least hardened one. A compromised or re-tagged upstream action here has a direct path to shipping a tampered image to `jasonkryst/bookwheel` and `ghcr.io/.../bookwheel`.

**This is the top finding of this audit.** `SECURITY_AUDIT_REPORT.md`'s CI addendum documents Trivy and CodeQL pinning accurately but doesn't mention gitleaks/actionlint, Dependabot, or this cross-workflow pinning inconsistency — this report fills that gap.

### Minor CI observations

- `scripts/check-vulnerable-packages.sh` gates on `dotnet list --vulnerable` output by grepping for the literal string `"has the following vulnerable packages"` in the CLI's human-readable text (the script's own header comment explains this is necessary because `dotnet list --vulnerable` always exits 0). This works today but is inherently fragile — a wording change in a future .NET SDK's console output would silently defeat the gate rather than fail loudly. Consider preferring a `--format json` output if/when the SDK supports a stable machine-readable form for this command.
- The version-format-drift risk flagged in a `BookWheel.csproj` comment ("a version format change here can silently break 4 of 7 CI jobs") **is** guarded correctly today: `ci.yml`'s `build`, `docker-build`, `container-smoke-test`, and `trivy` jobs all validate `InformationalVersion` against `^[0-9]+(\.[0-9]+){0,3}$` before using it, and `docker-release.yml` independently fails the release if the release tag's version doesn't match the csproj value. This is a well-designed, verified safeguard — not just a comment.

---

## 2. Dockerfile & Container Hygiene

`Dockerfile` is a clean two-stage build: `mcr.microsoft.com/dotnet/sdk:8.0` for restore/publish, `mcr.microsoft.com/dotnet/aspnet:8.0` for the runtime image. The final image:

- Contains no SDK/build tooling (multi-stage discards the build stage).
- Runs as the non-root `$APP_UID` user (the standard .NET 8 base-image mechanism), with `App_Data`/`DataProtection-Keys` explicitly `chown`'d to that user before the `USER` switch — correct ordering.
- Declares `VOLUME` for both persistent paths and listens on HTTP-only internally (`8080`), with a comment correctly noting TLS should terminate upstream.

One gap: both base images are pinned only to the `8.0` floating tag, not to an image digest (`@sha256:...`). This means the exact base layer contents can change between builds without any Dockerfile diff. Trivy scanning every CI build largely mitigates the risk (a newly-vulnerable base would be caught), but digest pinning is the stronger supply-chain practice and would also make the "which run of `docker-build` used which base" question answerable without cross-referencing CI logs.

`docker-compose.yml` is reasonable: named volumes for app data, DP keys, and Postgres data; a Postgres healthcheck gating the app's `depends_on`; a documented local-dev-only password default overridable via `.env`; `restart: unless-stopped`. No secrets are committed.

`.dockerignore` excludes build artifacts, `.git`, IDE state, and the two legacy file-storage paths (`user.cred`, `books.json`) plus `App_Data/logs/`. It does **not** exclude `BookWheel.Tests/`, `docs/`, `IMPROVEMENT_ROADMAP.md`, or other non-runtime repo content — but since the Dockerfile's build stage does `COPY . .` after the initial `.csproj`-only restore step, this only affects build-context upload size/time, not what ends up in the final runtime layer (which only receives `/app/publish`). Low impact; worth tightening for build performance and as a guardrail against a future Dockerfile edit that adds a broader `COPY`.

---

## 3. Observability & Operations

**Structured logging** (`JsonFileLoggerProvider`/`JsonFileLogger`): writes one JSON object per line (JSONL) with timestamp, category, level, event id, message, exception, and a captured-properties dictionary built from the structured log call's template arguments. Retention and rotation are both implemented and match `appsettings.json` (`Security:LogRetentionDays: 14`, `Security:LogMaxFileSizeMb: 5`) — a daily sweep deletes files older than the retention window, and a new numbered file (`bookwheel-YYYY-MM-DD-N.jsonl`) is opened once the active file exceeds the size cap. Both are sane defaults for a self-hosted app.

One caveat worth noting: `JsonFileLogger.BeginScope<TState>` always returns a no-op `NullScope` — logger scopes are never captured into the JSON output. `Program.cs` does push a `CorrelationId`/`Path`/`Method` scope around each request (line ~218) via `BeginScope`, but because the file sink ignores scopes entirely, that scope data only reaches the log file for the two log statements that also pass `correlationId` as an explicit structured argument (the "Request started"/"Request completed" lines). Any other log statement emitted mid-request that relies on ambient scope data (rather than passing `RequestId`/`CorrelationId` explicitly, as most controller logs correctly do today) would not carry correlation data in the persisted JSONL. Not currently causing a gap in the codebase (spot-checked controllers pass `HttpContext.TraceIdentifier` explicitly), but it's a latent trap for future log statements added inside `BeginScope` blocks that assume the scope will be honored.

**`/api/metrics`** (`MetricsController`) is correctly admin-gated — unauthenticated callers get 401, authenticated non-admins get 403 — before returning login/lockout/spin/book-count counters. Good.

**Log shipping** (`LogShippingService`, optional, `Observability:LogShipping`, disabled by default): a `BackgroundService` that polls on a configurable interval (min 5s enforced) and POSTs the tail of the current day's log file to an external HTTP sink. Two resilience gaps:
1. **No offset/cursor tracking.** Each pass reads the *last N lines* (`BatchSize`, min 10) of the active log file and ships them, with no record of what was already shipped in the prior pass. If fewer than `BatchSize` new lines accumulated between polls, already-shipped lines get re-sent (duplicate delivery to the sink); if more than `BatchSize` new lines accumulated, older lines in that window are silently skipped and never shipped. The endpoint being down for one poll cycle, or a burst of log volume, both produce silent gaps or dupes with no way to detect or recover them — the receiving sink must handle idempotency and gap-detection on its own, which is undocumented as a requirement.
2. **No backoff/retry.** A failed POST just logs a warning and waits for the next fixed-interval poll; there's no exponential backoff or dead-letter handling, so a sink outage produces a steady stream of warning-level noise rather than escalating or pausing.

Neither is severe (this is an optional, off-by-default convenience feature, not a compliance-grade shipping pipeline), but both are worth calling out since the feature currently doesn't guarantee exactly-once (or even reliably-at-least-once) delivery.

**Startup diagnostics** (`StartupDiagnosticsService`) validates writability of `App_Data`, `App_Data/logs`, and `App_Data/corrupt` at boot via a write-probe-and-delete, and applies best-effort Linux/macOS permission hardening (`rwxr-x---`) to the logs directory, logging a `LogCritical` (not throwing) on failure — a reasonable choice that surfaces the problem loudly in logs/health checks without crash-looping the container. Windows gets a logged reminder only (ACLs aren't set programmatically), which is appropriately scoped since the shipped container only ever runs on Linux.

**Health checks**: `DatabaseHealthCheck` (Postgres `CanConnectAsync`) and `LoggingHealthCheck` (log-directory write probe) both catch exceptions and report `Unhealthy` with the exception attached rather than letting the health-check middleware itself fault — correct pattern.

---

## 4. Error Handling & Resilience

**No global exception-handling middleware is registered.** `Program.cs` calls `AddControllers()` but never `AddProblemDetails()`, `UseExceptionHandler(...)`, or `UseDeveloperExceptionPage()`, and no custom exception-handling middleware/filter exists anywhere in the codebase. In ASP.NET Core 8, this means an unhandled exception escaping a controller action gets the framework's bare default behavior rather than a controlled, consistently-shaped error response — there is no single place enforcing "never leak a stack trace in Production" or "always return a consistent JSON error shape." Individual controllers (e.g., `AuthController`) do their own targeted `try`/`catch` around known failure modes (like `CorruptedDataException`), which covers the well-understood failure paths, but any *unanticipated* exception has no safety net. Given this is called out as an app-level concern, the security audit may also flag the leakage angle; from a resilience/ops standpoint, the absence of a catch-all handler means every new endpoint added in the future inherits no baseline error-shape or logging guarantee unless its author remembers to add one.

**Corrupted-data quarantine**: `CorruptedDataException` is a minimal marker exception (message-only, no extra fields) thrown by `JsonCredentialRepository`, `JsonBookRepository`, and `JsonPasswordResetTokenRepository` when a legacy JSON payload fails to parse. `QuarantineCorruptFileUnsafe` moves the bad file aside (into `App_Data/corrupt`, matching what `StartupDiagnosticsService` provisions and the README documents) and the exception surfaces up to callers like `AuthController`, which logs a specific "credential storage corruption" audit event with `RequestId` before returning a controlled error to the client — this specific path is well-handled. Note this machinery now only fires against the **legacy JSON migration path** (see §5 below on the credential-storage documentation contradiction) — the live Postgres-backed repositories used at runtime don't go through this quarantine logic, since a live Postgres connectivity failure surfaces through `DatabaseHealthCheck` instead.

---

## 5. Documentation Quality

`README.md` (592 lines) and `IMPROVEMENT_ROADMAP.md` (217 lines) were both read in full.

### Internal contradiction: credential storage location

This is the most concrete staleness finding. The README's own "Data Storage" section (line 309) correctly states:

> Book, credential, and password-reset-token data is stored in PostgreSQL... EF Core migrations run automatically at startup.

But the earlier "First-Run Account Setup" → "Credential storage details" section (lines 280–307) still describes the pre-Postgres-migration behavior as current:

> Account records are stored in `BookWheel/App_Data/user.cred`... The record is encrypted at rest with ASP.NET Core Data Protection... If you delete `BookWheel/App_Data/user.cred`, the app will prompt for first-run setup again.

This is now inaccurate: `Program.cs` binds `ICredentialRepository` to `PostgresCredentialRepository`, and `AuthController.GetStatus` determines `setupRequired` from `hasAccount` against that Postgres-backed store, not from the presence of a file. `JsonCredentialRepository` still exists in the codebase but is only reachable through the legacy migration utility (`--migrate-data`/`--migrate-to-postgres`), not the live request path. The "Troubleshooting" section compounds this — "verify whether `BookWheel/App_Data/user.cred` exists," and "delete `BookWheel/App_Data/user.cred` and create a new account" — both of which no longer reset anything in a Postgres-backed deployment and would mislead an operator trying to recover access. This whole block reads as leftover documentation from before the Postgres migration (roadmap Priority 5, item 4, correctly marked `[Done]`) that wasn't updated when the migration shipped.

### Spot-checked API routes vs. controller source

The "API Overview" section's route list was spot-checked against `AuthController`, `UsersController`, `MetricsController`, `BooksController`, and `StatsController` — routes, verbs, and admin-gating descriptions (`/api/metrics` admin-only, `/api/stats/aggregate` admin-only, `/api/users/*` admin-only) all matched the actual `[Route]`/`[HttpX]` attributes and in-method authorization checks. No other route-level discrepancies found beyond the credential-storage contradiction above.

### Roadmap plausibility

`IMPROVEMENT_ROADMAP.md`'s `[Done]` markers were spot-checked against the codebase and are consistent with what's actually implemented: Postgres-backed repositories (§ above), health checks (`HealthChecks/`), Trivy/CodeQL CI jobs, log retention/rotation (`JsonFileLoggerProvider`), forwarded-headers config, and soft-delete on books all match code that exists and is wired up. Unlike the README, the roadmap's "Current Strengths" list accurately reflects the Postgres migration (it explicitly notes "logs and Data Protection keys remain file-based" as the carve-out) — the roadmap is the more up-to-date of the two documents.

### Undocumented default: Google Analytics ID

`BookWheel/appsettings.json` ships a hardcoded default `Analytics:GoogleAnalyticsId: "G-JQXW826H1F"`, which `Program.cs` injects into `wwwroot/index.html` (replacing `__GOOGLE_ANALYTICS_ID__`) and which loads `googletagmanager.com/gtag/js` unconditionally in every deployment that doesn't override the setting. Neither `README.md` nor any other doc in the repo mentions `Analytics`, `GoogleAnalyticsId`, or Google Analytics at all — it's not listed in the "Features" list, the configuration/settings guidance, or anywhere in "Getting Started"/"Container Support." (The security audit is covering the privacy/attack-surface angle of this same setting; this note is specifically about the documentation gap.) For a maintainer running their own instance this is presumably intentional. For the open-source angle that matters here: anyone who clones/forks the repo and deploys via `docker run`, `docker-compose up`, or `dotnet run` without reading `appsettings.json` source will silently start sending their users' page-view telemetry to the original maintainer's GA property, with no README section, environment-variable callout, or setup-checklist item telling them this setting exists, what it does, or how to blank it out. This is a straightforward doc fix — add a line to "Getting Started" or a new "Analytics" subsection noting `Analytics:GoogleAnalyticsId` should be overridden or cleared for a forked deployment.

---

## 6. Third-Party Integration: Open Library Metadata Lookup

`OpenLibraryBookMetadataLookupService` (both `LookupByIsbnAsync` and `LookupByTitleAsync`) is well-defended for a best-effort, user-triggered feature:

- `HttpClient` is registered with an explicit `client.Timeout = TimeSpan.FromSeconds(8)` (`Program.cs`) and a descriptive `User-Agent` header pointing back at the project repo — the latter is good etiquette for a public API like Open Library that may rate-limit or block generic/anonymous clients.
- Both methods narrowly catch `HttpRequestException`, `TaskCanceledException` (covers the timeout case), and `JsonException`, logging a warning and returning `null`/`[]` rather than propagating — so an Open Library outage, timeout, or malformed response degrades the ISBN/title-lookup feature gracefully instead of raising a 500 to the caller. A non-success HTTP status is likewise treated as "no match" rather than an error.
- No retry/backoff and no circuit breaker — acceptable here since this is a synchronous, user-initiated, single-shot lookup (not a background job), so there's no risk of a retry storm; a slow-but-eventually-failing Open Library just costs the caller up to 8 seconds once per lookup attempt, which is reasonable UX for an optional convenience feature.

No changes recommended; this integration is appropriately scoped for its risk profile.

---

## 7. Licensing & Project Metadata

**No `LICENSE` file exists anywhere in the repository root or elsewhere in the tree**, and `README.md` never mentions licensing terms. There is also no `CONTRIBUTING.md` or `CODE_OF_CONDUCT.md`. For a project distributed publicly on GitHub with Docker Hub/GHCR image publishing and a CI badge suite advertising it as open-source, this is a meaningful gap: under default copyright law, the absence of an explicit license means the project is technically "all rights reserved" — other developers cannot safely fork, modify, or redistribute it (including republishing built Docker images) without the maintainer's explicit permission, regardless of the repo being public. This is likely an oversight rather than intentional, given the project publishes ready-to-run Docker images and documents a fork/deploy workflow in the README (version stamping, Docker build args, etc.) that implicitly assumes downstream use. Adding a `LICENSE` file (MIT/Apache-2.0 are the common choices for a project like this) would resolve the ambiguity; a short `CONTRIBUTING.md` would also help now that CI has PR-facing gates (actionlint, gitleaks, Trivy, CodeQL) that an external contributor would otherwise have to discover by trial and error.

---

## 8. Release Process

The release process (README's "Version Stamping" and "Automated Docker Publish" sections, `docker-release.yml`) is well-defined and low-risk in its version-consistency safeguards:

- Single source of truth (`BookWheel.csproj`'s `InformationalVersion`) is enforced by validation in both `ci.yml` (regex check in 4 jobs, per the csproj comment) and `docker-release.yml` (fails the release job outright if the GitHub Release tag's version doesn't match the csproj value) — this directly addresses the format-drift risk the csproj comment warns about, and it's a genuine gate, not just documentation.
- The `Release Checklist` in the README is a reasonable manual sequence (bump version → test → security regression → vulnerability scan → verify Trivy/CodeQL → build → verify readiness/login → verify volumes → confirm observability config) for a project this size, though it's manual/human-executed rather than automated — nothing in CI enforces that a human actually walked through it before tagging a release.
- The one process gap worth flagging is the action-pinning issue already covered in §1: `docker-release.yml` is the workflow that actually pushes released images to Docker Hub/GHCR under real credentials, and it's the least pinned of the three workflows.

---

## 9. Code Organization / Architecture Quality (high-level)

Separation of concerns (Controllers → Services → Storage/repositories) is consistently applied across the backend: controllers are thin and delegate to services (`AuthService`, `AppMetricsService`, etc.) or directly to repository interfaces (`IBookRepository`, `ICredentialRepository`), and the Postgres repositories under `Storage/Postgres/` are appropriately sized (61–289 lines) with no obvious God-class. `BooksController.cs` (351 lines) and `AuthController.cs` (278 lines) are the largest controllers but are proportionate to the number of distinct endpoints/responsibilities each owns (books CRUD + spin + lookup + export/import; auth + setup + password-reset + lockout logging), not obviously overloaded.

The one file worth flagging for organization is the frontend: `BookWheel/wwwroot/js/app.js` is a single 2,624-line vanilla-JS file covering essentially the entire client application (this is a deliberate architectural choice — no build step, no bundler, no framework — and is reasonable for the project's stated scope, but it means there's no per-feature module boundary on the client side the way there is on the server side).

`TODO`/`FIXME`/`HACK` search: **none found** across `.cs`, `.js`, `.html`, and `.css` files in `BookWheel/`. Either the codebase has no unaddressed known-gaps left as inline markers, or the team tracks such gaps exclusively via `IMPROVEMENT_ROADMAP.md`/GitHub issues rather than code comments — the roadmap document supports the latter interpretation, since it explicitly lists open, non-`[Done]` items (search/filter, self-service password change, external OIDC, etc.) as its own gap-tracking mechanism.

---

## Additional Observations

- A 38 KB `scratch_translations.js` file sits at the repository root (not under `wwwroot/`, `scripts/`, or any other conventional location) and isn't covered by any `.gitignore`/`.dockerignore` pattern. Given the filename, it reads as a working/scratch artifact rather than shipped code; worth confirming it's meant to be there and, if not, removing it or adding it to `.gitignore`.
- `docker-compose.yml`'s bundled Postgres uses `postgres:16-alpine` unpinned to a digest (same floating-tag pattern as the app's own base images in §2) — consistent with the rest of the project's tag-pinning posture, flagged here only for completeness since it's outside the Dockerfile itself.
- The project has good self-audit hygiene overall: `SECURITY_AUDIT_REPORT.md` and `IMPROVEMENT_ROADMAP.md` are both actively maintained, dated, and cross-referenced from the README's "Project Documents" section — this audit continues that pattern via `docs/audits/`.
