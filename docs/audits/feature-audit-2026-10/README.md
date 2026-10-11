# BookWheel Feature Audit — October 2026

**Date:** 2026-10-11 · **Version audited:** 3.6.0 (`feature/security-hardening` @ `d525043`) · **Method:** static review of the full codebase (API, SPA, storage, CI and Docker), cross-checked against the eight technical audits in [`docs/audits/`](../SUMMARY.md) and `IMPROVEMENT_ROADMAP.md`

**Tracking epic:** [#247](https://github.com/jasonkryst/BookWheel/issues/247) · **Issues:** #177–#246 and #248–#249 (label `audit:feature-2026-10`) · **Security:** 2 private advisories (AUTH-1, OPS-1)

| Companion files | Purpose |
|---|---|
| [`design-master-library.md`](design-master-library.md) | Design for the shared catalog, per-user master library, and manual/smart wheels (#248, #207) |
| [`issue-map.json`](issue-map.json) | Audit key → GitHub issue number |
| `advisories.local.mjs` (gitignored) | Full write-ups for the two private advisories. Never commit this file. |
| [`ISSUES.md`](ISSUES.md) | Every recommendation with its GitHub issue link, by priority and by feature |
| [`issue-catalog.mjs`](issue-catalog.mjs) | Source of truth for the 74 recommendations (impact, complexity, phase, acceptance criteria) |
| [`file-issues.mjs`](file-issues.mjs) | Idempotent script that creates the labels, issues and tracking epic from the catalog |

---

## 1. How to read this audit

The eight earlier audits were organised by **technical dimension** (security, database, a11y, performance…). This one is organised by **feature**. For each feature it asks:

1. **What does it do today?** (current capabilities)
2. **Usability:** is it pleasant and obvious to use?
3. **Sustainability:** can it be maintained, extended and operated cheaply?
4. **Functionality:** what is missing or broken, including natural adjacent features?

Every recommendation is scored on two axes and given the product phase it serves.

| Impact | Meaning |
|---|---|
| **High** | Fixes a security or correctness bug, or removes a daily pain point for most users or operators |
| **Medium** | Clear improvement for a meaningful subset of users, or reduces real maintenance cost |
| **Low** | Polish, or matters only at larger scale |

| Complexity | Rough size |
|---|---|
| **Small** | ≤ 1 day, localized change |
| **Medium** | 2–5 days, touches several layers or needs a migration |
| **Large** | 1–3 weeks, or needs a design doc |

**Priority tier** = `impact×2 − complexity` (H=3, M=2, L=1; S=1, M=2, L=3):

| Tier | Score | Typical combo | Count |
|---|---|---|---|
| **1 — Quick wins** | 5 | High / Small | 8 |
| **2** | 4 | High / Medium | 8 |
| **3** | 3 | Medium / Small, High / Large | 19 |
| **4** | 2 | Medium / Medium | 20 |
| **5 — Later** | ≤1 | Low / any, Medium / Large | 19 |

**Phases** (per the owner's direction): **1 — Self-hosted now**, **2 — Public face**, **3 — SaaS**. Within a tier, phase 1 comes first.

---

## 2. Executive summary

BookWheel is in **good engineering health for its size**. It has a disciplined integration-test suite on real PostgreSQL, CI with secret scanning and CodeQL, least-privilege DB roles, thoughtful anti-enumeration in account recovery, a11y and i18n passes, and a PWA shell. The feature set has grown well beyond "spin a wheel": scanner, two metadata providers, Book Search with retailer links, stats, admin user lifecycle, and email recovery.

The audit found **three things worth fixing immediately** and **one structural theme**.

### Fix first (Tier 1 bugs)

| Key | Finding |
|---|---|
| **AUTH-1** 🔒 | Session-lifecycle security finding, tracked as a **private security advisory** until fixed. |
| **OPS-1** 🔒 | Reverse-proxy deployment security finding, tracked as a **private security advisory** until fixed. |
| **WHEEL-1** | The core feature doesn't scale visually: there's no `devicePixelRatio` scaling (blurry on every phone), labels are a fixed 18px and truncated at 18 characters, and the wheel is unreadable past about 30 books. |

### The structural theme: the app has outgrown its single-wheel, single-file, in-memory-session shape

Most adjacent features users will expect (reading status, tags, spin filters, multiple wheels, sharing, the "wheel of questions" in #58) all push against the same three constraints:

1. **One implicit wheel per user.** Books *are* the wheel. → **CORE-10** (Wheel entity), then CORE-11 (sharing).
2. **A 3,200-line `app.js`** with global state and source-text-only tests. Every UI feature lands here. → **CORE-4** (ES modules + Vitest), **CORE-5** (Playwright + axe).
3. **In-memory sessions and hand-rolled auth checks.** Restarts log everyone out, there's no multi-instance support, and every action has a copy-pasted `GetAuthenticatedUser` (22 copies). → **CORE-1** (auth middleware + policies), **CORE-2** (persistent sessions).

Fixing these in phase 1, while the user base is one household, is far cheaper than after a public launch.

### Recommended sequence

| Milestone | Goal | Issues |
|---|---|---|
| **M1 — Trust** (≈1 week) | Close the security and correctness gaps | AUTH-1, OPS-1, AUTH-2, TRANSFER-1, TRANSFER-2, ACCOUNTS-2, STATS-2 |
| **M2 — Daily usability** (≈2 weeks) | The things a household notices every day | WHEEL-1, LIBRARY-1, LIBRARY-2, AUTH-3, ACCOUNTS-1, OPS-2, LIBRARY-4, WHEEL-5 |
| **M3 — Foundations** (≈3–4 weeks) | Make further growth cheap | CORE-1, CORE-2, CORE-5, CORE-4 (incremental), CORE-6, CORE-7, CORE-3 |
| **M4 — Master library** (≈4–5 weeks) | Add once, use on any wheel; wheels stay current by themselves ([design](design-master-library.md)) | CORE-16, LIBRARY-3, LIBRARY-5, LIBRARY-6, CORE-10 (manual + smart wheels), WHEEL-4 |
| **M4b — Richer reading** (≈2 weeks) | Turn "a random picker" into "my reading companion" | WHEEL-2, TRANSFER-4, LOOKUP-1, STATS-1 |
| **M5 — Public face** | Safe to open the doors (#46) | CORE-12, PREFS-4, CORE-13, AUTH-4, ACCOUNTS-3/4, CORE-15, LOOKUP-6 |
| **M6 — SaaS** | Ready for #48 | CORE-14, CORE-11, ACCOUNTS-5, OPS-4, AUTH-6/7 |

---

## 3. Feature-by-feature analysis

### 3.1 Authentication & Sessions

**Current capabilities:** first-run setup creates the admin; cookie login (HttpOnly, SameSite=Strict, 8h); per-IP fixed-window rate limit on login (5/min); per-username lockout (5 failures → 3 min, configurable); admin-set disable, lock and force-reset flags; login responses collapsed to avoid an account-status oracle (#146).

| | Assessment |
|---|---|
| **Usability** | Users are logged out at exactly 8h after login even mid-use: the cookie expiry is fixed while the server session slides (**AUTH-2**). There's no "keep me signed in" and no way to change your own password (**AUTH-3**). |
| **Sustainability** | Sessions, lockout counters and reset rate-limits live in `ConcurrentDictionary`s on a singleton. Every deploy logs everyone out and resets lockouts, and the app can't scale past one instance (**CORE-2**). Authorization is a hand-copied preamble in 22 actions (**CORE-1**). |
| **Functionality** | One finding is tracked privately (**AUTH-1** 🔒). No session visibility (**AUTH-5**), 2FA (**AUTH-6**), external IdP (**AUTH-7**) or password-strength checking (**AUTH-4**). |

### 3.2 Account Recovery & Email

**Current capabilities:** forgot-password (by username) and forgot-username (by email) with identical responses and fire-and-forget sends for timing safety; admin-generated 24h reset and setup links; required, unique email per account; non-dismissible "add your email" prompt for legacy admins; MailKit SMTP; reset links built only from `App:BaseUrl` (host-header-injection safe).

| | Assessment |
|---|---|
| **Usability** | New users invited by an admin receive an email titled **"Reset your Book Wheel password"** (**ACCOUNTS-2**). Emails are English-only plain text (**ACCOUNTS-3**). Users can't see or change their own email (**ACCOUNTS-4**). |
| **Sustainability** | When SMTP or BaseUrl is missing, everything fails silently by design, but admins get no signal either. Self-hosters discover it during a lockout (**ACCOUNTS-1**). |
| **Functionality** | No retry or outbox, so a transient SMTP failure loses the email (**ACCOUNTS-5**). No email verification. |

### 3.3 User Administration

**Current capabilities:** admin-only directory with search and status filters; create user with setup link; edit username, email, role and flags; generate reset link; transactional delete of the user and all their data (#137); guards against self-demotion, removing the last admin and deleting the first account; JSONL audit logging.

| | Assessment |
|---|---|
| **Usability** | No last-login or library-size columns, so dormant accounts are invisible and deletion impact is only known afterwards (**ADMIN-1**). |
| **Sustainability** | The audit trail lives only in rotating log files (14 days) inside the container (**ADMIN-2**). |
| **Functionality** | Solid for a household. Gaps appear only at scale (bulk actions, paging), so they're deferred and not filed. |

### 3.4 Book Library Management

**Current capabilities:** add, edit and remove (soft-delete) books with title, ISBN (checksum-validated), author, cover URL and type (Physical/Digital/Nook Only); scanner badge; client-side pagination (10/page); empty-state guidance; delete confirmation.

| | Assessment |
|---|---|
| **Usability** | No search, sort or filter, and the default order is by random GUID, so it looks unordered (**LIBRARY-1**). Removing a book can't be undone and there's no Trash, even though soft-delete keeps the data (**LIBRARY-2**). Manual add allows exact duplicates (**LIBRARY-4**). The empty state could launch the fastest add paths (**LIBRARY-10**). |
| **Sustainability** | Book types and providers are hard-coded in four places (`[Range(1,3)]`, JS icon maps, HTML options, dispatcher dictionary) (**LIBRARY-7**). Covers are hot-linked from third parties: a privacy leak, links break, and they're unavailable offline (**LIBRARY-6**). |
| **Functionality** | Books are binary: on the wheel or removed. There's no reading status (**LIBRARY-3**), tags (**LIBRARY-5**), notes or page count (**LIBRARY-9**), or bulk edit (**LIBRARY-8**). |

### 3.5 Spin Wheel

**Current capabilities:** canvas wheel with themed slices; server-side uniform random selection (the client only animates to the server's pick, which is good for integrity); entropy shuffle on add; reduced-motion path; keyboard-activatable canvas with an SR summary list; every pick recorded to `spin_selections`.

| | Assessment |
|---|---|
| **Usability** | Rendering doesn't scale (**WHEEL-1**). The result disappears on reload even though history exists server-side, and no UI reads `/spin-history` (**WHEEL-5**). The result has no actions, so the user can't "start reading" or "spin again" (**WHEEL-4**). |
| **Sustainability** | The result appears after a fixed `setTimeout(4200)` rather than `transitionend`, so it can desync from the animation (**WHEEL-6**). |
| **Functionality** | You can't spin a subset (**WHEEL-2**), weight books, or avoid repeats (**WHEEL-3**). There's only one wheel per user (**CORE-10**). |

### 3.6 Metadata Lookup, Barcode Scanner & Book Search

**Current capabilities:** ISBN and title lookup via Open Library or Google Books with a per-user preferred provider and automatic fallback; a multi-result picker; native `BarcodeDetector` scanner (EAN-13/8, camera flip, reticle); Book Search dialog with retailer links (Google Books, Open Library, Goodreads, B&N, Amazon) and "Add to Wheel".

| | Assessment |
|---|---|
| **Usability** | The scanner doesn't work on **iOS Safari or Firefox**, which covers most iPhone users (**LOOKUP-1**). Adding a shelf is one scan, then a manual Add, each time (**LOOKUP-4**). A provider outage costs about 8s before fallback (**LOOKUP-5**). |
| **Sustainability** | Two near-identical endpoints (`/lookup`, `/info`) and two parallel picker and renderer implementations in the SPA (**LOOKUP-3**). Provider IDs are hard-coded (**LIBRARY-7**). |
| **Functionality** | No caching and no per-user rate limit on outbound lookups, so a signed-in user can turn the server into a proxy against third-party quotas (**LOOKUP-2** 🔒). Retailer links are US-only with no affiliate hook (**LOOKUP-6**, supports #45). |

### 3.7 Stats & Insights

**Current capabilities:** per-user totals (spins, unique, never-spun), longest and shortest on wheel, top-books bar chart and table, type breakdown, unique-spun and never-spun lists; admin cross-user aggregate.

| | Assessment |
|---|---|
| **Usability** | All-time only, with no trends or ranges (**STATS-1**). Single-book "longest/shortest" cards are less actionable than a "most neglected" list (**STATS-3**). |
| **Sustainability** | Stats compute in 6+ queries plus in-memory joins. That's fine at household scale and was revisited in #139. |
| **Functionality** | "Active users" counts every account (**STATS-2**). There's nothing shareable for a public launch (**STATS-4**). |

### 3.8 Import / Export & Backup

**Current capabilities:** JSON export (books including deleted, spin history, schema version); JSON import (array or `{books}`) with title and ISBN de-duplication including soft-deleted titles; account-mismatch warning.

| | Assessment |
|---|---|
| **Usability** | Import commits immediately with no preview (**TRANSFER-6**). Results are only counts, with no indication of which rows were skipped (**TRANSFER-3**). No CSV, so Goodreads and StoryGraph users can't bring their TBR (**TRANSFER-4**, #95). |
| **Sustainability** | Import is one DbContext and `SaveChanges` per row, non-transactional and uncapped (**TRANSFER-3**). |
| **Functionality** | **Bugs:** `bookTypeId` is unvalidated on import (**TRANSFER-1**). Importing into a new account or instance brings back removed books as active (**TRANSFER-2**). Export isn't a real backup because history is dropped on import (**TRANSFER-5**). Instance-level backups are documented but not automated (**OPS-2**). |

### 3.9 Preferences, Theming, i18n & Analytics

**Current capabilities:** dark, light and high-contrast themes (server-persisted); en/es/pl UI with `Intl.PluralRules` and localized API errors; analytics opt-out; preferred book-info provider.

| | Assessment |
|---|---|
| **Usability** | There's no "follow system" theme, so the first-visit system choice gets pinned (**PREFS-2**). Language doesn't follow the user across devices (**PREFS-1**). |
| **Sustainability** | All locales ship in one 1,010-line file. Translations were never reviewed by native speakers. Adding a language means editing three places (**PREFS-3**). |
| **Functionality** | With a GA ID set, analytics is **on by default** and loads before any consent. That's acceptable on a private instance, but the opt-out is hard to find (**PREFS-4**). The owner keeps opt-out by default; revisit before an EU-facing launch. |

### 3.10 PWA & Offline

**Current capabilities:** installable manifest and icons; versioned precached app shell; offline fallback page; online and offline toasts; `/api/*` never cached.

| | Assessment |
|---|---|
| **Usability** | Offline shows the shell with no books and no spin (**PWA-2**). A new deploy silently takes over open tabs (**PWA-1**). There are no install shortcuts or share target (**PWA-3**). |
| **Sustainability** | Good: the version-stamped cache name and immutable assets are well designed. |

### 3.11 Deployment & Operations

**Current capabilities:** multi-stage Dockerfile running as non-root; compose with Postgres, least-privilege roles, named volumes and a PG healthcheck; liveness and readiness health endpoints; JSONL logs with rotation; optional HTTP log shipping; admin metrics endpoint; CI (actionlint, gitleaks, build, tests with coverage, CodeQL, vulnerable-package check); release workflow publishing to Docker Hub and GHCR with a version guard.

| | Assessment |
|---|---|
| **Usability (for operators)** | Reverse-proxy setup is undocumented, and one related finding is tracked privately (**OPS-1** 🔒). There are no automated backups and no app healthcheck in compose (**OPS-2**). Users aren't told what changed after an upgrade (**OPS-3**). |
| **Sustainability** | Custom metrics and log shipping duplicate what OpenTelemetry gives for free, and the counters reset on restart (**OPS-4**). The 47 KB README mixes changelog, user docs and ops docs (**CORE-15**). |

---

## 4. Whole-application review

### 4.1 Architecture

```
Browser SPA (index.html + app.js 3.2k + i18n.js 1k + site.css 1.5k)
   │  fetch /api/*  (cookie session, Accept-Language)
   ▼
ASP.NET Core 10 controllers (7) ── AuthService (singleton, in-memory sessions/lockouts)
   │                              ├─ Metadata dispatcher → Open Library / Google Books
   │                              └─ AccountEmailService → MailKit SMTP
   ▼
Repositories (interface + Postgres impl; legacy JSON impls still registered)
   ▼
PostgreSQL (EF Core migrations at startup; no FK constraints by convention)
```

**What works well:** clear repository seams; pooled `DbContextFactory`; startup migrations under a separate schema-owning role; cache-busted immutable static assets; minimal dependencies (MailKit + Npgsql only).

**Where it strains:**

| Concern | Evidence | Core issue |
|---|---|---|
| Auth is a convention, not a mechanism | 22 manual `GetAuthenticatedUser` checks | **CORE-1**, CORE-2 |
| Process-local state | Sessions, lockouts, reset windows and metrics all in memory | **CORE-2**, OPS-4 |
| Dead code paths | 3 JSON repositories (~1,300 lines), 2 migration services, an anonymous-pre-setup migration endpoint, a migration that runs on every startup | **CORE-7** |
| Integrity by convention | No FKs; the import path already writes an invalid FK value | **CORE-6** |
| Inconsistent error contract | `{message}` vs ValidationProblemDetails vs empty 401/403; resx keyed by English sentences | **CORE-8** |
| Redundant payloads | `GET /api/books` returns the list twice; spin re-reads the list | **CORE-9** |

### 4.2 Frontend

The vanilla-JS, no-build-framework approach is a **legitimate strength**: tiny dependency surface and fast loads. The problem is not the absence of a framework but the absence of **modules**. With about 185 element lookups at file top, global mutable state and every feature in one file, a change to (say) the scanner can break the stats dialog, and the tests won't notice because they assert on source text. **CORE-4** keeps vanilla JS and splits by feature into native ES modules. **CORE-5** adds behavioural safety (Playwright + axe) *before* the split so the refactor is protected. Do CORE-5 first.

### 4.3 Security posture

Strong fundamentals: PBKDF2 hashing via `PasswordHasher`, hashed reset tokens, anti-enumeration, IP and username rate limits, host-header-safe links, least-privilege DB, security headers, CodeQL and gitleaks. The remaining gaps are mostly **session lifecycle** (AUTH-1, CORE-2), **deployment-topology assumptions** (OPS-1), and **defence in depth** (CORE-3 CSP, LOOKUP-2 outbound limits). PREFS-4 is a privacy and UX item; the owner has chosen to keep opt-out analytics.

### 4.4 Data model

The schema is lean and well-indexed for current queries. For the product direction it lacks four things: **status** on books (LIBRARY-3), **tags** (LIBRARY-5), a **wheel/list** container (CORE-10) and **membership** (CORE-11). Sequencing matters. CORE-10 should be designed *before* tags and spin filters are built, so filters attach to wheels rather than having to move later. A short design doc for CORE-10 is the first step of M4/M5.

### 4.5 Testing & quality

Backend: excellent. Real Postgres, 400+ tests, consistent naming, concurrency tests, coverage in CI. Frontend: effectively untested behaviourally (**CORE-5**). i18n completeness isn't CI-enforced (**PREFS-3**). There's no restore test for backups (**OPS-2**).

### 4.6 Phase readiness

| Capability | Phase 1 — Self-hosted | Phase 2 — Public face | Phase 3 — SaaS |
|---|---|---|---|
| Session survives deploys | ❌ CORE-2 | ❌ | ❌ |
| Documented reverse-proxy setup | ⚠️ CORE-15 | ❌ | ❌ |
| Automated backups | ⚠️ documented only (OPS-2) | n/a (managed) | n/a |
| Self-service account (password, email, delete) | ❌ AUTH-3, ACCOUNTS-4 | ❌ + CORE-13 | ❌ |
| Public sign-up with abuse controls | n/a | ❌ CORE-12 | ❌ |
| Discoverable analytics opt-out | ⚠️ PREFS-4 | ⚠️ PREFS-4 (opt-out kept by owner decision) | ⚠️ |
| Landing page | n/a | ❌ #46 | ❌ |
| Horizontal scale (>1 instance) | n/a | ⚠️ CORE-2 | ❌ |
| Plans/entitlements | n/a | n/a | ❌ CORE-14 → #48 |
| Standard observability | ⚠️ | ⚠️ OPS-4 | ❌ |

---

## 5. Relationship to existing issues and documents

| Existing | Relationship |
|---|---|
| #95 Goodreads tie-in | **TRANSFER-4** is the concrete first slice (CSV import) |
| #58 Questions wheel | Unblocked by **CORE-10** (custom-kind wheels) |
| #47 Enhanced self account | Decomposed into **AUTH-3**, **ACCOUNTS-4**, **AUTH-5**, **CORE-13** |
| #46 Application front-end | Needs **CORE-12**, **PREFS-4** and **CORE-15** to be meaningful |
| #45 Affiliate links/ads | **LOOKUP-6** provides the affiliate hook; ads are deferred to CORE-14's ad-free entitlement |
| #48 Payments | **CORE-14** is the prerequisite groundwork |
| `IMPROVEMENT_ROADMAP.md` open items | Search/filter (LIBRARY-1), password change (AUTH-3), OIDC (AUTH-7), weights and skip (WHEEL-3), tags and status (LIBRARY-3/5), offline (PWA-2) are now all tracked as issues |

---

## 6. Owner decisions captured

| Question | Decision |
|---|---|
| Target deployment model | Self-hosted now → public face → SaaS (drives the `phase:*` labels) |
| Issue filing | GitHub issues with area, impact, complexity, phase and tier labels, plus a tracking epic |
| Granularity | One issue per recommendation |
| Scope | Improve existing features **and** propose adjacent ones |
| Multiple wheels (CORE-10, #207) | A book can belong to **any number of wheels** at once (`wheel_items` join table). Removing a book from a wheel is distinct from deleting the book. *(Corrected 2026-10-11.)* |
| Reading status on spin (LIBRARY-3, #186) | A spin **never** changes status automatically; only explicit user action does |
| Cover storage (LIBRARY-6, #223) | Delegated to the auditor: **Postgres** (`book_covers` bytea table), so the `pg_dump` backup (OPS-2) captures covers without a second volume. Covers are ~20–100 KB each, which is negligible at household scale. |
| Analytics provider (PREFS-4, #191) | **Google Analytics for now**, staying **opt-out** (on by default) with a discoverable opt-out and GPC support; a cookieless alternative is out of scope. *(Corrected 2026-10-11.)* |
| Master library (CORE-16, #248) | Shared **edition-only** catalog (one row per ISBN, no work layer) plus a per-user library. Several editions or formats of one book = **one entry, many formats**, **grouped by user confirmation** ("Is this another format of X?") rather than automatic matching. **Deletion is user-scoped only.** Catalog corrections are admin-only; users get personal overrides. |
| Smart wheels (CORE-10, #207) | Rules are **per user** and nothing is global. A book that drops off can be **re-added any time** ("Read again", pin). Re-adding a removed book restores it (#181). |
| "Nook Only" type (LIBRARY-7, #224) | **Keep as-is.** LIBRARY-7 is broadened to **admin-managed book types** (create, rename, icon, reorder, retire), at the owner's request |

## 7. Open questions for the owner

None outstanding. All five questions from the initial audit were answered on 2026-10-11 (see §6).
