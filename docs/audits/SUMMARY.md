# BookWheel Comprehensive Audit — Summary

**Date:** 2026-09-04 to 2026-09-06
**Status:** All 8 audits complete.

This summary synthesizes findings across all eight audits, cross-references overlapping findings, and gives one prioritized action list. Each audit's full detail lives in its own file (linked throughout); nothing here replaces them.

| Audit | File | Status |
|---|---|---|
| Security | [`SECURITY_AUDIT_REPORT.md`](../../SECURITY_AUDIT_REPORT.md) (new "Full Security Audit — 2026-09-04" section) | ✅ Complete |
| Database | [`database.md`](database.md) | ✅ Complete |
| i18n | [`i18n.md`](i18n.md) | ✅ Complete |
| Performance | [`performance.md`](performance.md) | ✅ Complete |
| Other Areas (CI/CD, Docker, observability, docs, licensing) | [`other-areas.md`](other-areas.md) | ✅ Complete |
| Testing | [`testing.md`](testing.md) | ✅ Complete |
| Accessibility (a11y) | [`a11y.md`](a11y.md) | ✅ Complete |
| Core Functionality | [`core-functionality.md`](core-functionality.md) | ✅ Complete |

---

## How this audit was run

Eight parallel agents worked against a live docker-compose instance of the app (PostgreSQL + the ASP.NET Core API/SPA) using dedicated test accounts (`audituser`/admin, `a11yuser`, `perfuser`, plus throwaway accounts for lockout/user-management testing), combining static code review with live HTTP/browser/database verification. No source code was modified, no containers were restarted, and no shared test data was altered outside each agent's own scope. Three agents (Testing, Accessibility, Core Functionality) were interrupted twice by Claude account rate limits and resumed from their in-progress transcripts once the limits reset; this is noted here for provenance, not because it affected result quality — each resumed agent completed its full original task list.

---

## Cross-Cutting Themes

Several issues surfaced independently from more than one audit, which increases confidence they're real and worth prioritizing:

