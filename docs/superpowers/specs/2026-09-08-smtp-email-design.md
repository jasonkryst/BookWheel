# SMTP Email — Design

- **Issue:** [#115 — SMTP Email](https://github.com/jasonkryst/BookWheel/issues/115)
- **Branch:** `feature/gh-93-dotnet10-lts` (to be re-branched for this work)
- **Date:** 2026-09-08

## Problem

Issue #115: *"Implement SMTP Email. Add Password Reset Email links which send email... And forgotten username. Usernames will be separate from emails. Emails will become a required field. Allow for other types of emails as well, which may be added later such as stats or that. Be sure the system is securely implemented."*

**Current state:** BookWheel already has admin-driven password-reset plumbing (`PasswordResetTokenRecord`, `AuthService.CreatePasswordResetLinkAsync`, `POST /api/users/{id}/password-reset-link`) — an administrator generates a 24-hour link and shares it with the account holder out of band. There is **no** `Email` concept anywhere in the data model, **no** self-service "I forgot my password/username" flow, and **no** SMTP/outbound-email infrastructure at all. This design adds all three, on top of the existing token machinery rather than replacing it.

Usernames are already structurally separate from any notion of email (there's no email field to conflate them with), so "usernames will be separate from emails" is satisfied by simply adding `Email` as its own column rather than repurposing `Username`.

## Scope

**In scope:**
- Nullable `Email` column on `users` (soft-required: mandatory on all newly-created accounts, optional/backfillable on accounts that predate this feature).
- `IEmailSender` / `SmtpEmailSender` (MailKit-based) + `EmailOptions` (`Smtp` config section), configured via `appsettings.json`/env vars only — no admin UI.
- `AccountEmailService`, holding the two email templates this issue asks for (password reset, forgotten username); a deliberately thin, extensible seam for future email types (e.g. stats digests), which stay out of scope for this pass.
- Two new **public, unauthenticated** self-service endpoints: `POST /api/auth/password-reset/request` (by username) and `POST /api/auth/forgot-username` (by email), both anti-enumeration (always-200, generic message) and rate-limited.
- The existing admin-triggered `POST /api/users/{id}/password-reset-link` additionally emails the link when the target account has an email on file (still returns the link in the response too, so it isn't broken by unset/failed SMTP).
- `Email` required on `POST /api/auth/setup` (via a new `SetupAccountRequest`, split out of `LoginRequest`) and `POST /api/users` (`CreateUserRequest`); optional/editable on `PUT /api/users/{id}` (`UpdateUserAccountRequest`) so admins can backfill legacy accounts.
- `Email` is unique across accounts (DB unique index + app-level duplicate check, mirroring how `Username` uniqueness is already enforced at both layers).
- Frontend: email inputs on setup and admin create/edit-user forms, an Email column in the admin users table, and "Forgot password?"/"Forgot username?" links on the login screen.
- Test coverage (repository, service, controller/integration) and documentation updates (README, `docs/audits/database.md`).
- Major version bump (`2.18.0` → `3.0.0`).

**Out of scope:**
- Self-service email editing / email-address verification. Per issue-owner direction, email stays admin-set-only for this pass — the same trust model BookWheel already uses for `Username`/`IsAdmin`/etc. No confirm-your-email flow is needed because of this.
- Any additional email types beyond password-reset and forgotten-username (stats digests, etc.) — the plumbing is built to make adding one easy, but none are implemented here.
- An admin UI for SMTP configuration — it's `appsettings.json`/env-var only, matching the existing `BookMetadata:GoogleBooks:ApiKey` / `Analytics:GoogleAnalyticsId` pattern.
- Hard-blocking migration/startup on missing emails for pre-existing accounts. Accounts without an email on file simply can't use the two new self-service flows until an admin backfills one.
- Localizing the email body text itself (subject/body stay English-only; the on-page confirmation copy for the two new endpoints still goes through the existing `en`/`es`/`pl` i18n system, since that's user-facing UI text, not mail content).

## Architecture

```
BookWheel/
  Models/
    CredentialRecord.cs                  (+ Email, nullable)
    UserAccountSummary.cs                (+ Email, nullable)
    SetupAccountRequest.cs               (new — Username, Password, Email[required])
    CreateUserRequest.cs                 (+ Email[required])
    UpdateUserAccountRequest.cs          (+ Email, nullable/optional)
    EmailOptions.cs                      (new — Smtp config section)
  Services/
    IEmailSender.cs                      (new)
    SmtpEmailSender.cs                   (new — MailKit)
    AccountEmailService.cs               (new — password-reset + forgotten-username templates)
    AuthService.cs                       (+ RequestPasswordResetAsync, RequestForgottenUsernameAsync,
                                           rate-limit dictionaries; CreateAccountAsync threads Email through;
                                           CreatePasswordResetLinkAsync now also emails the link when the
                                           target has an email on file — shared by both the admin-triggered
                                           and new self-service callers)
  Controllers/
    AuthController.cs                    (+ POST password-reset/request, POST forgot-username;
                                           Setup uses SetupAccountRequest)
    UsersController.cs                   (CreateUser/UpdateUser thread Email through; password-reset-link
                                           action itself is unchanged — the emailing lives in AuthService)
  Storage/
    ICredentialRepository.cs             (+ Email params on Create*/Update*; + FindByUsernameAsync,
                                           FindUsernameByEmailAsync; duplicate-email check on Create*/Update*)
    JsonCredentialRepository.cs          (Email round-trips as a plain nullable property — additive,
                                           no schema-version bump needed)
  Storage/Postgres/
    Entities/UserEntity.cs               (+ Email, nullable citext)
    PostgresCredentialRepository.cs      (Email plumbed through; new lookups)
    BookWheelDbContext.cs                (Email column mapping + unique index)
  Migrations/
    <timestamp>_AddUserEmail.cs          (new — nullable ADD COLUMN, unique index)
  Program.cs                             (DI: IEmailSender -> SmtpEmailSender singleton,
                                           AccountEmailService singleton)
  BookWheel.csproj                       (+ MailKit package; InformationalVersion 2.18.0 -> 3.0.0)
  appsettings.json                       (+ "Smtp": {...})
  wwwroot/
    index.html                          (Email input on setup + admin create/edit-user forms;
                                          Email column in users table; forgot-password/forgot-username
                                          links + small forms on the login screen)
    js/app.js                           (wiring for the above; i18n strings for new UI text)
    js/i18n.js                          (new keys, en/es/pl)

BookWheel.Tests/
  Services/
    SmtpEmailSenderTests.cs             (new — fake SMTP transport / unconfigured no-op path)
    AccountEmailServiceTests.cs         (new — fake IEmailSender captures subject/body/recipient)
  Storage/
    JsonCredentialRepositoryTests.cs    (+ Email round-trip, uniqueness, FindByUsernameAsync, FindUsernameByEmailAsync)
    Postgres/PostgresCredentialRepositoryTests.cs  (same additions, against the real Postgres schema)
  BookWheelApiTests.cs                  (+ setup/create-user require email; password-reset/request and
                                          forgot-username always-200 + rate-limit cases; admin reset-link
                                          also emails)

README.md                               (Features, API Overview, Testing, SMTP config docs)
docs/audits/database.md                 (note new column/unique index, extends Finding 9)
```

### Data model

`CredentialRecord`/`UserAccountSummary` gain `public string? Email { get; set; }`. `UserEntity` gains `public string? Email { get; set; }`, mapped as nullable `citext` (matching `Username`'s case-insensitive-comparison rationale — email addresses are compared case-insensitively throughout this design) with `HasIndex(u => u.Email).IsUnique()`. `citext` uniqueness is case-insensitive by construction, so `Foo@example.com` and `foo@example.com` collide, which is the correct behavior for an email address. Because the index is unique but the column is nullable, multiple accounts *can* still have `Email IS NULL` simultaneously — Postgres unique indexes never compare two `NULL`s as equal — which is exactly the legacy-account backfill state this design needs to allow.

App-level duplicate checks mirror the existing `Username` pattern exactly: `CreateInitialAccountAsync`/`CreateUserAsync`/`UpdateUserCoreAsync` pre-check for an existing non-null match (`AnyAsync(u => u.Email == normalizedEmail)` in Postgres, `OrdinalIgnoreCase` comparison in JSON) and throw `InvalidOperationException("Email already exists.")`; the Postgres repository additionally catches `DbUpdateException` where `IsUniqueViolation(ex)` around the same `SaveChangesAsync` calls that already do this for `Username`, closing the same concurrent-write race the audit already confirmed is closed for `Username` (`docs/audits/database.md` Finding 9).

The migration (`AddUserEmail`) is a single nullable `ADD COLUMN "Email" citext NULL` plus a unique `CREATE INDEX` — the safe, zero-downtime shape this repo's migrations already favor for additive nullable columns (`AddBookIsbnAuthorCover`, per `docs/audits/database.md` Finding 5); a unique index on an all-`NULL` new column costs nothing to create.

`JsonCredentialRepository`'s `CredentialDocument`/`CredentialRecord` need no schema-version bump: `System.Text.Json` deserializes older on-disk documents that lack an `Email` property as `null` automatically, and `CurrentCredentialSchemaVersion` stays `2`. The duplicate check and the forgot-username lookup both compare with `StringComparison.OrdinalIgnoreCase`, mirroring the existing `Username` comparison convention in this repository, and both skip records where `Email` is `null`.

### Repository interface changes

```csharp
public interface ICredentialRepository
{
    // existing members...
    Task<CredentialRecord> CreateInitialAccountAsync(string username, string password, string email);
    Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin, string email);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled,
        bool forcePasswordReset, bool isLocked, string? email);

    Task<CredentialRecord?> FindByUsernameAsync(string username);
    Task<string?> FindUsernameByEmailAsync(string email);
}
```

`CreateInitialAccountAsync`/`CreateUserAsync` validate `email` the same way `username` is validated today — required, non-blank, and (per the previous section) unique — with the same `InvalidOperationException` pattern for both "blank" and "already exists". The DTO-level `[Required]`/`[EmailAddress]` attributes are the first line of defense, but repositories keep validating too, matching this codebase's existing defense-in-depth pattern of checking in both the DTO and the repository. The 3-arg `UpdateUserAsync(userId, username, isAdmin)` overload has no production call site today (`UsersController` only calls the 6-arg one) — it's exercised only by `JsonCredentialRepositoryTests`/`PostgresCredentialRepositoryTests` as a convenience for tests that don't care about the security-state fields — and is left exactly as-is. `FindByUsernameAsync` returns the full record (needed to check `IsDisabled`/`Email` for the password-reset-request flow without a second round trip). `FindUsernameByEmailAsync` returns `null` unless there's an *enabled* account with that email — a disabled account's username should not leak into a forgot-username email — and, since `Email` is now unique, there is at most one match to return.

### Email subsystem

`EmailOptions` (bound from `Smtp`, same binding convention as `SecurityOptions`/`BookMetadataOptions`):

```csharp
public sealed class EmailOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Book Wheel";
}
```

`appsettings.json` gets a matching empty-by-default `"Smtp"` section; `docker-compose.yml` passes through `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__FromAddress` from new `.env` variables (`SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM_ADDRESS`), exactly mirroring the existing `GOOGLE_BOOKS_API_KEY` → `BookMetadata__GoogleBooks__ApiKey` pass-through.

```csharp
public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);
}
```

`SmtpEmailSender` (MailKit's `SmtpClient`, chosen over `System.Net.Mail` because the latter is legacy/unmaintained per Microsoft's own guidance and has weaker TLS support) implements `IEmailSender`. Two failure paths, both non-throwing so a caller can never turn an SMTP problem into a leaked signal or a 500:
- `Host` empty (not configured) → log a warning once per call, return without attempting a connection.
- Any exception during connect/authenticate/send → log an error (including the target address, never the SMTP password) and swallow it.

`AccountEmailService` is the extensible seam the issue asks for — a small class over `IEmailSender` with one method per email type today:

```csharp
public sealed class AccountEmailService
{
    public Task SendPasswordResetEmailAsync(string toAddress, string username, string resetLink, DateTimeOffset expiresAtUtc, CancellationToken ct = default);
    public Task SendForgottenUsernameEmailAsync(string toAddress, string username, CancellationToken ct = default);
}
```

Adding a future email type (e.g. a stats digest) means adding one more method here that builds its own subject/body and calls `_emailSender.SendAsync(...)` — no interface change, no new abstraction layer needed for two-to-a-handful of email types. If this class grows past a handful of unrelated email types later, splitting it is a future refactor, not something to pre-build now (YAGNI).

### Self-service flows and anti-enumeration

`AuthService` gains two rate-limited, always-generic-outcome methods, reusing the existing `ConcurrentDictionary`-based lockout pattern (`_failedLogins`) rather than introducing a new mechanism:

```csharp
public async Task RequestPasswordResetAsync(string username, string appBaseUrl, CancellationToken ct = default)
{
    if (!TryConsumeRateLimit(_passwordResetRequests, username)) return;
    var account = await _credentialRepository.FindByUsernameAsync(username);
    if (account is null || account.IsDisabled || string.IsNullOrWhiteSpace(account.Email)) return;

    await CreatePasswordResetLinkAsync(account.UserId, appBaseUrl, ct);
}

public async Task RequestForgottenUsernameAsync(string email, CancellationToken ct = default)
{
    if (!TryConsumeRateLimit(_forgottenUsernameRequests, email)) return;
    var username = await _credentialRepository.FindUsernameByEmailAsync(email);
    if (username is null) return;

    await _accountEmailService.SendForgottenUsernameEmailAsync(email, username, ct);
}
```

`CreatePasswordResetLinkAsync` itself gains the emailing step (a `CancellationToken` parameter, default `default`, added for the self-service caller's benefit): after generating the link it now checks `user.Email` (available on the `CredentialRecord` returned by `MarkForPasswordResetAsync`) and, if present, calls `_accountEmailService.SendPasswordResetEmailAsync(...)` before returning — still returning the link/expiry/username in its result exactly as today. This one change covers **both** callers: the admin-triggered `POST /api/users/{id}/password-reset-link` (unchanged controller code, now emails as a side effect, still returns the link too) and the new self-service `RequestPasswordResetAsync` above (which has nothing else to do once the shared method returns, since the response is generic either way).

Both public self-service methods are `void`-shaped from the controller's point of view — nothing about "found account?", "has email?", "rate limited?", or "SMTP send succeeded?" is ever observable in the HTTP response, closing the classic user/email-enumeration side channel this kind of endpoint is prone to. `TryConsumeRateLimit` is a small helper mirroring `ValidateCredentialsAsync`'s existing lockout bookkeeping: a fixed cap (3 requests per identifier per 15 minutes, matching the order of magnitude of `SecurityOptions.UsernameLockoutThreshold`) tracked in a `ConcurrentDictionary<string, RateLimitRecord>` keyed by the normalized username/email — over-cap calls are dropped silently (still a generic `200` from the controller), not surfaced as a `429`, for the same anti-oracle reason.

`AuthController` adds:

```csharp
[HttpPost("password-reset/request")]
public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request)
{
    var appBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
    await _authService.RequestPasswordResetAsync(request.Username, appBaseUrl, HttpContext.RequestAborted);
    _logger.LogInformation("Password reset requested. Username {Username} from {ClientIp} request {RequestId}", request.Username, GetClientIp(), GetRequestId());
    return Ok(new { message = _errors.Localize("If that account exists and has an email on file, a reset link has been sent.") });
}

[HttpPost("forgot-username")]
public async Task<IActionResult> ForgotUsername([FromBody] ForgotUsernameRequest request)
{
    await _authService.RequestForgottenUsernameAsync(request.Email, HttpContext.RequestAborted);
    _logger.LogInformation("Forgotten-username requested from {ClientIp} request {RequestId}", GetClientIp(), GetRequestId());
    return Ok(new { message = _errors.Localize("If that email is on file, we've sent the associated username.") });
}
```

Both new request models (`RequestPasswordResetRequest { Username }`, `ForgotUsernameRequest { Email }`) get the same `[Required]`/length validation style as `LoginRequest`; `Email` additionally gets `[EmailAddress]`. Logging follows this controller's existing structured-logging convention (client IP, path, request id) — logging *that* a request happened is fine and matches how login attempts are already logged; only the HTTP *response* stays generic.

Because the emailing lives inside the shared `CreatePasswordResetLinkAsync`, `UsersController.CreatePasswordResetLink` (the existing admin-triggered flow) needs **no code change** to start emailing the link — it already calls that method and returns its result unchanged. The response shape stays the same, so an admin can still copy/share the link manually when SMTP isn't configured or delivery fails.

### Required email on account creation, without touching login

`LoginRequest` is reused today for both `/api/auth/login` and `/api/auth/setup`. Adding a required `Email` there would incorrectly force every *login* call to carry one. Instead, `POST /api/auth/setup` switches to a new `SetupAccountRequest` (`Username`, `Password`, `Email` — `Email` is `[Required][EmailAddress]`), and `AuthController.Setup`/`AuthService.CreateAccountAsync` thread the email through to `CreateInitialAccountAsync`. `CreateUserRequest` (admin-creates-a-user) gets the same `[Required][EmailAddress] Email`. `UpdateUserAccountRequest` gets `string? Email` with only `[EmailAddress]` (no `[Required]`) — omitted/blank is valid, since this is the one path allowed to leave/set a legacy account's email empty. Setting it to a value already used by another account returns `400` with the same `"Email already exists."` message the duplicate-username path already uses for `Username`.

### Frontend

`index.html`: an `<input type="email">` added to the setup form and the admin create-user form (both marked required in markup + validated server-side regardless), an optional email input in the admin edit-user dialog, and an `Email` column in the admin users table (rendering "—" for accounts with none). The login card gets two small text links, "Forgot password?" and "Forgot username?", each expanding a one-field inline form (username, or email respectively) that posts to the new endpoints and replaces itself with the generic confirmation message — no client-side branching on whether the account/email "really" existed, since the server never says.

`app.js`/`i18n.js`: wiring for the above, plus new i18n keys (`en`/`es`/`pl`) for the forgot-password/forgot-username links, their inline forms, and the generic confirmation text — the same three-locale completeness bar every other UI string in this app already meets.

## Testing

- `SmtpEmailSenderTests` — unconfigured `Host` → no-op, no exception; simulated transport failure → logged and swallowed, not rethrown. (No real SMTP server is contacted in tests; MailKit's client is exercised against a stub/mock transport or the send path is isolated behind a thin wrapper that tests can substitute.)
- `AccountEmailServiceTests` — password-reset email contains the link/expiry/username; forgotten-username email contains the username passed in; both delegate to the injected fake `IEmailSender` with the right `toAddress`.
- `JsonCredentialRepositoryTests` — `Email` round-trips through create/update; a document written before this change (no `Email` property) still deserializes with `Email == null`; two accounts may both have `Email == null` simultaneously; creating/updating a second account with an already-used email (case-insensitive) throws `InvalidOperationException`; `FindByUsernameAsync` returns the full record incl. `Email`/`IsDisabled`; `FindUsernameByEmailAsync` returns the matching enabled account's username, `null` when no match or the only match is disabled.
- `PostgresCredentialRepositoryTests` — the same set of cases against the real schema (via `PostgresTestFixture`/Testcontainers), plus: the unique index actually rejects a concurrent duplicate insert (`DbUpdateException` → translated `InvalidOperationException("Email already exists.")`, mirroring the existing `Username` race-condition test); two `Email IS NULL` rows coexist without violating the unique index; a pre-existing row with `Email IS NULL` is unaffected by the migration.
- `BookWheelApiTests` (integration, real Postgres via Testcontainers):
  - `POST /api/auth/setup` and `POST /api/users` reject a missing/malformed email with `400`, and reject an email already used by another account with `400`.
  - `PUT /api/users/{id}` rejects changing an account's email to one already used by another account with `400`.
  - `POST /api/auth/password-reset/request` returns the same generic `200` whether the username exists or not, is disabled, or has no email — and an email is actually sent (captured via a test-double `IEmailSender`, matching this suite's existing "swap real dependency for a fake in `BookWheelWebAppFactory`" pattern, e.g. `FakeGoogleBooksMetadataLookupService`) only in the found/enabled/has-email case.
  - Repeated requests beyond the rate-limit cap still return `200` but stop triggering additional sends.
  - `POST /api/auth/forgot-username` emails back the one matching account's username; a non-matching or disabled-account email still returns the generic `200` with no email sent.
  - `POST /api/users/{id}/password-reset-link` (admin path) still returns the link in its response and now also triggers a send when the target has an email.
- `BookWheelFrontendTests`-style checks: setup/create-user forms require the email field; forgot-password/forgot-username links render and their i18n keys are present across `en`/`es`/`pl`.

## Documentation

- `README.md`: Features bullet describing password-reset/forgot-username email delivery and the admin-set, required-for-new-accounts email field; API Overview additions for `POST /api/auth/password-reset/request` and `POST /api/auth/forgot-username`; a new "Email (SMTP)" config subsection documenting the `Smtp:*` settings and their env-var overrides, following the existing Google Books API key write-up's structure; Testing section notes the new fakes.
- `docs/audits/database.md`: a short addendum under "Schema Design Findings" (§3, alongside the existing `citext`-for-`Username` note) recording that `users.Email` follows the identical pattern — nullable `citext`, unique index, app-level duplicate check plus `DbUpdateException`/`UniqueViolation` handling — so Finding 9's "uniqueness enforced at both layers" statement now covers `Email` too, not just `Username`.
- `BookWheel/BookWheel.csproj`: bump `InformationalVersion` from `2.18.0` to `3.0.0` — justified as a major bump (not minor) because email becomes a required field on every new-account creation path, a behavior change to an existing public contract (`POST /api/auth/setup`, `POST /api/users`), on top of adding new unauthenticated endpoints.
