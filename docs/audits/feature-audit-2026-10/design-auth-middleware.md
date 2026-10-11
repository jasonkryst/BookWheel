# Design: ASP.NET Core authentication/authorization (CORE-1, #183)

**Status:** Proposed (owner chose full ASP.NET Core cookie authentication, 2026-10-11; CORE-2 kept separate) · **Issue:** CORE-1 #183 · **Builds on:** AUTH-1 (#251, merged) · **Related:** CORE-2 #184 (persistent sessions, separate), CORE-7 #200 (retire MigrationController)

## Problem

Every controller action starts with a hand-copied preamble:

```csharp
var user = _authService.GetAuthenticatedUser(HttpContext);
if (user is null) return Unauthorized();
// admin endpoints then add:
if (!user.IsAdmin) return StatusCode(StatusCodes.Status403Forbidden);
```

~22 copies across 7 controllers. One omission is an auth bypass; `[Authorize]`/OpenAPI tooling can't see any of it; and per-request concerns (role/account-state checks) can't be centralized.

## Decision

Adopt **framework cookie authentication** (`AddAuthentication().AddCookie`), replacing the hand-rolled cookie + in-memory session lookup. Authorization moves to a **fallback policy** + `[Authorize]`/`[AllowAnonymous]`.

Because a framework auth cookie is **self-contained** (claims encrypted in the cookie via Data Protection, no server-side session), the AUTH-1 revocation guarantees are preserved with a **security stamp** validated on every request — the canonical ASP.NET Core Identity pattern.

## Endpoint authorization map

| Area | Endpoints | Rule |
|---|---|---|
| AuthController | `status`, `setup`, `login`, `logout`, `password-reset/{request,complete,validate}`, `forgot-username` | `[AllowAnonymous]` |
| AuthController | `me` | authenticated (fallback) |
| BooksController | all | authenticated |
| PreferencesController | `Get`, `Update` | authenticated |
| StatsController | `GET` | authenticated |
| StatsController | `aggregate` | `[Authorize(Policy="Admin")]` |
| MetricsController | `GET` | `[Authorize(Policy="Admin")]` |
| UsersController | all 5 | `[Authorize(Policy="Admin")]` |
| MigrationController | `status`, `run` | `[AllowAnonymous]` + keep its bespoke "anonymous only before first account, else admin" check (CORE-7 removes this controller) |

## Security stamp (preserves AUTH-1 under self-contained cookies)

1. Add `SecurityStamp` (text, a `Guid.NewGuid().ToString("N")`) to `users` (migration; backfill existing rows with generated values). Add it to `UserEntity` and `CredentialRecord`.
2. At sign-in, put the stamp in a claim alongside user id / username / role.
3. `CookieAuthenticationEvents.OnValidatePrincipal` runs each request: load the user by id, and **reject the principal** (`RejectPrincipal()` + `SignOutAsync`) when the user is missing, `IsDisabled`, `IsLocked` (and lock not expired), `ForcePasswordReset`, or the stamp claim ≠ the stored stamp. Also refresh the role claim from the current `IsAdmin` so a demotion takes effect immediately.
4. **Bump the stamp** (new GUID) on every security-relevant change — the operations that AUTH-1 currently guards with `RemoveSessionsForUser`:
   - `SetPasswordAsync` (completing a reset or self-service change)
   - `MarkForPasswordResetAsync` (admin forces reset)
   - `UpdateUserCoreAsync` when `IsAdmin`, `IsDisabled`, `IsLocked`, `ForcePasswordReset`, or `Username` changes
   - user deletion already ends access (row gone → validator rejects)

This makes revocation enforced at the auth layer and correct even with multiple instances (no shared in-memory session needed). The merged AUTH-1 integration tests (disable/lock/demote/reset → next request 401/403; email-only edit keeps the session) are the regression guard and must stay green.

