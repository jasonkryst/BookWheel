# Open GitHub Issue Priority Tiers

**Date:** 2026-09-22
**Last updated:** 2026-09-22 (branch `feature/tier1-fixes`)
**Scope:** All 25 issues open at time of writing (`gh issue list --repo jasonkryst/BookWheel --state open`).

This re-tiers the open issue backlog by actual functional/security impact rather than by each source label alone. Issues #136–#154 and #122 originate from the 2026-09-04/06 comprehensive audit (see [`SUMMARY.md`](SUMMARY.md)) and already carry that audit's own Medium/Low tags; a few are promoted or demoted here based on real-world consequence (e.g. #150's service-worker bug is more than cosmetic; #146 is a genuine security disclosure). Issues #45–#95 predate the audit and are unscoped roadmap ideas with little or no spec — they need product scoping, not estimation, so they're kept in their own tier rather than mixed into 1–3.

Re-run `gh issue list --repo jasonkryst/BookWheel --state open` periodically and diff against this list — issues get closed, and new ones won't appear here automatically.

---

## Tier 1 — High impact, fix soon

1. ~~**[#136](https://github.com/jasonkryst/BookWheel/issues/136)** — Secure cookie forced `true` regardless of HTTPS.~~ ✅ Fixed on main before this branch (`Secure = Request.IsHttps`).
2. **[#137](https://github.com/jasonkryst/BookWheel/issues/137)** — `UsersController.DeleteUser` isn't transactional. A crash mid-sequence leaves orphaned book/spin-history rows tied to a deleted user. ✅ Fixed in `feature/tier1-fixes` (`UserManagementService.DeleteUserWithDataAsync` wraps all three steps in one EF Core transaction).
3. **[#146](https://github.com/jasonkryst/BookWheel/issues/146)** — Login responses leak account status (disabled/reset-required vs. invalid password). ✅ Fixed in `feature/tier1-fixes` (disabled and reset-required now return `401 "Invalid username or password."`; lockout keeps its `423 + lockoutEndsAtUtc`).
4. ~~**[#141](https://github.com/jasonkryst/BookWheel/issues/141)** — Light-theme primary buttons fail WCAG AA contrast (4.10:1 vs 4.5:1).~~ ✅ Fixed on main before this branch (`--accent: #027ab8`, 4.69:1).
5. **[#122](https://github.com/jasonkryst/BookWheel/issues/122)** — CodeQL log-injection/PII findings. ✅ Fixed in `feature/tier1-fixes` (`LogSanitizer.Sanitize()` applied to user-controlled values in Auth, SMTP, and metadata-lookup log calls).

## Tier 2 — Medium, worth scheduling

6. **[#150](https://github.com/jasonkryst/BookWheel/issues/150)** — Service worker precache list uses unversioned URLs while the app requests `?v=`-suffixed ones. Cache-first/offline PWA behavior is silently dead, not just a missing minification step.
7. **[#144](https://github.com/jasonkryst/BookWheel/issues/144)** — `/api/metrics` `spinCount:0` vs. real DB-persisted spin history. Confusing/misleading metric; needs a decision (bug vs. documented ephemeral counter).
8. **[#139](https://github.com/jasonkryst/BookWheel/issues/139)** — Redundant DB round-trips in spin/stats/books endpoints. Compounding cost as usage grows.
9. **[#147](https://github.com/jasonkryst/BookWheel/issues/147)** — Missing `(UserId, DeletedAtUtc)` composite index on `books`. Sequential scans that worsen as libraries grow.
10. **[#145](https://github.com/jasonkryst/BookWheel/issues/145)** — Missing baseline security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`). Trivial effort, real hardening.
11. **[#142](https://github.com/jasonkryst/BookWheel/issues/142)** — Missing admin self-modify-guard and authz-boundary tests. Guards exist and work today, but regressions would go undetected.
12. **[#138](https://github.com/jasonkryst/BookWheel/issues/138)** — README contradicts itself on credential storage (stale `App_Data` instructions vs. actual Postgres). Could mislead an operator mid-incident.
13. **[#140](https://github.com/jasonkryst/BookWheel/issues/140)** — No `Intl.PluralRules`. Visibly wrong grammar ("1 users", broken Polish plurals) for non-English users.
14. **[#143](https://github.com/jasonkryst/BookWheel/issues/143)** — Misleadingly named "browser" tests (no real DOM/JS engine) plus zero frontend coverage for Stats/spin-analytics.

## Tier 3 — Low, polish/hardening

15. **[#154](https://github.com/jasonkryst/BookWheel/issues/154)** — Wire `coverlet.collector` into CI + add concurrency tests (parallel spin, parallel lockout).
16. **[#148](https://github.com/jasonkryst/BookWheel/issues/148)** — Align Action pinning/version drift between `ci.yml`/`codeql.yml`.
17. **[#153](https://github.com/jasonkryst/BookWheel/issues/153)** — Stray `banner` landmark in Settings dialog; unverified badge `aria-label`.
18. **[#152](https://github.com/jasonkryst/BookWheel/issues/152)** — Delete-confirmation dialog defaults focus to the destructive button; focus-order anomaly on 2-button dialogs.
19. **[#151](https://github.com/jasonkryst/BookWheel/issues/151)** — Small i18n gaps (one hardcoded catch-block string, one uncataloged string, two hardcoded aria-labels, locale-inconsistent date formatting).
20. **[#150](https://github.com/jasonkryst/BookWheel/issues/150)** (minification half only — the SW-precache half is Tier 2 above) — JS/CSS served unminified.
21. **[#149](https://github.com/jasonkryst/BookWheel/issues/149)** — Log-shipping service has no offset tracking or backoff (duplicate delivery, retry storms).

## Tier 4 — Unscoped roadmap ideas (need product scoping before engineering work)

22. **[#95](https://github.com/jasonkryst/BookWheel/issues/95)** — Goodreads tie-in (CSV import path, since live API is dead). Most concrete of this group; already has a recommendation attached.
23. **[#58](https://github.com/jasonkryst/BookWheel/issues/58)** — "Questions" wheel — one-line feature idea.
24. **[#47](https://github.com/jasonkryst/BookWheel/issues/47)** — Enhanced self-account management — explicitly says "needs more details."
25. **[#48](https://github.com/jasonkryst/BookWheel/issues/48)** — Payment acceptance/tiers — open questions, no plan.
26. **[#46](https://github.com/jasonkryst/BookWheel/issues/46)** — Marketing front-end (pre-login).
27. **[#45](https://github.com/jasonkryst/BookWheel/issues/45)** — Affiliate links/ads integration.
28. **[#50](https://github.com/jasonkryst/BookWheel/issues/50)** — "Purchase Domain" — empty body, unclear intent.
29. **[#49](https://github.com/jasonkryst/BookWheel/issues/49)** — "Privatize Pipelines and Components" — empty body, unclear intent.

---

## Suggested next action

Tier 1 is five issues and all are cheap fixes (a conditional flag, a transaction wrapper, one shared error message, a CSS color tweak, one logging pass) — worth doing as a single short PR before anything else. Tier 4's #50 and #49 have empty bodies and should probably get a comment asking for scope, or be closed, before they linger.
