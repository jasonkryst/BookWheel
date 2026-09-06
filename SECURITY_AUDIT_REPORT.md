# Security Audit Report - Book Wheel

## Full Security Audit — 2026-09-04: Full-Application-Surface Review

Date: 2026-09-04
Auditor: Claude (Sonnet 5), via Claude Code
Scope: A comprehensive, full-application-surface review rather than an incremental feature-diff audit. Covered: authentication and session management (`AuthController`, `AuthService`), authorization and IDOR risk across all controllers (`UsersController`, `MetricsController`, `MigrationController`, `StatsController`, `BooksController`), SQL-injection surface across the entire `BookWheel/` tree, XSS patterns in `wwwroot/js/app.js`, CSRF posture, live rate-limiting/lockout behavior against a dedicated throwaway account, HTTP security headers on the running instance, third-party script/privacy exposure (Google Analytics default), Docker/deployment hygiene, dependency vulnerability scanning, password-reset token entropy/expiry/single-use enforcement, and the legacy migration utility's exposure.

### Executive Summary

Overall posture: **Low-to-Medium risk** (two new Medium findings; prior Low findings remain open and unchanged)

- Critical: 0
- High: 0
- Medium: 2 (both new)
- Low: 5 (3 new, 2 carried forward unchanged from the 2026-08-19 audit)

The application's core security architecture remains sound: all data access goes through EF Core LINQ with no raw/interpolated SQL anywhere in the tree, every user-data endpoint (`BooksController`, `StatsController`) scopes queries to the authenticated caller's own `UserId` taken from the server-side session rather than any client-supplied identifier (no IDOR found), all admin-only endpoints correctly gate on `IsAdmin`, password hashing uses ASP.NET Core Identity's `PasswordHasher<T>`, and password-reset tokens use a 256-bit CSPRNG value hashed at rest with single-use and 24-hour-expiry enforcement verified live. Two new Medium findings were identified: a Production cookie behavior that can silently break login for a plausible self-hosted deployment shape, and a privacy-relevant default that sends every deployer's end-user traffic to the original developer's Google Analytics property unless explicitly overridden.

### Live Verification Performed Against the Running Instance (`http://localhost:32700`)

- Logged in as `audituser` (admin) and inspected the `Set-Cookie` header on `POST /api/auth/login`.
- Created a dedicated throwaway account (`secaudituser`) via `POST /api/users`, retrieved its setup-link token, and completed setup via `POST /api/auth/password-reset/complete` — used exclusively for the lockout and token-replay tests below. `audituser`, `a11yuser`, and `perfuser` were not touched, and no books were created, modified, or deleted.
- Sent 6 rapid failed login attempts against `secaudituser` to observe both the username-aware lockout and the IP-based rate limiter.
- Replayed the already-consumed password-reset token to confirm single-use enforcement.
- Captured full response headers for `GET /`, `GET /api/auth/status`, and an `OPTIONS` cross-origin preflight against `/api/books`.
- Ran `dotnet list ... --vulnerable --include-transitive` for both `BookWheel.csproj` and `BookWheel.Tests.csproj`, and `scripts/check-vulnerable-packages.sh` against both.
- Started a full `dotnet test BookWheel.slnx` run; it did not complete within the session and was terminated (see "Fresh Scan Results" and "Audit Limitations" below).

No containers were stopped, restarted, or rebuilt; `audituser`, `a11yuser`, and `perfuser` were left untouched.

### Findings (Ordered by Severity)

#### 1) Medium (NEW) - Production `Secure` cookie flag is forced `true` unconditionally, independent of whether the actual request was HTTPS

Evidence:

- `AuthController.cs`, `SignInAsync` (lines 238-257):

  ```csharp
  var secureCookie = !environment.IsDevelopment() && !environment.IsEnvironment("Testing")
      ? true
      : Request.IsHttps;
  ```

- Live verification against the running Production-mode instance confirmed this exactly:

  ```
  POST /api/auth/login (over plain HTTP, http://localhost:32700)
  Set-Cookie: BookWheel.Auth=...; expires=...; path=/; secure; samesite=strict; httponly
  ```

  The `secure` attribute is present even though the request itself was plain HTTP.

Risk:

