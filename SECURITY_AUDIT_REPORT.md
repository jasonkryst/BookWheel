# Security Audit Report - Book Wheel

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