- **Google Analytics hardcoded default — the most corroborated finding in this entire exercise.** Flagged by **three independent audits** from three different angles: **Security** (privacy/attack-surface: ships telemetry to the original developer by default), **Other Areas** (documentation: not mentioned anywhere in the README, so forkers have no way to know it exists), and **Core Functionality** (live, functional: directly observed the browser attempting to load `gtag.js` and phone home with a live-looking property ID on every single page view during testing, contradicting the README's otherwise-thorough self-hosting/privacy framing). Three audits, three methods, one root cause: `appsettings.json`'s `Analytics:GoogleAnalyticsId` should not ship with a real, working ID as the default.
- **Missing index on `books.DeletedAtUtc`** — flagged independently by **Database** (data-integrity/indexing angle) and **Performance** (query-cost angle). Both recommend the same fix: a composite `(UserId, DeletedAtUtc)` index.
- **Postgres privilege model** — the existing `SECURITY_AUDIT_REPORT.md`/`IMPROVEMENT_ROADMAP.md` already tracked "the app needs DDL privileges" as a known Low-priority item. The **Database** audit's live query revealed this is actually understated: the runtime role is a full PostgreSQL **superuser**, not just DDL-capable — a materially larger blast radius that should reprioritize this item upward.
- **Action pinning inconsistency** — the **Other Areas** audit found this is worst on exactly the workflow (`docker-release.yml`) that has real registry write credentials and publishes images end users pull — the opposite of where lax pinning is most tolerable.
- **"Tested" doesn't always mean what it sounds like.** The **Testing** audit found that files named `BookWheelBrowserWorkflowTests`/`BookWheelFrontendTests` never execute a real browser, DOM, or JS engine — they string-match served source text. Separately, the **Accessibility** and **Core Functionality** audits *did* drive a real browser end-to-end and caught things the string-match tests structurally cannot (a live contrast failure, a focus-order glitch, a metrics/spin-history discrepancy, an actual outbound network call). This isn't a contradiction — it's the exact gap the Testing audit predicted, independently confirmed by two audits that used real browser automation where the test suite doesn't.

---

## Prioritized Action List (across all 8 audits)

### High priority

1. [Done, GH #92] **Ship an empty default `Analytics:GoogleAnalyticsId`, or remove the Google Analytics include entirely.** The single most-corroborated finding across this audit (see Cross-Cutting Themes above) — confirmed by static review *and* live observation that it actually attempts to phone home on every page load. *(Security Finding #2, Other Areas §5, Core Functionality Bug #1.)*
2. **Split the runtime PostgreSQL role from superuser to least-privilege DML**, with a separate elevated role used only for `MigrateAsync()`/schema migrations. *(Database, finding #12 — reprioritizes an existing roadmap item from Low to High given the actual grant is superuser, not just DDL-capable.)*
3. **SHA-pin the actions in `docker-release.yml`** (`actions/checkout`, `docker/setup-buildx-action`, `docker/login-action`, `docker/build-push-action`) — this is the workflow with `packages: write` and real Docker Hub/GHCR credentials that ships images to end users, and it's currently the least-pinned workflow in the repo. *(Other Areas, §1.)*
4. **Add a `LICENSE` file.** A publicly distributed, forkable, Docker-image-publishing project with no license is technically all-rights-reserved by default — this blocks legitimate reuse the project's own README/workflow otherwise assumes. *(Other Areas, §7.)*
5. **Route ASP.NET Core's automatic model-validation (400) errors through the existing localizer**, or confirm/enable the framework's own localized DataAnnotations resources. Live-confirmed to return raw English text to Spanish/Polish users on the most-used form in the app (adding a book). *(i18n, F1.)*
6. **Enable HTTP response compression** and **fix static-asset cache headers** (currently `no-store` on everything, including versioned `?v=` assets designed to be cached forever). A ~127KB-per-load fix that accounts for most of a measured 7x LCP regression (259ms → 1,843ms) under simulated real-world network conditions. *(Performance, F1/F2.)*
7. **Share one PostgreSQL Testcontainer per test class instead of per test method.** The current per-method pattern (used by ~230 of 281 tests) drove a full local run to 34.3 minutes and produced 7 Docker-daemon-timeout failures — a real risk against CI's 15-minute job timeout. A working shared-container pattern already exists elsewhere in the same test project (`PostgresTestFixture`) and just needs to be applied here too. *(Testing, H1.)*

### Medium priority

8. **Change the `Secure` auth-cookie flag to depend on `Request.IsHttps`, or explicitly document and warn when Production runs without TLS termination.** Currently forced `true` unconditionally, which works only because of the `localhost` browser exemption — a LAN-IP or hostname plain-HTTP deployment (a plausible way to self-host this app) would see login silently fail. *(Security, Finding #1.)*
9. **Wrap `UsersController.DeleteUser`'s three-step delete/purge-books/purge-spin-history sequence in a single transaction.** Currently three independent `SaveChangesAsync` calls across three DbContexts — a crash mid-sequence orphans data. *(Database, finding #7.)*
10. **Fix the README's internal contradiction about credential storage.** "First-Run Account Setup" and "Troubleshooting" still describe the pre-migration `App_Data/user.cred` file as the live source of truth; "Data Storage" correctly says it's Postgres now — an operator following the stale Troubleshooting instructions would take an action that no longer resets anything. *(Other Areas, §5.)*
11. **Consolidate redundant backend queries**: `POST /api/books/spin` fetches the full book list twice per request; `GET /api/stats` makes 5 round trips where 2-3 would do; `GET /api/books` and the spin response both duplicate the entire book list under two JSON keys. *(Performance, B2/B3/B4.)*
12. **Adopt `Intl.PluralRules` for count-bearing strings** — fixes a grammatically-wrong Polish plural form for common counts and a "1 users"/"1 usuarios"/"1 użytkowników" bug present in all three locales. *(i18n, F4.)*
13. **Fix the light-theme primary-button contrast failure** (4.10:1, needs 4.5:1) — affects every accent-colored primary button (Log in, Add, Spin, Save, etc.) in the light theme only; dark and high-contrast themes pass comfortably. Live-verified against actual computed styles, not just source CSS. *(a11y, 1.4.3.)*
14. **Add the two missing admin self-modify guard tests** (cannot update own account, cannot generate own reset link — only "cannot delete own account" is currently tested) **and API-level tests for the `ForcePasswordReset`/account-lockout login blocks.** Cheap to write, closes real authorization-boundary test gaps. *(Testing, M1/M2.)*
15. **Re-scope the "browser-level" test naming** to reflect that these are string-match tests against served source, not executed-browser tests, and **add frontend test coverage for the Stats/spin-analytics UI**, which currently has zero. *(Testing, H2/H3.)*
16. **Investigate the `/api/metrics` `spinCount: 0` discrepancy** against the correctly-persisted spin-history log (10+ real spins recorded and visible in the Stats modal, but not reflected in the metrics counter) — likely an in-memory/process-lifetime counter that's desynced from DB-persisted state; clarify in the Observability docs whether this is by design. *(Core Functionality, Bug #2.)*

### Low priority

17. Add baseline HTTP security headers (`X-Content-Type-Options: nosniff`, `X-Frame-Options`/`frame-ancestors` CSP, `Referrer-Policy`). *(Security, Finding #3.)*
18. Collapse the disabled/locked/reset-required login response into the same generic invalid-credentials response as a wrong password, to avoid an account-status oracle for a caller who already has a correct password. *(Security, Finding #5.)*
19. Add a composite `(UserId, DeletedAtUtc)` index on `books` — recommended independently by two audits. *(Database finding #10 + Performance B5.)*
20. Align action-pinning/version drift across `ci.yml` and `codeql.yml`. *(Other Areas, §1.)*
21. Add offset/cursor tracking and backoff to the optional log-shipping service. *(Other Areas, §3.)*
22. Add a minification/bundling step for `app.js`/`i18n.js`/`site.css`, and fix the service worker's precache list to use the same versioned URLs the app actually requests. *(Performance, F3/F4.)*
23. Small i18n polish: one un-localized controller catch block, one uncataloged translation string, two hardcoded `aria-label`s, browser-locale-dependent date formatting. *(i18n, F2/F3/F5/F6/F7.)*
24. Default delete-confirmation dialogs to focus "Cancel" instead of the destructive button, and investigate a focus-order anomaly (an invisible stop on `<body>`) when tabbing through 2-button dialogs. *(a11y, findings under 2.4.3 and 3.2/3.3.)*
25. Neutralize a stray `banner` landmark nested inside the Settings dialog, and verify the book-type-badge's `aria-label` announces reliably with a real screen reader. *(a11y, 1.3.1.)*
26. Wire `coverlet.collector` (already referenced but unused) into CI so future coverage gaps are visible without a manual audit, and add a small number of concurrency tests (parallel spins, parallel failed logins near the lockout threshold). *(Testing, M4/M5.)*
27. **Watch for recurrence, no action yet**: two unexplained transient `400` responses to `POST /api/books` observed once during core-functionality testing, not reliably reproducible, no data corruption resulted. *(Core Functionality, Bug #3.)*

---

## What's Working Well (cross-audit positive findings)

- **No SQL injection, no IDOR, no XSS-exploitable pattern anywhere in the codebase** — confirmed by grepping the entire tree and reviewing every relevant code path. *(Security.)*
- **Password hashing, reset-token entropy, single-use enforcement, lockout, and IP rate-limiting all verified working correctly, live.** *(Security.)*
- **Dependency scans clean; CodeQL/Trivy/Dependabot/gitleaks/actionlint all well-configured** across 8 CI gating jobs. *(Security, Other Areas.)*
- **Database migration history is clean** — no drift from the live schema, and username uniqueness is correctly enforced at both the app and DB layer. *(Database.)*
- **No N+1 query pattern anywhere in the repository layer**, and LCP is genuinely fast in absolute/local terms (162-259ms) — the delivery-pipeline gaps are the bottleneck, not the app's own logic. *(Performance.)*
- **i18n has 100% structural translation parity** across all three locales (224 frontend keys, 26 backend keys) — the gaps found are about which code paths route through the translation system, not missing translations. *(i18n.)*
- **Docker container hygiene is solid** — clean multi-stage build, non-root user, no baked-in secrets. *(Security, Other Areas.)*
- **The test suite is unusually disciplined for a project this size**: 281 tests, real PostgreSQL via Testcontainers (no mocking framework beyond one unavoidable HTTP boundary), zero skipped/dead tests, consistent self-documenting naming, and strong negative-path coverage on soft-delete, ISBN validation, and corrupted-data quarantine. *(Testing.)*
- **BookWheel is substantially conformant with WCAG 2.1 AA**, with genuinely deliberate accessibility engineering: a working skip link, correctly announced form errors, a keyboard-operable spin wheel with a full non-visual result path (live regions plus a screen-reader-only book list), a complete WAI-ARIA tabs pattern, native `<dialog>` modals instead of hand-rolled traps or blocking `alert()`, and `prefers-reduced-motion` honored in both CSS and the JS spin-timing logic. Only one confirmed contrast failure and a handful of minor/moderate issues were found. *(a11y.)*
- **Every one of 16 documented core features was verified working end-to-end via live browser automation**, exactly as documented — soft-delete, password-reset one-time-use, admin self-service lockout protection, PWA wiring, i18n, theming, stats math (percentages summed to exactly 100%), and import/export round-trip all matched their documentation with no functional defects found. *(Core Functionality.)*

---

## Notes on This Audit's Own Execution

Testing, Accessibility, and Core Functionality were each interrupted twice mid-run by the Claude account's session and then weekly usage limits (the weekly reset landed 2026-09-06, 4:00am America/Chicago). Each was resumed from its own in-progress transcript rather than restarted, so no work was lost or duplicated — the final reports reflect each agent's originally-scoped task list completed in full, just across three separate work sessions instead of one continuous run. This is noted for transparency about how the audit was produced, not because it affects confidence in the findings themselves.
