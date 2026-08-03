# Storage Layer Abstraction — Design

- **Issue:** [#14 — Storage Layer Abstraction](https://github.com/jasonkryst/BookWheel/issues/14)
- **Milestone:** V2.0.0
- **Related:** [#15 — STORAGE - SQL/NoSQL Storage Layer Implementation](https://github.com/jasonkryst/BookWheel/issues/15) (follow-up; not implemented by this design)
- **Branch:** `14`
- **Date:** 2026-08-03

## Problem

Issue #14: *"Storage CRUD operations should be abstracted to allow for writing to a local storage, backend Json or even SQL/nosql"*.

Today `BookStore` and `CredentialStore` (`BookWheel/Services/`) are concrete classes with no interfaces, registered as DI singletons and depended on directly by controllers and services. There is no seam to swap in a different storage backend (e.g. SQL/NoSQL, per #15) without touching every consumer.

Both classes also mix three concerns in one type:
1. Domain CRUD operations (add/update/remove/query).
2. Legacy-format migration (`HasLegacyPayloadAsync` / `MigrateLegacyPayloadAsync`).
3. JSON-file-specific corruption quarantine handling.

`CredentialStore` additionally owns two distinct data types — user accounts and password-reset tokens — under one shared file lock, with `CreatePasswordResetLinkAsync` and `CompletePasswordResetAsync` touching both in a single quasi-atomic operation.

## Scope

**In scope:** Introduce repository interfaces for the domain CRUD surface and refactor the existing JSON-file code to implement them. The JSON implementation remains the *only* implementation — this design does not add a second storage backend.

**Out of scope (deferred to #15):** Any actual SQL/NoSQL implementation, configuration-driven backend selection, or schema/migration tooling for a database backend.

**No observable behavior change** for any existing endpoint is intended, except the internal sequencing described in the "Password-reset orchestration" section below (functionally equivalent, marginally weaker cross-file atomicity).

## Architecture

New `BookWheel/Storage/` folder holds three interfaces and their JSON-backed implementations. Business logic depends on the **interfaces**; storage-format-specific concerns (legacy migration, corrupt-file quarantine) stay on the **concrete JSON classes** only — an interface should describe what any implementation can do, and "recover from a corrupted JSON file" is not something a future SQL backend would ever need to do.

```
BookWheel/
  Storage/
    IBookRepository.cs
    JsonBookRepository.cs                (renamed from Services/BookStore.cs)
    ICredentialRepository.cs
    JsonCredentialRepository.cs          (split out of Services/CredentialStore.cs)
    IPasswordResetTokenRepository.cs
    JsonPasswordResetTokenRepository.cs  (split out of Services/CredentialStore.cs)
  Services/
    AuthService.cs           -> ICredentialRepository + IPasswordResetTokenRepository
    DataMigrationService.cs  -> JsonBookRepository + JsonCredentialRepository (concrete)
    AppMetricsService.cs     -> IBookRepository (passed as a GetSnapshotAsync parameter, as BookStore is today)
    StartupDiagnosticsService.cs -> concrete Json* types (path/writability checks unchanged)
```

## Components

```csharp
// Storage/IBookRepository.cs
public interface IBookRepository
{
    Task<IReadOnlyList<BookRecord>> GetAllAsync(Guid userId);
    Task<BookRecord> AddAsync(Guid userId, string title);
    Task<BookRecord> UpdateAsync(Guid userId, Guid id, string title);
    Task<BookRecord> RemoveAsync(Guid userId, Guid id);
    Task<BookRecord> SelectRandomAsync(Guid userId);
    Task<int> RemoveUserDataAsync(Guid userId);
    Task<int> GetTotalBookCountAsync();
}
```

```csharp
// Storage/ICredentialRepository.cs
public interface ICredentialRepository
{
    Task<bool> HasAccountAsync();
    Task<CredentialRecord> CreateInitialAccountAsync(string username, string password);
    Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password);
    Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync();
    Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked);
    Task<UserAccountSummary> DeleteUserAsync(Guid userId);
    Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId);
}
```

```csharp
// Storage/IPasswordResetTokenRepository.cs
public interface IPasswordResetTokenRepository
{
    Task<(string RawToken, DateTimeOffset ExpiresAtUtc)> CreateAsync(Guid userId);
    Task<PasswordResetTokenValidationResult> ValidateAsync(string token);
    Task<Guid> CompleteAsync(string token); // consumes the token, returns the owning userId
}
```

Migration/quarantine methods (`HasLegacyPayloadAsync`, `MigrateLegacyPayloadAsync`, corrupt-file quarantine) remain public methods on `JsonBookRepository` and `JsonCredentialRepository` only — not part of any interface.

### Password-reset orchestration

`CredentialStore.CreatePasswordResetLinkAsync` currently updates the user record and creates a reset token under one shared file lock. Splitting the data types means this becomes two separately-locked operations. That coordination moves up to `AuthService`, which already wraps credential operations for the auth surface:

```csharp
// AuthService.cs
public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)>
    CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl)
{
    var user = await _credentialRepository.MarkForPasswordResetAsync(userId);
    var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(userId);
    var resetLink = BuildResetLink(appBaseUrl, rawToken);
    return (resetLink, expiresAtUtc, user.Username);
}

public async Task<string> CompletePasswordResetAsync(string token, string newPassword)
{
    var userId = await _resetTokenRepository.CompleteAsync(token);
    return await _credentialRepository.SetPasswordAsync(userId, newPassword);
}
```

`UsersController` calls `AuthService.CreatePasswordResetLinkAsync` instead of calling the former `CredentialStore.CreatePasswordResetLinkAsync` directly (used in both the initial-setup-link and admin-triggered-reset-link code paths).

The brief window between the user being marked for reset and the token existing is functionally harmless — a valid token is still required to complete a reset regardless of the user record's flag state.

## DI wiring

Each JSON implementation is registered once as a concrete singleton, then exposed under its interface pointing at the same instance, preserving today's per-file singleton lock semantics:

```csharp
builder.Services.AddSingleton<JsonBookRepository>();
builder.Services.AddSingleton<IBookRepository>(sp => sp.GetRequiredService<JsonBookRepository>());

builder.Services.AddSingleton<JsonCredentialRepository>();
builder.Services.AddSingleton<ICredentialRepository>(sp => sp.GetRequiredService<JsonCredentialRepository>());

builder.Services.AddSingleton<JsonPasswordResetTokenRepository>();
builder.Services.AddSingleton<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<JsonPasswordResetTokenRepository>());
```

Consumer changes:

- `BooksController` → `IBookRepository` (was `BookStore`)
- `UsersController` → `AuthService` (for password-reset-link creation) + `ICredentialRepository` (list/create/update/delete) — was `CredentialStore` directly
- `AuthService` → `ICredentialRepository` + `IPasswordResetTokenRepository` (was `CredentialStore`)
- `AppMetricsService.GetSnapshotAsync` → takes `IBookRepository` as its parameter (was `BookStore`); `MetricsController` holds `IBookRepository` and passes it through
- `DataMigrationService`, `StartupDiagnosticsService` → concrete `JsonBookRepository` / `JsonCredentialRepository` (unchanged behavior, types renamed)

`BookWheelWebAppFactory` (test host) updates its `RemoveAll<BookStore>()` override to `RemoveAll<JsonBookRepository>()` plus the corresponding interface re-registration, keeping the temp-content-root test pattern working.

## Error handling

No new error-handling patterns. `InvalidOperationException` for domain errors (not found, duplicate username, last-admin protection, first-account protection) and `CorruptedDataException` for quarantine cases keep their existing call sites, messages, and HTTP-response mapping in controllers — they simply relocate to the `Json*Repository` classes.

## Testing

**New unit tests** — `BookWheel.Tests/Storage/JsonBookRepositoryTests.cs`, `JsonCredentialRepositoryTests.cs`, `JsonPasswordResetTokenRepositoryTests.cs` — constructed directly against a temp `App_Data` directory (same temp-content-root pattern `BookWheelWebAppFactory` already uses):

- **Positive:**
  - Add / update / remove / select-random a book; total count and per-user isolation.
  - Create initial account (becomes admin); validate correct credentials.
  - Create / update / delete a user; list users.
  - Issue a password-reset token, validate it, complete it (password updates, token becomes single-use).
- **Negative:**
  - Update/remove a nonexistent book → throws `InvalidOperationException`.
  - Validate wrong password / unknown username → returns null.
  - Create a user with a duplicate (case-insensitive) username → throws.
  - Demote/delete the last remaining admin → throws.
  - Delete the first-created account → throws.
  - Validate/complete an expired, already-used, or unknown reset token → invalid result / throws.
  - Corrupted `books.json` or `user.cred` payload → quarantined to `App_Data/corrupt/` and `CorruptedDataException` thrown.

**Existing `BookWheel.Tests/BookWheelApiTests.cs` integration tests must continue to pass unmodified** — they are the regression guard proving the refactor did not change observable behavior end-to-end through the controllers.

## Documentation updates

- `README.md`: update "Solution Structure" tree to show the new `Storage/` folder; no functional/API doc changes since no endpoints change shape.
- `IMPROVEMENT_ROADMAP.md`: mark storage abstraction as done under whichever section tracks #14/#15, and note #15 (SQL/NoSQL backend) as the remaining follow-up.
- `SECURITY_AUDIT_REPORT.md`: reviewed for any references to `BookStore`/`CredentialStore` by name; update if present so the report doesn't point to renamed/moved types.