The per-request user load is one indexed PK lookup; acceptable at this app's scale. A short `IMemoryCache` (≈10 s) keyed by user id may be added if needed, but is not required for v1.

## Cookie specifics (keep current behaviour)

- Cookie name `BookWheel.Auth`; `HttpOnly`; `SameSite=Strict`; `Secure = Request.IsHttps || SecurityOptions.AlwaysSecureCookie` (reuse OPS-1's `CookieBuilder` + `OnAppendCookie`, or set via events); `IsEssential`; `Path=/`.
- `ExpireTimeSpan = 8h`, `SlidingExpiration = true`. (This incidentally also satisfies AUTH-2's sliding-cookie intent, but AUTH-2 stays its own issue.)
- API-friendly events: `OnRedirectToLogin` → return `401`; `OnRedirectToAccessDenied` → return `403` (no HTML redirects for an API/SPA).
- Data Protection is already configured (persisted keys), so cookie encryption survives restarts.

## Controller refactor

- Remove the manual `GetAuthenticatedUser`/`IsAdmin`/403 preambles.
- Read identity from `User` claims via a small extension: `User.GetUserId()` (Guid from `NameIdentifier`), `User.GetUsername()`, `User.IsInRole("Admin")`.
- Keep behaviour identical (same JSON, same status codes). `UsersController`'s self-edit guard and audit logging read the id/username from claims.
- `AuthController.Me` still reloads email fresh by id.

## AuthService changes

- Remove the in-memory session store (`_sessions`, `CreateSession`, `GetAuthenticatedUser`, `RemoveSessionsForUser`) and the manual cookie issuance.
- `ValidateCredentialsAsync` is unchanged (lockout, disabled, force-reset, locked checks stay); login calls `HttpContext.SignInAsync` with the claims it returns.
- Keep the failed-login lockout counters and the password-reset / forgotten-username rate-limit dictionaries (not session state).
- `CompletePasswordResetAsync` no longer calls `RemoveSessionsForUser`; the stamp bump in `SetPasswordAsync` handles revocation.

## Program.cs

- `AddAuthentication(scheme).AddCookie(scheme, options => { ... events ... })`; `UseAuthentication()` then `UseAuthorization()` placed after `UseForwardedHeaders`/rate limiter and before `MapControllers`.
- `AddAuthorization` with a fallback policy `RequireAuthenticatedUser()` and an `"Admin"` policy `RequireRole("Admin")`.
- Static files and the `/`, `/index.html`, `/sw.js`, `/api/version`, health endpoints must remain reachable anonymously (they're not controller actions; the fallback policy applies to endpoints — map them with `.AllowAnonymous()` or ensure they sit outside the authorized routing).

## Tests

- **Coverage guard** (new): a reflection test enumerating every controller action and asserting each is either `[AllowAnonymous]` or covered by an authorize policy (directly or via the fallback) — fails if a new action is added unprotected.
- **Authorization boundaries**: existing 401/403 tests (incl. #142's admin-guard tests) stay green unchanged — the proof the behaviour is preserved.
- **Revocation (AUTH-1)**: existing tests stay green; add a stamp-specific test that a password reset invalidates a still-open cookie (already covered) and that a demotion flips admin access on the next request.
- **Anonymous reachability**: `/`, `/sw.js`, `/api/version`, `/health/*`, and the pre-auth auth endpoints return without auth.

## Migration & rollout

- One migration: add `security_stamp`. Non-breaking; existing cookies become invalid on deploy (everyone re-logs in once) — acceptable and expected for an auth change; note it in the PR.
- Controllers migrated together (the test suite is the safety net). No version bump beyond the unreleased 3.8.0 unless this ships in its own release.

## Out of scope

- Persistent/DB-backed sessions (CORE-2 #184).
- TOTP/2FA (AUTH-6), external OIDC (AUTH-7), breached-password checks (AUTH-4).
- Deleting MigrationController and the JSON repositories (CORE-7 #200).
