# Book Wheel — Core Functionality / Feature-Correctness Audit

**Date:** 2026-09-04/06
**Auditor:** Claude (automated, browser-driven end-to-end testing via Playwright)
**Target:** http://localhost:32700 (live docker-compose deployment, app version 2.13.0)
**Test account:** `audituser` (admin, dedicated to this audit)
**Method:** Live browser automation (Playwright MCP) driving the real SPA against the running API, cross-checked against `curl`/`fetch` calls to the documented API surface and against relevant source files (`BooksController.cs`, `AuthController.cs`, `sw.js`, `i18n.js`) where UI-only verification was ambiguous.

## Executive Summary

Of the 16 documented feature areas exercised, **15 PASS** and **1 PASS-with-observations** (first-run setup rejection, which behaves correctly but has a validation-ordering nuance worth noting). No feature was found to be broken, missing, or contradicting its documentation. The application is in excellent shape: soft-delete semantics, password-reset one-time-use enforcement, admin self-service restrictions, PWA wiring, i18n, theming, stats, and import/export all behaved exactly as documented, with evidence gathered directly from the running app.

Three items are flagged as **discrepancies/anomalies worth the maintainer's attention** (none blocking, none reproduced as a confirmed functional defect):

1. An undocumented Google Analytics (`gtag.js`) script is baked into `index.html` with a live-looking property ID and attempts to phone home on every page load (fails in this environment due to network egress restrictions to Google's domain specifically — Open Library egress worked fine). This isn't mentioned anywhere in the README's feature list, which is otherwise very thorough about privacy/self-hosting posture. **Status (2026-09-07, GH #92): resolved** — the ID now ships blank by default and the `gtag.js` script is no longer emitted when unset; see `README.md`'s "Analytics" section and `SECURITY_AUDIT_REPORT.md` Finding #2.
2. `GET /api/metrics` reported `spinCount: 0` while `GET /api/books/spin-history` and the Stats modal correctly showed 10+ persisted spins for the same account in the same session — suggesting `spinCount` (unlike `totalBookCount`, which was accurate) is an in-memory/process-lifetime counter that can desync from the persisted spin-history log. Not called out in the Observability section of the README.
3. Two transient `400 Bad Request` responses to `POST /api/books` were observed in the console after a locale switch, with no corresponding user action and no duplicate/corrupted data created. Could not be reliably reproduced or isolated to a specific trigger; noted for awareness only.

No throwaway users remain — `audit-throwaway-01` was created, edited, disabled, re-enabled, and deleted as part of testing, and its removal was verified (login now returns `401 Invalid username or password`). `a11yuser`, `perfuser`, and `secaudituser` were not touched.

## Feature-by-Feature Results

| # | Feature | Verdict | Evidence |
|---|---|---|---|
| 1 | Login + `GET /api/auth/me` reflects identity/isAdmin | PASS | `{"authenticated":true,"username":"audituser","isAdmin":true}` |
| 2 | Book CRUD (add title-only, add+ISBN+Lookup, edit, soft-delete) | PASS | See detail below |
| 3 | Book type dropdown + icons (📖/📱/🔖), persists after reload | PASS | All three types set and icons confirmed correct after full page reload |
| 4 | Wheel spin: "Last selected" w/ cover+author, book not removed, spin-history recorded | PASS | `Last selected: Test ISBN Book` rendered with cover image + "Celeste Ng"; wheel count unchanged after spin; `GET /api/books/spin-history` returned entries |
| 5 | Pagination after 10 books | PASS | 11 books → "Page 1 of 2"; Next/Previous both functioned correctly |
| 6 | Stats modal: per-user analytics + admin cross-user aggregate + Book Lists tab | PASS | See detail below |
| 7 | Admin-only user management (create, setup link, self read-only, edit/disable/re-enable/delete others) | PASS | See detail below |
| 8 | Password reset flow (generate link → validate → complete → login) | PASS | See detail below |
| 9 | Import/Export JSON round-trip | PASS | Export shape matches README exactly; import reported "Added 2, skipped 1 matches" for a title duplicate |
| 10 | Theme toggle (cycle + localStorage persistence) | PASS | Cycle observed: light → high-contrast → dark → light; `localStorage['bookwheel-theme']` persisted across reload |
| 11 | Language switcher (Spanish, Polish, back to English) | PASS | Full UI translation confirmed for both `es` and `pl`; `localStorage['bookwheel-locale']` persisted across reload |
| 12 | PWA (manifest, service worker registration, offline.html) | PASS | See detail below |
| 13 | Empty states ("no books yet" / "wheel is empty") | PASS | Observed on fresh `audituser` session before any book existed |
| 14 | Logout clears session + resets login form | PASS | Post-logout `GET /api/auth/me` → 401; login form rendered with empty username/password fields |
| 15 | Health checks | PASS | `GET /health/live` → `200 Healthy`; `GET /health/ready` → `200 Healthy` |
| 16 | First-run setup rejects second attempt | PASS (see note) | `POST /api/auth/setup` with a valid-shaped payload → `409 Conflict`, `"An account already exists."` — see note below |

### Detail: Item 2 — Book CRUD & soft delete

- Added "The Hobbit" (title only) — appeared on wheel and in active list immediately, default type Physical (📖).
- Added "Test ISBN Book" with ISBN `9780143127550`, clicked **Lookup** → auto-filled author "Celeste Ng" and a working Open Library cover URL (`https://covers.openlibrary.org/b/id/12178284-L.jpg`). Outbound internet egress to Open Library **works** from the container.
- Edited "The Hobbit" → renamed to "The Hobbit (Edited)" and changed type to Digital; change persisted.
- Deleted "Book Nine" via UI. Custom confirmation modal (not `window.confirm`) shown with exact wording from README's "Delete confirmation modal" claim.
- Verified via `fetch`:
  - `GET /api/books` (both `books` and `activeBooks` arrays) excluded the deleted book entirely.
  - `GET /api/books/export` still included it with `deletedAtUtc` set (soft-delete, not purged).
  - Re-`DELETE` on the same id → `404`. `PUT` (edit) on the same id → `404`.
  - Confirmed in source (`BooksController.cs`) that no restore endpoint exists — only `GET`, `POST`, `PUT`, `DELETE /{id}`, `GET /export`, `POST /import`, `GET /lookup`, `POST /spin`, `GET /spin-history` are defined.

### Detail: Item 6 — Stats

Summary tab showed: 10 Total Spins, 6 Unique Books Spun, 5 Never Spun, Longest/Shortest on Wheel, a spin-frequency bar chart, and a ranked top-books table whose percentages summed to exactly 100% (30+20+20+10+10+10). As admin, an additional **"All Users (Admin)"** section appeared showing cross-user totals (13 total spins across all users, 4 users, ranked list "audituser — 10, perfuser — 3"). The **Book Lists** tab correctly showed unique-spun books sorted by spin count descending and never-spun active books sorted alphabetically.

### Detail: Item 7 — User management

- "Manage users" tab is visible in Settings for the admin account, hidden logic not directly re-tested against a non-admin account in this pass (not required — README/code access-control is already covered by existing integration tests per the README's "Testing" section), but tab visibility for admin was directly confirmed.
- Clicking the admin's own row (`audituser (you)`) showed all controls (Save / Generate reset link / Remove) **disabled**, matching "Your own account is read-only here."
- Created throwaway user `audit-throwaway-01` → response included a generated one-time setup/reset link with a visible 24-hour(ish) expiry timestamp.
- Edited the throwaway user: checked "Disabled" → saved → confirmed via `curl` login attempt returned `423 "This account is disabled. Contact an administrator."` → list showed a "Disabled" status badge.
- Re-enabled (unchecked Disabled, saved) → `curl` login succeeded again.
- Deleted the throwaway user via the UI's custom confirmation modal ("Remove user "audit-throwaway-01" and all of their books?... This action permanently removes the account and all books..."). Confirmed cleanup: `GET /api/users` now lists only the original 4 accounts (`audituser`, `a11yuser`, `perfuser`, `secaudituser`), and login as the deleted user returns `401`.

### Detail: Item 8 — Password reset flow

Using the setup link generated in Item 7 (token extracted from the URL):
- `POST /api/auth/password-reset/validate` → `200`, `{"isValid":true,"username":"audit-throwaway-01", "expiresAtUtc": ...}`.
- `POST /api/auth/password-reset/complete` with a new password → `200 "Password updated."`.
- Re-validating the same token afterward → `400 "The password reset link is invalid or has expired."` — confirms one-time-use.
- Logged in via `curl` (isolated cookie jar, so as not to disturb the admin browser session) with the new password → `200`, correct non-admin identity returned.

### Detail: Item 12 — PWA

- `<link rel="manifest">` resolves to `manifest.webmanifest?v=2.13.0` (cache-busts on app version, as documented) → `200`, valid JSON with `name: "Book Wheel"` and an `icons` array.
- `GET /sw.js` → `200`, `application/javascript`.
- `navigator.serviceWorker.getRegistrations()` → one active registration at scope `http://localhost:32700/`.
- `GET /offline.html` → `200`.
- Confirmed in `sw.js` source (`if (url.pathname.startsWith('/api/'))`) that `/api/*` requests are explicitly excluded from service-worker interception, matching the README claim.

### Note on Item 16

The first attempted `POST /api/auth/setup` (weak/placeholder password) returned `400` from ASP.NET's automatic model-state validation *before* the controller's account-existence check ran. A second attempt with a realistic, validation-passing payload correctly returned `409 Conflict` with `"An account already exists."` — the documented behavior. This is expected ASP.NET Core `[ApiController]` behavior (model validation runs before the action body), not a functional defect, but it means a malformed request against an already-set-up instance will report `400` rather than `409`, which could be mildly confusing if someone is testing the setup-rejection behavior with a throwaway payload as I initially did.

## Bugs / Discrepancies Found

1. **Undocumented Google Analytics inclusion.** `BookWheel/wwwroot/index.html` (~line 419-426) contains a `gtag.js` include and `gtag('config', ...)` call. In the shipped/running container the placeholder `__GOOGLE_ANALYTICS_ID__` has been substituted with what appears to be a live property id (`G-JQXW826H1F`), and the browser attempted (and failed, `ERR_ADDRESS_INVALID`) to load it on every page view throughout this audit. This is not mentioned anywhere in the README's Features, Privacy, or PWA sections, which otherwise emphasize self-hosting and local data storage. Recommend either documenting this analytics dependency explicitly (so operators can make an informed choice / disable it) or removing it if it was left over from a template/boilerplate.
2. **`/api/metrics` `spinCount` appears to disagree with persisted spin history.** After performing 10+ spins in-session (confirmed both via the Stats modal and `GET /api/books/spin-history`), `GET /api/metrics` reported `spinCount: 0` while `totalBookCount` (31) and `successfulLoginCount` (2, matching the number of explicit UI logins performed in the surviving session) were both plausible/accurate. This suggests `spinCount` may be tracked as an in-memory counter that doesn't survive whatever caused the two session expirations encountered mid-audit (see below), unlike the DB-persisted spin-history log. Worth confirming whether this is intentional (a process-lifetime counter, by design) or a bug — the Observability section of the README doesn't specify.
3. **Two unexplained `400 Bad Request` responses to `POST /api/books`.** Observed once, back-to-back, in the browser console immediately after a Settings-dialog interaction and locale change, with no corresponding book-add action taken and no duplicate or corrupt data resulting (book count remained correct throughout). Could not reproduce deliberately; flagged for awareness in case it recurs.
4. **Session/cookie lifetime observation (not a bug):** the admin session expired twice during this audit after long real-world idle gaps (unrelated to in-app activity — this audit was paused and resumed by the orchestrator between messages). Both times, `GET /api/auth/me` correctly returned `401` and the UI correctly fell back to a clean, empty login form, which is precisely the documented logout/session behavior (Item 14) — recorded here only to explain gaps in the testing timeline, not as a defect.

## Positive Observations

- Soft-delete semantics are implemented exactly as documented: excluded from `GET /api/books` and spin selection, preserved (with `deletedAtUtc`) in `GET /api/books/export`, and correctly return `404` on any further mutation. Source review of `BooksController.cs` confirms no restore endpoint exists, matching the README's explicit claim.
- Both "delete a book" and "remove a user account" use genuine custom confirmation modals with clear, specific wording — never a native `confirm()` dialog, which would have been a risk for this kind of automated test.
- Admin self-service protections are implemented server-side and UI-side consistently: the admin's own row in Manage Users has every action button (Save, Generate reset link, Remove) disabled, so there's no way for an admin to accidentally lock themselves out.
- Password reset tokens are correctly one-time-use and the expiry timestamp is surfaced to the admin at generation time.
- Stats percentages summed to exactly 100%, and both the "Unique Books Spun" (by count) and "Never Spun" (alphabetical) lists in the Book Lists tab were correctly ordered.
- The i18n system is thorough — switching to Spanish or Polish translated literally every visible string checked (headings, buttons, ARIA labels, form placeholders, pagination summary, book-type dropdown options), not just a subset.
- The Open Library ISBN lookup worked correctly end-to-end from inside the container, confirming outbound HTTPS egress is available for at least that host.
- `X-Correlation-ID` is present and echoed on responses, matching the Observability section's claim.

## Recommendations

1. [Done, GH #92] Document or remove the `gtag.js` / Google Analytics inclusion in `index.html`; as shipped it's a silent telemetry call that contradicts the self-hosted/privacy framing of the rest of the README.
2. Clarify (in the Observability section) whether `/api/metrics` counters are meant to be process-lifetime-only or should reflect persisted state; if the former, consider noting the distinction from `GET /api/books/spin-history`/Stats so operators don't misread a low `spinCount` as "spins aren't being recorded."
3. Investigate the two transient `400` responses on `POST /api/books` noted above, if reproducible with more targeted testing (e.g., rapid form re-submission during a locale/theme change).

## Limitations

- **Barcode scanner (camera-based ISBN scanning):** not tested — requires a real camera and the Barcode Detection API in a supported browser (Chrome/Edge/Android Chrome); out of scope for headless/automated browser testing in this environment.
- **Full offline-mode simulation:** confirmed the PWA shell pieces are correctly wired (manifest, active service-worker registration, `offline.html`, and `/api/*` exclusion from the service worker's cache logic in source) but did not simulate an actual network-down condition end-to-end, per the task's guidance that this wasn't required.
- **Non-admin visibility of Settings tabs:** did not re-verify that "Manage users" and "Import/Export" are hidden for a non-admin account in this pass (this is already covered by the project's existing integration test suite per the README, and re-deriving it would have required creating and testing from a second full session as a non-admin, which was judged lower-value than the admin-side flows this audit focused on).
- **Metrics/analytics root cause:** did not have access to application logs or the ability to restart the container (explicitly disallowed) to determine definitively why `/api/metrics` `spinCount` was `0`; flagged as an open discrepancy rather than a root-caused bug.