- Browsers only accept a `Secure`-flagged cookie set over a plain-HTTP response when the origin is on a short list of exemptions defined by the Secure Contexts spec — chiefly `localhost` and loopback addresses, which browsers treat as "potentially trustworthy" regardless of scheme. This is why login still works when the app is reached at `http://localhost:32700`.
- That exemption does **not** extend to a LAN IP address (e.g. `http://192.168.1.50:32700`), a real hostname, or any other non-localhost origin reached over plain HTTP — all plausible for a self-hosted personal app deployed via the bundled `docker-compose.yml`, which exposes port 32700 directly with no reverse-proxy TLS termination (`README.md`'s Docker instructions do not mandate one).
- In that deployment shape, the browser will accept the `200 OK` login response but silently discard the `Secure` cookie, since it was delivered over an insecure channel. The user appears to log in successfully, but no session cookie persists — the next request is unauthenticated, with no error message pointing at the cause. This is a confusing, hard-to-diagnose failure mode for exactly the deployment pattern (home-network self-hosting, no TLS) this project's README and Docker packaging otherwise anticipate.
- This is distinct from the existing "PostgreSQL connection string does not pin SSL/TLS mode" Low finding (2026-08-19), which concerns database transport encryption, not the browser-facing auth cookie.

Recommendations:

1. Prefer basing the `Secure` flag on the actual request scheme (`Request.IsHttps`) rather than unconditionally forcing `true` in Production, so a plain-HTTP deployment gets a cookie that at least persists (even though `SameSite=Strict` cookies should still be delivered over TLS for confidentiality — see below) — or:
2. Keep forcing `Secure=true` in Production, but make the requirement explicit and loud: document in `README.md` that Production deployments **must** sit behind TLS termination (reverse proxy or direct HTTPS binding), and add a startup-time warning (logged, and ideally surfaced in the UI) when the app is running in Production without HTTPS configured/detected, so operators see an actionable signal instead of a silent login failure.
3. Regardless of which option is chosen, consider detecting the specific "login succeeded but no session cookie came back on the next request" pattern and logging it distinctly, since it is otherwise invisible to both the operator and the end user.

#### 2) Medium (NEW) - Google Analytics is enabled by default with a real, hardcoded property ID, sending every deployer's user telemetry to the original developer

Evidence:

- `BookWheel/appsettings.json`:

  ```json
  "Analytics": {
    "GoogleAnalyticsId": "G-JQXW826H1F"
  }
  ```

- `Program.cs` reads this value unconditionally (`builder.Configuration["Analytics:GoogleAnalyticsId"]`) and substitutes it into every served `index.html` response (`WriteConfiguredIndexAsync`, `html.Replace("__GOOGLE_ANALYTICS_ID__", googleAnalyticsId, ...)`) with no feature flag or empty-string guard.
- `wwwroot/index.html` (lines 419-427) unconditionally loads `https://www.googletagmanager.com/gtag/js?id=__GOOGLE_ANALYTICS_ID__` and calls `gtag('config', ...)` on every page load, **including the pre-login screen** — before any authentication, consent, or opt-out is possible.

Risk:

- BookWheel is an open-source, self-hostable application. Any operator who deploys it without explicitly overriding `Analytics:GoogleAnalyticsId` (via `appsettings.Production.json`, an environment variable, or similar) will unknowingly route their own end-users' page-view telemetry — IP-derived geolocation, browser/device fingerprint captured by Google's `gtag.js` — to the *original developer's* Google Analytics property (`G-JQXW826H1F`), not their own.
- This happens on every page load, including the unauthenticated login screen, with no cookie-consent banner, no privacy-policy link, and no in-app way to opt out.
- Beyond the direct privacy exposure to the original developer, this is an "insecure/non-private by default" packaging choice: a real, working tracking ID should not ship as the default in a template configuration file intended to be forked and redeployed by third parties.

Recommendations:

1. Ship an empty default (`"GoogleAnalyticsId": ""`) and treat analytics as strictly opt-in; when empty, skip emitting the `gtag.js` script tag entirely (rather than requesting `gtag/js?id=` with an empty id) so no third-party network call is made by default.
2. If analytics remains desired for the maintainer's own deployment, keep the real ID out of the committed `appsettings.json` and set it via environment/secret configuration on the maintainer's own instance only.
3. Document the setting prominently in `README.md`'s configuration section, including what data is sent and to whom.
4. If analytics ships enabled by default in any form going forward, add a visible privacy notice/consent mechanism, and ensure it does not fire on the pre-login screen without consent.

**Status (2026-09-06):** Addressed via an opt-out mechanism rather than removal — see `README.md`'s new "Analytics" section. The default ID is retained (tracking remains on by default for the maintainer's own deployment), but end users can now disable it via a Settings → Preferences checkbox, which sets Google's documented `ga-disable-<id>` flag immediately. Downstream forkers are now explicitly told in the README to override or blank the ID for their own deployment.

#### 3) Low (NEW) - No baseline HTTP security headers on any response

Evidence — live `curl -i` against the running instance:

```
GET / HTTP/1.1                      GET /api/auth/status HTTP/1.1
200 OK                               200 OK
Cache-Control, Pragma, Expires       (no security headers)
X-Correlation-ID
(no X-Content-Type-Options)
(no X-Frame-Options / frame-ancestors)
(no Referrer-Policy)
(no Content-Security-Policy)
```

