# Security Audit Report - Book Wheel

Date: 2026-08-19
Auditor: Claude (Sonnet 5), via Claude Code
Scope: Application project in BookWheel and solution-level dependency review, focused on the PostgreSQL storage-layer migration (#55) shipped since the prior audit (2026-06-01) and a full re-verification of previously reported findings

## Executive Summary

This audit refreshes the 2026-06-01 report against the current codebase, which has since migrated book, credential, and password-reset-token storage from encrypted JSON files to PostgreSQL via EF Core (see `docs/superpowers/plans/2026-08-18-postgresql-migration.md`). All scans and tests below were re-run fresh for this audit rather than reused from prior results.

Overall posture: Low risk

- Critical findings: 0
- High findings: 0 (1 found and remediated during this audit cycle — see below)
- Medium findings: 0
- Low findings: 2 (1 new, 1 carried-forward finding closed — see "Previously Reported Findings" below)

Security-relevant changes verified in this revision:

- Books, credentials, and password-reset tokens now persist in PostgreSQL instead of encrypted JSON files; controllers and `AuthService` are unchanged (repository interfaces were not modified)
- Passwords remain hashed with ASP.NET Core's `PasswordHasher<string>` — same algorithm and call signature as the JSON-backed implementation, so no regression in password storage strength
- Usernames moved from an `IDataProtector`-encrypted JSON blob to a plain PostgreSQL `citext` column, enforced unique and case-insensitive at the database level (`HasIndex(u => u.Username).IsUnique()`), a deliberate tradeoff documented in the migration plan — production deployments are expected to rely on TLS-in-transit and disk/volume encryption instead of application-level username encryption
- Password reset tokens are stored as a hash only (`TokenHash`, unique-indexed); the raw token is never persisted
- All PostgreSQL data access goes through EF Core LINQ query composition — no raw/interpolated SQL (`FromSqlRaw`, `ExecuteSqlRaw`, string-built queries) was found anywhere in `BookWheel/`, so the migration does not introduce a SQL-injection surface
- `PasswordHash` is never referenced outside the storage layer and migration service — confirmed not exposed through any controller or API response DTO
- The PostgreSQL connection string is read once from configuration and is never logged or included in exception messages
- The one-shot `--migrate-to-postgres` CLI tool refuses to run if PostgreSQL already contains user data (verified in code: `if (await context.Users.AnyAsync()) throw ...`), and its stdout JSON report contains only counts/timestamps, never credential material
- `docker-compose.yml`'s bundled Postgres service uses a documented local-dev-only default password, overridable via `.env`

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

**Finding identified and remediated during this audit cycle (High, resolved):** the scan above is clean only as of this revision. At the start of this audit, `BookWheel.Tests.csproj` failed the scan: `Testcontainers.PostgreSql` 3.10.0 (added by the Postgres migration) pulls in `SSH.NET` 2023.0.0 transitively, flagged High severity by [GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284). This was also the cause of the CI `dependency-audit` job failing on `main` since PR #55 merged. Fixed by pinning `SSH.NET` directly to 2026.0.0 in `BookWheel.Tests.csproj` (PR #56). Verified: (a) the scan above is now clean, (b) the version jump doesn't break `Testcontainers` functionality — full test suite passes including all Postgres-container-backed tests.

Full test suite:

```
dotnet test BookWheel.slnx --verbosity normal
  -> Total tests: 145, Passed: 145
```

Targeted security regression tests:

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

dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter \
  "FullyQualifiedName~PostgresCredentialRepositoryTests|FullyQualifiedName~PostgresPasswordResetTokenRepositoryTests"
  -> Total tests: 17, Passed: 17
```

The second filter directly exercises the new PostgreSQL storage layer's security-relevant behavior against a real `Testcontainers`-provisioned database: case-insensitive unique-username enforcement, duplicate-username rejection, last-admin-demotion guard, first-account deletion protection, and password-reset token lifecycle.

## Methodology

- Manual review of the new PostgreSQL storage layer (`BookWheel/Storage/Postgres/`), migration service, and `Program.cs` wiring — authentication, session handling, authorization gates, data persistence, and startup configuration
- Grep-based static checks for raw/interpolated SQL, connection-string logging, and password-hash exposure through API-facing code
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

- In a deployment where the app and PostgreSQL are not on the same trusted network segment, credentials and query data (including plaintext-visible usernames, now that they're no longer `IDataProtector`-encrypted at the application layer) could traverse the network unencrypted without an operator noticing, since the connection would still succeed.

Recommendations:

1. Set `SSL Mode=Require` (or `VerifyFull` with a configured trusted CA) explicitly in production connection strings.
2. Document this alongside the existing "TLS-in-transit to Postgres" guidance in the migration plan and README, since that guidance currently describes an expectation rather than an enforced configuration.

## Previously Reported Findings — Status

### Data Protection key storage not explicitly configured (Low, reported 2026-06-01) — **Closed**

Verified in `Program.cs`: production startup now resolves `DataProtection:KeyDirectory` from configuration, falling back to a documented default (`App_Data/DataProtection-Keys`) when unset outside Development/Testing, and persists keys via `PersistKeysToFileSystem`. This matches the "[Done]" status already recorded in `IMPROVEMENT_ROADMAP.md` Priority 1, item 5. No further action needed.

## Positive Observations

- The Postgres migration preserved all prior security properties: encrypted-at-rest credential storage requirement is now met via disk/volume + TLS expectations rather than application-level encryption, password hashing algorithm is unchanged, and reset tokens remain hash-only.
- Repository interfaces (`IBookRepository`, `ICredentialRepository`, `IPasswordResetTokenRepository`) were not modified, so controllers and `AuthService` — and their existing security tests — did not need to change to support the new backend.
- Case-insensitive username uniqueness is now enforced at the database level (`citext` + unique index) rather than only in application code, closing a class of race-condition duplicate-account bugs the file-based version could theoretically have had.
- The one-shot migration tool's refuse-if-data-exists guard and transactional copy prevent silent data duplication or partial migrations.
- No SQL injection surface was introduced — 100% EF Core LINQ, no raw SQL found in the new storage layer.
- Non-admin users remain denied access to user-management and metrics endpoints (re-verified).
- Login lockout/backoff, forwarded-header-aware rate limiting, and request correlation logging all continue to function correctly against the new storage backend (re-verified).
- Auth cookies remain `HttpOnly` and `SameSite=Strict`; HTTPS redirection and HSTS remain enabled outside testing.
- The dependency-audit gate did its job this cycle — it caught a real High-severity transitive vulnerability introduced by a new package, which is exactly the failure mode it exists to prevent.

## Prioritized Remediation Plan

### Immediate (0-2 days)

1. Merge PR #56 (SSH.NET pin) so the CI `dependency-audit` gate is green on `main` again.

### Short Term (1-2 weeks)

1. Pin an explicit `SSL Mode` for production PostgreSQL connection strings (Finding #2).
2. Decide on and document a DDL-privilege strategy for schema migrations vs. runtime DML access (Finding #1).

### Mid Term (2-6 weeks)

1. Evaluate ASP.NET Core Identity or an external OIDC provider now that the data layer is production-grade (already tracked in `IMPROVEMENT_ROADMAP.md`).
2. Revisit whether `App_Data/books.json` and `App_Data/user.cred` — left on disk as a historical backup by the migration tool — should have a documented retention/deletion policy, since they contain the same sensitive data the Postgres migration was meant to modernize custody of.

## Audit Limitations

- No dynamic penetration testing was performed.
- No infrastructure, reverse proxy, firewall, or environment hardening review was performed.
- No external SAST/DAST tool results were included beyond NuGet vulnerability scanning and integration tests.
- TLS/SSL behavior (Finding #2) was assessed by reading configuration and Npgsql's documented default behavior, not by capturing live network traffic.

## Conclusion

The PostgreSQL migration was executed without regressing any previously verified security control, and the dependency-audit gate caught a real vulnerability the migration introduced (now remediated in PR #56). Remaining risk is limited to two Low-severity, easily addressed configuration hardening items: explicit TLS enforcement on the database connection, and clarifying the DDL-privilege footprint of automatic startup migrations. With those addressed, the solution remains well-positioned for reliable production operation.