`HSTS` (`app.UseHsts()`) is present and correctly gated to non-Development environments, so that header is sent — but only once a client has already connected over HTTPS at least once, and only in deployments that actually terminate TLS (see Finding #1).

Risk:

- `X-Content-Type-Options: nosniff` absence allows MIME-sniffing in older/non-compliant clients.
- `index.html` serves an interactive login form and an authenticated management UI; without `X-Frame-Options` or a `frame-ancestors` CSP directive, the page can be embedded in a hostile iframe for clickjacking (tricking a logged-in user into clicking disguised UI elements). `SameSite=Strict` on the auth cookie limits (but does not eliminate) the practical impact, since a framed page still runs with the victim's real session if they are already logged in on that origin.
- No `Content-Security-Policy` is set. Given the app is a hand-written vanilla-JS SPA with a narrow, known script surface (`js/app.js`, `js/i18n.js`, and the Google Analytics tag from Finding #2), a CSP would meaningfully reduce blast radius if a script-injection bug were ever introduced.
- Absence of `Referrer-Policy` means the default browser behavior applies (`strict-origin-when-cross-origin` in modern browsers), which is a reasonable default and lower-priority than the other two headers.

This is an acceptable-but-improvable posture for an API+static-file host (there is no server-rendered content reflecting untrusted input), but the app does serve a full interactive UI, not just JSON, so these headers are cheap wins.

Recommendations:

1. Add `X-Content-Type-Options: nosniff` and `X-Frame-Options: DENY` (or `Content-Security-Policy: frame-ancestors 'none'`) globally via middleware.
2. Add a baseline `Content-Security-Policy` scoped to the app's actual script/style/connect sources (self, plus `googletagmanager.com`/`google-analytics.com` only if Finding #2's analytics remain enabled).
3. Add `Referrer-Policy: strict-origin-when-cross-origin` explicitly rather than relying on browser defaults.

#### 4) Low (NEW) - Session tokens are held only in an in-process, in-memory dictionary

Evidence:

- `AuthService.cs`: `private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();` — sessions and the username-aware failed-login/lockout counters (`_failedLogins`) live entirely in process memory with no persistence layer.

Risk:

- Every application restart (deploy, crash, container recreation) silently invalidates all active sessions and resets in-progress lockout counters — not a vulnerability by itself, but a resilience/UX gap worth documenting, since a mid-lockout restart lets a locked-out attacker resume immediately.
- The design does not support horizontal scaling (multiple app instances/replicas behind a load balancer) without sticky sessions, since a session created on one instance is invisible to another. This is consistent with BookWheel's current single-instance deployment model, but is worth calling out explicitly as an architectural constraint rather than an oversight, especially if the roadmap's OIDC/Identity item (`IMPROVEMENT_ROADMAP.md`, Priority 4) is pursued.

Recommendations:

1. If multi-instance deployment is ever anticipated, move session state to PostgreSQL (already used for credentials/tokens) or a shared cache.
2. Otherwise, document the single-instance assumption explicitly so operators don't unknowingly deploy multiple replicas.

#### 5) Low (NEW) - Disabled/locked/force-reset account status is only revealed after the correct password is supplied, acting as a password-correctness oracle for inactive accounts

Evidence:

- `PostgresCredentialRepository.ValidateCredentialsAsync` (lines 54-68) looks up the user by username, then calls `PasswordHasher.VerifyHashedPassword(...)`, and returns a non-null `CredentialRecord` **only if the password is correct** — regardless of whether the account is disabled, locked, or flagged for forced reset:

  ```csharp
  var result = PasswordHasher.VerifyHashedPassword(entity.Username, entity.PasswordHash, password);
  return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
      ? ToRecord(entity)
      : null;
  ```

- `AuthService.ValidateCredentialsAsync` then branches on `user.IsDisabled` / `user.IsLocked` / `user.ForcePasswordReset` only in the non-null (i.e., password-correct) path; a wrong password for the same account always falls into the generic `IsInvalidCredentials` branch instead.
- `AuthController.Login` surfaces this directly to the caller: a wrong password returns `401` with the generic "Invalid username or password" message, while a **correct** password against a disabled/locked/reset-required account returns `423 Locked` with an account-status-specific message ("This account is disabled...", "Password reset is required...").

Risk:

- An attacker who has (or is testing) a candidate username/password pair — for example, from a credential-stuffing list following an unrelated breach — can distinguish "wrong password" from "right password, but the account happens to be disabled/locked/reset-pending" purely from the HTTP status code and message, without any other side channel. Reaching a `423` response is a definitive confirmation that the submitted credential pair is valid for this application, even though the account itself cannot currently be used to log in.
- This is a narrower, lower-impact variant of a classic user-enumeration oracle: it doesn't reveal whether a username exists (that part is handled correctly — a wrong password against a nonexistent user and a wrong password against a disabled user both return the same generic `401`), but it does leak password correctness for accounts that are not currently active.
- Practical exploitability is bounded by the same username-lockout (5 attempts / 3 minutes) and IP-based rate limiter (5/minute) verified working in this audit, so this is not an unlimited oracle — but it is a real, avoidable information leak for admins who proactively disable/lock a compromised account, since doing so does not stop an attacker from confirming their stolen credentials still work.

Recommendations:

1. Return the same generic `401`/"Invalid username or password" response for a correct-password-but-inactive-account case as for a wrong password, and instead surface the disabled/locked/reset-required detail only through an authenticated channel (e.g., an admin-visible account status in `UsersController`, which already exists) rather than to the unauthenticated login caller.
2. If the current behavior (informing the end user *why* they can't log in) is intentional for UX reasons, document that tradeoff explicitly so it's a deliberate decision rather than an overlooked side effect.

### Previously Reported Findings — Status

#### Automatic EF Core migrations at startup require DDL privileges (Low, reported 2026-08-19) — **Open**

No change. Still tracked in `IMPROVEMENT_ROADMAP.md` Priority 1, item 10.

#### PostgreSQL connection string does not pin an SSL/TLS mode (Low, reported 2026-08-19) — **Open**

No change. Still tracked in `IMPROVEMENT_ROADMAP.md` Priority 1, item 9.

### Methodology and Areas Reviewed With No New Findings

- **SQL injection**: Grepped the entire `BookWheel/` tree for `FromSqlRaw`, `ExecuteSqlRaw`, `ExecuteSqlInterpolated`, and `FromSqlInterpolated` — zero matches. All storage access (`Storage/Postgres/*.cs`) is EF Core LINQ.
- **IDOR / authorization**: `BooksController`, `StatsController`, and the spin-history/export endpoints derive the owning `UserId` exclusively from `_authService.GetAuthenticatedUser(HttpContext)` (the server-side session), never from a client-supplied route/query/body parameter — a user cannot address another user's books, spins, or stats by guessing an id. `UsersController`, `MetricsController`, and `StatsController.GetAggregate()` all correctly gate on `IsAdmin` and return 403 for non-admins, 401 for unauthenticated callers.
- **Migration utility (`MigrationController`)**: Confirmed admin-gated once an account exists (`AuthorizeMigrationRequestAsync` returns 401/403 appropriately). It is reachable without authentication only in the narrow bootstrap window before any account has been created — the same documented pattern as `POST /api/auth/setup`, and matches `README.md`'s "Legacy Data Migration Utility" section ("If an account exists, these endpoints require an authenticated administrator"). No new risk beyond what is already documented.
- **CSRF**: All state-changing endpoints use `POST`/`PUT`/`DELETE`; every `[HttpGet]` across all controllers (`GetUsers`, `GetStats`, `GetAggregate`, `GetAll`, `Export`, `Lookup`, `GetSpinHistory`, `Me`, `Status`, migration `Status`) is read-only — no state-changing GETs were found. Combined with `HttpOnly` + `SameSite=Strict` on the auth cookie (which prevents the cookie from being attached to any cross-site navigation or request, including top-level form-post CSRF), the app has no separate CSRF token and doesn't currently need one for its cookie-based model.
- **CORS**: No `AddCors`/CORS middleware is configured anywhere in `Program.cs`; a live cross-origin `OPTIONS` preflight against `/api/books` returned no `Access-Control-Allow-Origin` header, confirming the default same-origin browser policy is in effect with no explicit relaxation.
- **XSS**: Reviewed all `innerHTML`/`outerHTML`/`insertAdjacentHTML` usages in `wwwroot/js/app.js`. Every occurrence either clears content (`el.innerHTML = ''`) or injects a static, hardcoded template built from `t('...')` i18n keys (never raw user data). User-controlled values — book titles, authors, usernames, ISBN-lookup results — are consistently assigned via `textContent` or DOM property assignment (e.g., `titleButton.textContent = book.title`), not HTML interpolation. No injectable pattern was found.
- **Password reset tokens** (`PostgresPasswordResetTokenRepository.cs`): Raw tokens are generated via `RandomNumberGenerator.GetBytes(32)` (256-bit CSPRNG), stored only as a SHA-256 hash, expire after 24 hours, and are deleted on successful use. Live-tested: after `POST /api/auth/password-reset/complete` succeeded for the throwaway account, replaying the same token returned `400 Bad Request` ("invalid or has expired") — single-use is correctly enforced, not just documented.
- **Rate limiting / lockout** (live-tested against `secaudituser` only): `SecurityOptions.UsernameLockoutThreshold=5` / `UsernameLockoutMinutes=3` behaved exactly as configured — the 5th consecutive failed login returned `423 Locked`. A second, independent IP-based fixed-window rate limiter (`PermitLimit=5` per minute on `/api/auth/login`, configured in `Program.cs`) also engaged, returning `429 Too Many Requests` on the 6th rapid attempt — two independent layers of brute-force defense, both functioning correctly.
- **Password hashing**: `PostgresCredentialRepository` uses ASP.NET Core Identity's `PasswordHasher<T>` (PBKDF2-based) for all password storage, resets, and verification — an appropriate, well-vetted default.
- **Docker/deployment** (`Dockerfile`): Multi-stage build (SDK image for build, smaller ASP.NET runtime image for final stage). Runs as the non-root `$APP_UID` user, with `chown -R app:app` applied to `/app` and the Data Protection key directory before dropping privileges. No secrets are baked into the image; the PostgreSQL connection string is injected via environment variable at runtime, and `docker-compose.yml`'s local-dev-only default database password is clearly commented as such with override guidance. Base images (`sdk:8.0`, `aspnet:8.0`) are pinned to a major.minor tag but not a digest — standard practice for this project's release cadence and not flagged as a new finding.
- **Dependency vulnerabilities**: `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for either `BookWheel.csproj` or `BookWheel.Tests.csproj`. `scripts/check-vulnerable-packages.sh` (the script that actually gates CI) passed clean for both projects.

### Fresh Scan Results (run for this audit)

```
dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive
  -> The given project `BookWheel` has no vulnerable packages given the current sources.

dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive
  -> The given project `BookWheel.Tests` has no vulnerable packages given the current sources.

scripts/check-vulnerable-packages.sh BookWheel/BookWheel.csproj BookWheel.Tests/BookWheel.Tests.csproj
  -> clean for both projects
```

Full test suite: a `dotnet test BookWheel.slnx` run was started for this audit but did not complete within the audit session (still running after 30+ minutes, well beyond the prior audit's full-suite runtime, likely due to Testcontainers resource contention with the already-running `bookwheel`/`bookwheel-postgres` containers on the same Docker host) and was terminated rather than left running unattended or reported with fabricated numbers. This audit's live rate-limiting, lockout, token-replay, and header checks were performed directly against the running instance instead (see "Live Verification Performed" above), and the previous audit's full-suite pass (281/281) is the most recent confirmed result — see Audit Limitations.

## Positive Observations

- All data access across the entire codebase (not just previously-audited controllers) goes through EF Core LINQ; no `FromSqlRaw`, `ExecuteSqlRaw`, or string-concatenated SQL exists anywhere in `BookWheel/`.
- No IDOR was found: every book, spin-history, spin-stats, and export endpoint derives the owning user exclusively from the server-side session, never from a client-supplied identifier.
- All admin-only endpoints (`UsersController`, `MetricsController`, `StatsController.GetAggregate()`, and `MigrationController` once an account exists) correctly enforce `IsAdmin` and return 403/401 as appropriate.
- Password-reset tokens use a 256-bit CSPRNG (`RandomNumberGenerator.GetBytes(32)`), are hashed with SHA-256 at rest, expire after 24 hours, and are enforced single-use — verified live by successfully replaying a consumed token and receiving `400 Bad Request`.
- Password hashing uses ASP.NET Core Identity's `PasswordHasher<T>` (PBKDF2), an appropriate, well-vetted default.
- Username-aware login lockout and IP-based rate limiting both function correctly and independently — verified live end-to-end against a dedicated throwaway account (`5th` failed attempt → `423 Locked`; `6th` rapid attempt also tripped the separate `429 Too Many Requests` IP limiter).
- No `innerHTML`/`outerHTML`/`insertAdjacentHTML` call in `wwwroot/js/app.js` interpolates user-controlled data; all user-controlled values (titles, authors, usernames, ISBN-lookup results) are rendered via `textContent` or safe DOM property assignment.
- `HttpOnly` + `SameSite=Strict` on the session cookie, combined with the absence of any state-changing `GET` endpoint, means the app has no meaningful CSRF exposure despite carrying no separate CSRF token.
- No CORS relaxation is configured anywhere; a live cross-origin preflight confirmed no `Access-Control-Allow-Origin` header is ever returned.
- The Docker image runs as a non-root user with correctly-scoped ownership of writable paths, uses a proper multi-stage build, and bakes in no secrets — the PostgreSQL connection string and Data Protection key path are both supplied at runtime via environment/volume configuration.
- Dependency scanning (`dotnet list --vulnerable` and the CI-gating `check-vulnerable-packages.sh`) remains clean for both `BookWheel.csproj` and `BookWheel.Tests.csproj`.

## Prioritized Remediation Plan

### Short Term (1-2 weeks)

1. Decide the approach for Finding #1 (Production `Secure` cookie forced regardless of request scheme) — either base it on `Request.IsHttps` and accept the residual confidentiality tradeoff, or keep it forced and add explicit, loud documentation plus a startup-time warning when Production is running without HTTPS. Either way, stop leaving operators with a silent, undiagnosable login failure on non-localhost plain-HTTP deployments.
2. Change the default `Analytics:GoogleAnalyticsId` to an empty string and skip emitting the `gtag.js` tag entirely when unset (Finding #2). This is a one-line config change with an immediate privacy benefit for every downstream deployer who hasn't touched the setting.
3. Add `X-Content-Type-Options: nosniff` and either `X-Frame-Options: DENY` or a `frame-ancestors` CSP directive globally (Finding #3) — low effort, meaningful clickjacking/MIME-sniffing reduction.

### Mid Term (2-6 weeks)

1. Add a baseline `Content-Security-Policy` and `Referrer-Policy` scoped to the app's actual script/style/connect sources (Finding #3).
2. Pin an explicit `SSL Mode` for production PostgreSQL connection strings (carried-forward Low finding, 2026-08-19).
3. Decide on and document a DDL-privilege strategy for schema migrations vs. runtime DML access (carried-forward Low finding, 2026-08-19).
4. If a privacy notice or consent mechanism for analytics is desired going forward, design and add it now rather than after re-enabling telemetry by default (Finding #2).
5. If horizontal scaling is ever anticipated, move session state out of the in-process dictionary (Finding #4) into PostgreSQL or a shared cache; otherwise, document the single-instance assumption explicitly.
6. Collapse the disabled/locked/reset-required login responses into the same generic invalid-credentials response, or explicitly document why revealing account status to an unauthenticated caller with a correct password is an accepted UX tradeoff (Finding #5).

## Audit Limitations

- No dynamic penetration testing beyond the specific live checks listed above (login-cookie inspection, lockout/rate-limit behavior, token replay, HTTP header capture, CORS preflight) was performed.
- No infrastructure, reverse proxy, firewall, or environment hardening review was performed; Finding #1's risk analysis assumes a plausible but unverified deployment shape (direct port exposure with no TLS termination) rather than observing an actual non-localhost deployment.
- No external SAST/DAST tool results were included beyond NuGet vulnerability scanning and integration tests (CodeQL and Trivy results are tracked separately in the addendum below and were not re-run as part of this pass).
- Browser-side confirmation of the Secure-cookie-drop behavior (Finding #1) over a genuinely non-localhost plain-HTTP origin was not performed in this session; the analysis relies on documented Secure Contexts spec behavior and the live-captured `Set-Cookie` header rather than an observed failed login against a LAN IP.
- Client-side JavaScript review covered both `wwwroot/js/app.js` and `wwwroot/js/i18n.js` for unsafe HTML-injection patterns (`innerHTML`, `outerHTML`, `insertAdjacentHTML`); `i18n.js` contains none — it is a static translation-string lookup table with no DOM-injection surface.
- The full `dotnet test BookWheel.slnx` regression suite was not re-run to completion for this audit (see "Fresh Scan Results" above) — it was started but did not finish within the session and was terminated rather than reported with an unverified number. This audit instead relied on direct live testing against the running instance for the specific security-relevant behaviors in scope (lockout, rate limiting, token single-use, cookie flags, headers, CORS). The most recent confirmed full-suite result remains the 2026-08-31 audit's 281/281 pass.

## Conclusion

The application's core security architecture remains strong: consistent EF Core-only data access, session-scoped authorization with no IDOR across any endpoint, correctly-enforced admin gating, safe DOM-manipulation patterns in the frontend, and properly-implemented password/token cryptography, all verified either by code review or live testing against the running instance. This audit's two new Medium findings are both about defaults and deployment posture rather than exploitable application-layer vulnerabilities: a Production cookie setting that can silently break sessions for a plausible non-TLS self-hosted deployment, and a real analytics ID shipped as the default for an open-source project, sending downstream deployers' user telemetry to the original developer by default. The three new Low findings (missing baseline HTTP security headers, in-memory-only session storage, and a password-correctness oracle for inactive accounts) are all inexpensive to address. The two Low findings carried forward from the 2026-08-19 audit remain open and unchanged.

---

## Addendum — 2026-08-31: CI Security Scanning (Trivy + CodeQL)

Date: 2026-08-31
Auditor: Claude (Sonnet 4.6), via Claude Code
Scope: CI workflow additions — Trivy container scanning and CodeQL source-code SAST — introduced in version 2.11.0

### Summary

Two automated security scanning layers are now active in CI:

**Trivy container scanning** (`.github/workflows/ci.yml`, `trivy` job):
- Builds the Docker image on every CI run and scans it with [Trivy](https://github.com/aquasecurity/trivy).
- Hard failure gate: exits non-zero if any fixable CRITICAL or HIGH severity findings are present. CI does not pass if this gate fails.
- Full SARIF report (all severities, including unfixed/informational) is generated and uploaded to the GitHub Security → Code Scanning tab on every push to `main` and on PRs from the same repository.
- Pinned to `aquasecurity/trivy-action@ed142fd0673e97e23eac54620cfb913e5ce36c25` (v0.36.0).

**CodeQL source-code analysis** (`.github/workflows/codeql.yml`):
- Runs GitHub's native [CodeQL](https://codeql.github.com/) on the .NET/C# source code.
- Triggers: every push to `main`, every PR targeting `main`, and on a weekly Monday 08:00 UTC schedule for catching newly published CVEs against unchanged code.
- Query suite: `security-extended` (the default `security-and-quality` set plus extended security queries — broader coverage than the default).
- Results feed into the same GitHub Security → Code Scanning tab as Trivy, giving a unified view of container-layer and source-code findings.
- Pinned to `github/codeql-action@d1ba80a13dd99fba24a470575428917156a28b43` (v4).

No new application-code findings. Both tools add detection coverage without modifying runtime behavior.

---

## Prior Audit — 2026-08-19
Auditor: Claude (Sonnet 5), via Claude Code
Scope: Application project in BookWheel and solution-level dependency review, focused on the PostgreSQL storage-layer migration (#55) shipped since the prior audit (2026-06-01) and a full re-verification of previously reported findings
Date: 2026-08-31
Auditor: Claude (Sonnet 4.6), via Claude Code
Scope: Application project in BookWheel and solution-level dependency review, focused on the Spin Wheel Stats feature (GH #75) — including new stat endpoints, book audit fields, and two new EF Core migrations — shipped since the prior audit (2026-08-19) and a full re-verification of previously reported findings

## Executive Summary

This audit refreshes the 2026-08-19 report against the current codebase, which has since added a Spin Wheel Stats feature (`GET /api/stats`, `GET /api/stats/aggregate`), two new EF Core migrations adding `CreatedAtUtc` and audit columns (`CreatedByUserId`, `UpdatedAtUtc`, `LastUpdatedByUserId`) to the `books` table, and a new `PostgresSpinStatsRepository`. All scans and tests below were re-run fresh for this audit rather than reused from prior results.

Overall posture: Low risk

- Critical findings: 0
- High findings: 0
- Medium findings: 0
- Low findings: 2 (both carried forward from prior audit — no new findings)

Security-relevant changes verified in this revision:

- `GET /api/stats` requires authentication (returns 401 for unauthenticated callers — verified by regression test `Stats_Unauthenticated_Returns_Unauthorized`)
- `GET /api/stats/aggregate` requires both authentication and the `isAdmin` flag; non-admin callers receive 403 — verified by regression tests `Non_Admin_Cannot_Access_Aggregate_Stats` and `Aggregate_Stats_Unauthenticated_Returns_Unauthorized`
- All stats data access goes through EF Core LINQ — no raw/interpolated SQL (`FromSqlRaw`, `ExecuteSqlRaw`, string-built queries) found in `PostgresSpinStatsRepository.cs`; the migration does not widen the SQL-injection surface
- `PostgresSpinStatsRepository.GetAggregateAsync()` cross-joins `SpinSelections` with `Users` to derive top-user spin counts but does not expose any credential fields (password hash, reset tokens) — only `UserId` and `Username` appear in the aggregate response
- `Username` is a non-sensitive identifier already visible throughout user-management responses; no new credential-class data is introduced in the stats payload
- The two new migrations (`AddBookCreatedAt`, `AddBookAuditFields`) add nullable/defaulted columns to `books` — no schema change to credential or token tables; EF Core snapshot updated accordingly
- `CreatedByUserId`, `UpdatedAtUtc`, and `LastUpdatedByUserId` audit columns are stored server-side via the repository layer; they are not accepted from request bodies and cannot be spoofed by a caller

## Fresh Scan Results (run for this audit)

Dependency vulnerability scan:

```
dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive
  -> The given project `BookWheel` has no vulnerable packages given the current sources.

dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive
  -> The given project `BookWheel.Tests` has no vulnerable packages given the current sources.

scripts/check-vulnerable-packages.sh BookWheel/BookWheel.csproj BookWheel.Tests/BookWheel.Tests.csproj
  -> clean for both projects (this is the script that actually fails CI; `dotnet list --vulnerable`
     alone always exits 0)
```

Full test suite:

```
dotnet test BookWheel.slnx --verbosity normal
  -> Total tests: 281, Passed: 281
```

Targeted security regression tests (same filter as prior audit, all pass):

```
dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter \
  "FullyQualifiedName~Failed_Login_Is_Recorded_As_Structured_Warning_Log|\
   FullyQualifiedName~Login_Is_Rate_Limited_After_Repeated_Failed_Attempts|\
   FullyQualifiedName~Login_Rate_Limiter_Uses_Forwarded_Client_Ip_When_Present|\
   FullyQualifiedName~Non_Admin_User_Cannot_Access_User_Management_Endpoints|\
   FullyQualifiedName~Non_Admin_User_Cannot_Access_Metrics_Endpoint|\
   FullyQualifiedName~Password_Reset_Link_Can_Be_Generated_And_Used_Once|\
   FullyQualifiedName~Disabled_User_Cannot_Log_In|\
   FullyQualifiedName~Request_Correlation_Header_Is_Propagated"
  -> Total tests: 8, Passed: 8
```

New stats-specific security regression tests (all pass):

```
dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~Stats"
  -> Total tests: 20, Passed: 20

Includes:
  Stats_Unauthenticated_Returns_Unauthorized
  Non_Admin_Cannot_Access_Aggregate_Stats
  Aggregate_Stats_Unauthenticated_Returns_Unauthorized
  Admin_Can_Access_Aggregate_Stats
  Aggregate_Stats_Reflect_Multi_User_Totals
  Stats_For_User_With_No_History_Returns_Zero_Totals
  Stats_After_Spins_Returns_Correct_Counts
  Stats_NeverSpunCount_Reflects_Unselected_Books
  Stats_After_Book_Deleted_Preserves_Spin_Count
  Stats_TopBooks_Percentage_Sums_To_One_Hundred
  (+ 10 additional data-correctness cases)
```

## Methodology

- Manual review of the new stats endpoints (`StatsController`), `PostgresSpinStatsRepository`, new EF Core migrations, and `Program.cs` wiring
- Grep-based static checks for raw/interpolated SQL and credential-field exposure through the new stats API response DTOs
- Review of authorization gates on `GET /api/stats` (auth required) and `GET /api/stats/aggregate` (auth + admin required)
- Fresh NuGet vulnerability scan for direct and transitive packages (not reused from any prior run)
- Fresh full-solution test run plus targeted security regression tests, all re-executed for this audit

## Findings (Ordered by Severity)

### 1) Low - Automatic EF Core migrations at startup require the runtime database role to hold DDL privileges

Evidence:

- `Program.cs` calls `await startupDbContext.Database.MigrateAsync()` unconditionally at application startup, using the same connection/credential the app uses for normal request handling.

Risk:

- The database role configured in `ConnectionStrings:BookWheel` must be able to create/alter tables and indexes, not just read/write rows. If the application is compromised, the attacker inherits DDL privileges in addition to data access, which is broader than a least-privilege DML-only role would allow.

Recommendations:

1. For production deployments, consider running `dotnet ef database update` (or equivalent) as a separate deploy step with an elevated, migration-only credential, then run the application itself with a least-privilege role restricted to DML on the `bookwheel` schema.
2. If automatic startup migration is kept for operational simplicity, document the DDL-privilege requirement explicitly so operators don't under-provision — or over-provision — the runtime role.

### 2) Low - PostgreSQL connection string does not pin an SSL/TLS mode

Evidence:

- `docker-compose.yml`'s `ConnectionStrings__BookWheel` and `BookWheel/appsettings.json`'s connection string template do not set `SSL Mode`. Npgsql's default (`Prefer`) attempts encryption opportunistically but does not fail the connection if the server doesn't offer TLS, and does not validate the server certificate.

Risk:

- In a deployment where the app and PostgreSQL are not on the same trusted network segment, credentials and query data (including plaintext-visible usernames) could traverse the network unencrypted without an operator noticing, since the connection would still succeed.

Recommendations:

1. Set `SSL Mode=Require` (or `VerifyFull` with a configured trusted CA) explicitly in production connection strings.
2. Document this alongside the existing "TLS-in-transit to Postgres" guidance in the README, since that guidance currently describes an expectation rather than an enforced configuration.

## Previously Reported Findings — Status

### Automatic EF Core migrations at startup require DDL privileges (Low, reported 2026-08-19) — **Open**

No change. Still tracked in `IMPROVEMENT_ROADMAP.md` Priority 1 item 8.

### PostgreSQL connection string does not pin an SSL/TLS mode (Low, reported 2026-08-19) — **Open**

No change. Still tracked in `IMPROVEMENT_ROADMAP.md` Priority 1 item 7.

### Data Protection key storage not explicitly configured (Low, reported 2026-06-01) — **Closed**

Previously closed in the 2026-08-19 audit. No regression introduced by GH #75.

## Positive Observations

- The stats feature follows the same auth-gate pattern as existing secured endpoints: unauthenticated → 401, non-admin on admin route → 403, verified by dedicated regression tests.
- The admin aggregate endpoint exposes only `username` and spin counts — no password hashes, reset tokens, or session material reach the response DTO.
- All data access in `PostgresSpinStatsRepository` is EF Core LINQ, consistent with the rest of the storage layer; no new SQL-injection surface was introduced.
- The two new migrations are additive-only: nullable and defaulted columns on `books`, with no changes to user or token tables.
- Audit columns (`CreatedByUserId`, `UpdatedAtUtc`, `LastUpdatedByUserId`) are populated server-side only and cannot be influenced by request bodies.
- All 281 tests pass, including 20 new stats-specific tests and 8 targeted security regression tests.
- Dependency scan remains clean for both `BookWheel.csproj` and `BookWheel.Tests.csproj`.

## Prioritized Remediation Plan

### Short Term (1-2 weeks)

1. Pin an explicit `SSL Mode` for production PostgreSQL connection strings (Finding #2).
2. Decide on and document a DDL-privilege strategy for schema migrations vs. runtime DML access (Finding #1).

### Mid Term (2-6 weeks)

1. Evaluate ASP.NET Core Identity or an external OIDC provider now that the data layer is production-grade (already tracked in `IMPROVEMENT_ROADMAP.md`).
2. Revisit whether `App_Data/books.json` and `App_Data/user.cred` — left on disk as a historical backup by the migration tool — should have a documented retention/deletion policy.

## Audit Limitations

- No dynamic penetration testing was performed.
- No infrastructure, reverse proxy, firewall, or environment hardening review was performed.
- No external SAST/DAST tool results were included beyond NuGet vulnerability scanning and integration tests.
- TLS/SSL behavior (Finding #2) was assessed by reading configuration and Npgsql's documented default behavior, not by capturing live network traffic.

## Conclusion

The Spin Wheel Stats feature (GH #75) was implemented without introducing any new security findings. The two remaining Low-severity items are unchanged from the prior audit and relate to production deployment configuration rather than the application code itself. The overall posture remains Low risk.
