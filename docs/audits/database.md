# BookWheel Database Design & Operations Audit

**Scope:** EF Core 9 + Npgsql code-first schema, migration history, live PostgreSQL 16 schema (container `bookwheel-postgres`), repository layer query patterns, connection/privilege configuration, and legacy migration tooling.
**Date:** 2026-09-04
**Method:** Static review of `BookWheel/Migrations/*`, `BookWheel/Storage/Postgres/*`, `Program.cs`, `docker-compose.yml`, `appsettings.json`, `SECURITY_AUDIT_REPORT.md`, `IMPROVEMENT_ROADMAP.md`, `README.md`; read-only live-schema inspection via `psql` (`\d+`, `\di`, `SELECT` only — no writes or DDL were executed against the live database, and no containers were restarted).

---

## Executive Summary

BookWheel's PostgreSQL schema is small, coherent, and the migration history is clean — six of seven migrations are purely additive, live schema matches the EF Core model snapshot exactly (no drift), and `username` uniqueness is enforced at both the application layer and the database layer (unique index + `DbUpdateException` handling), closing the race-condition gap that pattern usually leaves open. Those are real strengths for a project this size.

However, the audit surfaced one finding materially more serious than what the existing security documentation describes. The live database role the application connects with (`bookwheel`) is not merely DDL-capable — `pg_roles` shows `rolsuper = t`: **it is a full PostgreSQL superuser**, verified live via `SELECT rolname, rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = current_user;`. `SECURITY_AUDIT_REPORT.md` and `IMPROVEMENT_ROADMAP.md` (Priority 1, item 10) already flag "the app's connection needs DDL privileges" as an open item — the actual live grant is a strictly larger blast radius than that finding describes, and the write-up should be updated to say so.

Beyond that, the two other notable gaps are: (1) `UsersController.DeleteUser` performs three independent `SaveChangesAsync` calls across three separate `DbContext` instances (delete user credential row, purge books, purge spin history) with no enclosing transaction — a crash between steps leaves orphaned rows; and (2) the soft-delete query filter (`DeletedAtUtc == null`), applied to essentially every books query, has no supporting index — currently invisible at 18 rows but a real cost once the table grows. Overall assessment: a well-organized schema for its scale, let down by one high-severity operational/privilege gap and a couple of straightforward, low-effort fixes.

---

## Schema Design Findings

### 1. No FK-navigation / no DB-level FK constraints — confirmed, evaluated

Verified against every migration and the live schema: relationships are modeled purely as bare `Guid`/`int` columns with `HasIndex` (`books.UserId`, `books.BookTypeId`, `books.CreatedByUserId`, `books.LastUpdatedByUserId`, `spin_selections.UserId`/`BookId`, `password_reset_tokens.UserId`) — none of these are `FOREIGN KEY` constraints in the live `\d+` output. `BookWheelDbContext.OnModelCreating` never calls `HasOne`/`HasForeignKey`; there are no C# navigation properties on any entity.

- **Pro:** simpler migrations (no cascade-ordering concerns, no migration failures from FK violations during backfills), and the `AddBookAuditFields` migration's backfill (`UPDATE books SET "CreatedByUserId" = "UserId"`) would have needed to run before any FK could be added anyway.
- **Con, verified concretely:** `UpdateBookRequest.BookTypeId` is validated only with `[Range(1, 3)]` (`BookWheel/Models/UpdateBookRequest.cs:22`) — a hardcoded magic-number mirror of the three `book_types` seed rows. If an admin ever adds a fourth `book_type` via direct SQL (there's no admin UI for it), this attribute silently rejects valid IDs until someone remembers to update the range. Conversely, nothing at the DB level stops an `books.BookTypeId` value that doesn't exist in `book_types`, or a `books.UserId`/`spin_selections.UserId` that doesn't exist in `users`, from being written by anything other than the app's own validated code paths (a bad migration, a manual `psql` fix, a future feature that bypasses the repository layer). `PostgresSpinHistoryRepository.GetForUserAsync`/`PostgresSpinStatsRepository` already defensively `LEFT JOIN` books with `IgnoreQueryFilters()` and null-coalesce the title (`"(deleted)"`) — a tacit admission that orphan-tolerant code is already required for the *expected* soft-delete case, and would silently also paper over a *bug-caused* orphan the same way, which is the real risk this pattern accepts.
- **Verdict:** reasonable tradeoff for this project's size and its single-writer-per-row access pattern, but it depends entirely on every write path going through the repository layer. Worth a one-line comment in `BookWheelDbContext` documenting this as a deliberate choice, so a future contributor doesn't "helpfully" add `HasForeignKey` inconsistently to just one entity.

### 2. Primary key types — consistent and justified

Every entity uses a `Guid` PK except `book_types`, which uses `integer` with `IdentityByDefaultColumn` (verified live: `Id | integer | generated by default as identity`). This is justified: `book_types` is a small, effectively-enum lookup table seeded via `HasData`, never client-generated, and referenced by the app as a plain int with range validation rather than a real FK — using `Guid` there would add no value. All user-data tables (`books`, `users`, `spin_selections`, `password_reset_tokens`) use `Guid`, generated client-side (`Guid.NewGuid()` in every repository's `AddAsync`/`RecordAsync`/`CreateUserAsync`), which is consistent and avoids any need for `RETURNING` round-trips.

### 3. `citext` used correctly for `users.Username`

`Username` is `citext` (verified live and in `InitialCreate`), giving case-insensitive uniqueness/lookup without app-side `ToLower()` normalization scattered across query code — a good, deliberate choice, and the `citext` extension is registered via `AlterDatabase().Annotation("Npgsql:PostgresExtension:citext", ...)` in `InitialCreate` rather than assumed pre-installed.

### 4. Live schema vs. model snapshot — no drift found

Ran `\d+` against all six application tables (`books`, `book_types`, `users`, `password_reset_tokens`, `spin_selections`, `__EFMigrationsHistory`) and cross-checked column names, types, nullability, and defaults against `BookWheelDbContextModelSnapshot.cs`. Every column, type, default (`now()` on `books.CreatedAtUtc`, `false` on `AddedByScanner`, `1` on `BookTypeId`), and index matched exactly. `__EFMigrationsHistory` shows all seven migrations applied, all at `ProductVersion 9.0.19`, consistent with the pinned EF Core version in `BookWheel.csproj`. **No remediation needed here** — this is a positive finding, listed under design findings only because it was one of the explicit review items.

---

## Migration Strategy Findings

### 5. Migration history is almost entirely additive — one migration needs care

Reviewed all seven migrations in order:

| Migration | Nature | Zero-downtime safe? |
|---|---|---|
| `InitialCreate` | New tables | Yes (bootstrap) |
| `AddBookIsbnAuthorCover` | 3 nullable `ADD COLUMN` | Yes |
| `AddBookSoftDeleteAndSpinHistory` | 1 nullable `ADD COLUMN` + new table | Yes |
| `AddBookScannerFlag` | 1 `NOT NULL ADD COLUMN` with `defaultValue: false` | Yes — Postgres 11+ applies a fixed default without a table rewrite/lock |
| `AddBookTypeTable` | 1 `NOT NULL ADD COLUMN` with `defaultValue: 1`, new table, seed `InsertData` | Yes, same reasoning |
| `AddBookCreatedAt` | 1 `NOT NULL ADD COLUMN` with `defaultValueSql: "now()"` | **Caution** — a *non-constant* default requires Postgres to evaluate `now()` per existing row at `ALTER TABLE` time rather than store a single catalog default, so this **does rewrite the table** and take an `ACCESS EXCLUSIVE` lock for the duration, unlike the two migrations above it. On a large `books` table this could stall reads/writes briefly during a rolling deploy. At current row counts (18 rows) this is immaterial, but call it out for anyone using this migration as a template at scale.
| `AddBookAuditFields` | `NOT NULL ADD COLUMN` w/ default, **raw SQL backfill** (`UPDATE books SET "CreatedByUserId" = "UserId"`), then `ALTER COLUMN ... DROP DEFAULT`, plus 2 nullable columns + 2 indexes | Safe in effect (no drops/renames/type changes) but is the migration doing the most work — it's a three-step add-default/backfill/drop-default pattern that is correct but means this migration briefly holds the table under a full rewrite (for the `ADD COLUMN ... DEFAULT`) and then a second full-table `UPDATE` (the backfill), i.e. two passes over every row. Correct and reversible (`Down` restores column drops), just worth knowing it's not a single cheap operation.

**No migration drops, renames, or changes the type of an existing column** — there is nothing in the history that would be unsafe to roll forward without an explicit backward-compatibility window (the classic zero-downtime hazard). The one thing to flag for future migrations: prefer nullable-then-backfill-then-not-null in two separate migrations (as `AddBookAuditFields` does for `CreatedByUserId` via SQL, though as one migration) over a single migration with `defaultValueSql` on a `NOT NULL` column, once table size makes the rewrite cost non-trivial.

### 6. `MigrateAsync()` at every startup — concurrent-replica risk

Confirmed in `Program.cs:140-145`: every process startup opens a scoped `DbContext` and calls `Database.MigrateAsync()` unconditionally, before the app starts serving traffic. Today's `docker-compose.yml` runs a single `bookwheel` container, so this is safe in the deployed topology.

Reasoned assessment for the hypothetical the task asked about (e.g. Kubernetes with `replicas > 1`): **EF Core's migrator does not take any cross-process advisory lock.** `Database.MigrateAsync()` reads `__EFMigrationsHistory`, computes the pending-migration list, and applies each pending migration's `Up()` inside its own transaction — but nothing serializes two processes that both compute "these N migrations are pending" at the same moment. Two replicas starting concurrently against a fresh or lagging database could both attempt to run the same `CREATE TABLE`/`ALTER TABLE`. Because PostgreSQL DDL is transactional, the *loser* of such a race fails cleanly with a "relation already exists" (or similar) error and rolls back — it will not corrupt the schema or leave a half-applied migration — but the losing replica's startup will crash on that unhandled exception, and depending on the orchestrator's restart policy this could loop until the winner's transaction commits and `__EFMigrationsHistory` catches up. This is a real but *contained* risk (transactional DDL is the safety net, not EF Core), and is only relevant if BookWheel is ever scaled horizontally — worth a one-line README/roadmap caveat if that's ever on the table, but not an issue for the current single-replica deployment.

---

## Data Integrity Findings

### 7. `UsersController.DeleteUser` is not transactional — confirmed, concrete

`BookWheel/Controllers/UsersController.cs:215-220`:

```csharp
var deletedUser = await _credentialRepository.DeleteUserAsync(id);   // DbContext #1, own SaveChangesAsync
var removedBooks = await _bookStore.RemoveUserDataAsync(id);         // DbContext #2, own SaveChangesAsync
await _spinHistory.RemoveUserDataAsync(id);                          // DbContext #3, own SaveChangesAsync
_authService.RemoveSessionsForUser(id);
```

Each repository call (`PostgresCredentialRepository.DeleteUserAsync`, `PostgresBookRepository.RemoveUserDataAsync`, `PostgresSpinHistoryRepository.RemoveUserDataAsync`) opens its **own** `DbContext` via `_contextFactory.CreateDbContextAsync()` and commits independently — there is no `Database.BeginTransactionAsync()` or `TransactionScope` wrapping the sequence in the controller. A process crash or unhandled exception between any two of these steps leaves orphaned data: e.g. user row deleted but their `books`/`spin_selections` rows remain forever (since nothing else ever purges by a now-nonexistent `UserId`, and there's no FK-cascade to fall back on per Finding 1). This is the exact "multi-step write that should be atomic" scenario the review was asked to check, and it is not handled. **Recommendation:** wrap these three calls in a single `Database.BeginTransactionAsync()` against one shared `DbContext`, or expose a single repository method that performs the delete-and-purge inside one transaction.

By contrast, `PostgresMigrationService.RunAsync()` (the one-time JSON→Postgres import) *does* wrap its multi-entity writes in `context.Database.BeginTransactionAsync()` / `CommitAsync()` correctly — so the codebase clearly knows the pattern, it just wasn't applied to `DeleteUser`.

### 8. Single-row writes are correctly atomic

`PostgresBookRepository.AddAsync`/`UpdateAsync`/`RemoveAsync` (soft delete) and `PostgresSpinHistoryRepository.RecordAsync` are each a single entity mutation plus one `SaveChangesAsync()` — inherently atomic, no transaction needed. Recording a spin selection has no other "related audit/count field" to keep in sync (stats in `PostgresSpinStatsRepository` are computed live via aggregation queries, not denormalized counters), so there is no hidden atomicity gap there.

### 9. Username uniqueness — enforced at the DB level, not just app code

`IX_users_Username` is a live `UNIQUE` index on `citext` (confirmed via `\d+ users`), and `PostgresCredentialRepository.CreateUserAsync`/`UpdateUserCoreAsync` additionally catch `DbUpdateException` where `ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }` and translate it into a friendly `InvalidOperationException("Username already exists.")`. This is the correct pattern — the app-level `AnyAsync(u => u.Username == normalizedUsername)` pre-check is only an optimization for the common case; the real race-condition guard is the unique index plus the exception handler. **No concurrency gap here** — flagged as a positive finding, not a defect, since the task specifically asked to verify this.

### 10. Soft-delete filter has no supporting index

`BookWheelDbContext.OnModelCreating`: `entity.HasQueryFilter(b => b.DeletedAtUtc == null)` on `BookEntity` — applied globally to every non-`IgnoreQueryFilters()` query, which is most of them (`GetAllAsync`, `UpdateAsync`, `SelectRandomAsync`, the stats repository's `activeBooks` query, etc.). Live indexes on `books` are `PK_books(Id)`, `IX_books_BookTypeId`, `IX_books_CreatedByUserId`, `IX_books_LastUpdatedByUserId`, `IX_books_UserId` — **none include `DeletedAtUtc`**, and there is no partial index (`WHERE "DeletedAtUtc" IS NULL`) either. Every "active books for this user" query (the single most common read pattern in the app — `GetAllAsync`, `SelectRandomAsync`, spin stats) uses `IX_books_UserId` to find the user's rows, then filters `DeletedAtUtc IS NULL` via a heap re-check on each candidate row. At 18 total rows this is unmeasurable; at meaningfully larger per-user book counts it becomes a real cost, especially since `SelectRandomAsync` (`BookWheel/Storage/Postgres/PostgresBookRepository.cs:73-84`) pulls **every** active row for the user into memory to pick one at random rather than doing the selection in SQL — a second reason a covering index matters here more than for a typical filtered read. **Recommendation:** replace `IX_books_UserId` with a composite `(UserId, DeletedAtUtc)` index (or add a partial index `ON books (UserId) WHERE "DeletedAtUtc" IS NULL`), which both accelerates the common query and would let it become index-only under the right column set.

### 11. Other indexing coverage — adequate

Checked every repository query pattern against live indexes:
- `PostgresCredentialRepository.ValidateCredentialsAsync` (login, hit on every request) filters on `Username` → covered by `IX_users_Username` (also unique, so this is an index-only equality lookup — good).
- `PostgresSpinHistoryRepository`/`PostgresSpinStatsRepository` filter `spin_selections` by `UserId` → covered by `IX_spin_selections_UserId`; the `LEFT JOIN` to `books` on `BookId` → covered by `PK_books`. `IX_spin_selections_BookId` exists but nothing in the repository layer currently queries `spin_selections` by `BookId` alone — it's unused today but cheap insurance and matches the FK-equivalent-column convention (index everything that stands in for a relationship), so not a finding, just an observation.
- `PostgresPasswordResetTokenRepository` (not shown above but implied by schema) would look up by `TokenHash` → covered by the unique `IX_password_reset_tokens_TokenHash`.
- `book_types.Name` unique index (`IX_book_types_Name`) is unused by any current repository query (lookups are by int `Id`, which is the PK) but is a reasonable defensive constraint (prevents "Physical" being seeded twice) rather than dead weight.

---

## Operational Findings

### 12. Runtime database role is a full PostgreSQL superuser — more severe than documented

`docker-compose.yml` creates the Postgres container with `POSTGRES_USER: bookwheel` / `POSTGRES_DB: bookwheel`, and the `bookwheel` app container's `ConnectionStrings__BookWheel` connects as that same `bookwheel` role. The official `postgres` image grants the `POSTGRES_USER`-named role full superuser rights by default (it's the bootstrap/initdb role), and this is exactly what the live database confirms:

```
current_user: bookwheel
rolsuper | rolcreatedb | rolcreaterole
   t     |      t      |      t
```

`SECURITY_AUDIT_REPORT.md` and `IMPROVEMENT_ROADMAP.md` (Priority 1, item 10) both currently describe this as "the app's connection needs DDL privileges, not just DML" — true, but understated. A `rolsuper` grant is not "DDL privileges scoped to the `bookwheel` schema"; it bypasses all permission checks entirely, can read/write/drop objects in any database on the cluster, alter roles, and disable row-level security anywhere. **Given the app runs `MigrateAsync()` automatically at startup using this exact connection, a compromised app process (e.g. via a future SQL-injection or deserialization bug, or a supply-chain compromise of a dependency) inherits full superuser control of the entire PostgreSQL instance, not just the `bookwheel` database.**

Context that somewhat mitigates severity today: app and database currently run on the same Docker Compose bridge network with no other tenants on the Postgres instance, so "superuser over the whole cluster" and "full control of the one database the app uses" are presently equivalent in practice — there's no other database or role on this instance for a compromised app to pivot to. That equivalence would disappear the moment this Postgres instance is shared (a managed multi-tenant Postgres service, a cluster also hosting other apps' databases, or any topology where the DB is not app-exclusive) — at that point the gap between "DDL on my schema" and "superuser on the cluster" becomes a real lateral-movement path. **Recommendation:** create two roles — a migration-only role with `CREATEDB`/schema-owner-equivalent DDL rights on just the `bookwheel` database, used solely for the `MigrateAsync()` step or an out-of-band `dotnet ef database update`, and a DML-only runtime role (`GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO bookwheel_app`) for normal request handling — matching the `SECURITY_AUDIT_REPORT.md` recommendation, but the write-up should be corrected to say the current grant is superuser, not merely DDL-capable, since that changes the priority calculus (this is closer to "Medium/High" than "Low" once framed accurately).

### 13. Connection string: no explicit `SSL Mode` — confirmed, security-audit finding is accurate and still open

`docker-compose.yml:33`: `ConnectionStrings__BookWheel: "Host=postgres;Database=bookwheel;Username=bookwheel;Password=..."` — no `SSL Mode` parameter. `appsettings.json`'s template is empty (`"BookWheel": ""`), so it inherits nothing more specific. This matches `SECURITY_AUDIT_REPORT.md`'s existing Low/open finding almost exactly. From a DB-operations angle: in the current topology (app and Postgres on the same single-host Docker Compose bridge network, `postgres:16-alpine` with no TLS listener configured at all — there's no certificate material mounted into the Postgres container in `docker-compose.yml`), Npgsql's `Prefer` default connects in plaintext because the server doesn't offer TLS to negotiate, and that plaintext traffic never leaves the Docker bridge network's virtual interface, so the practical exposure today is low — it would require another process on the same Docker host or a compromised sibling container to observe it. This is a materially different risk profile than a real multi-host production topology (e.g. app and DB on separate VMs/nodes, or a managed Postgres reached over the public internet or a shared VPC), where the same `Prefer`-default connection string would silently send credentials and query data (including usernames, though not password hashes — those never appear in cleartext SQL text, but `Username` values do) across a network segment an attacker might actually be positioned on, with the connection succeeding either way and no operator signal that encryption never happened. **Recommendation stands as documented:** set `SSL Mode=Require` (minimum) or `VerifyFull` explicitly before any deployment topology changes from "app and DB co-located on one trusted host," and treat "co-located Compose deployment" as the only scenario where the current default is acceptable, not the general case.

### 14. Backup/restore guidance — adequate for the deployment size, silent on point-in-time recovery

`README.md` "Data Storage" (line 309 onward) documents `pg_dump`/`pg_restore` (or provider snapshot tooling) as the backup mechanism, plus separate file-based backup guidance for `App_Data/logs/` and `DataProtection-Keys/`, and a restore procedure (stop app → restore DB from `pg_dump` → replace file directories → restart). This is reasonable, correct, self-consistent guidance for a self-hosted single-instance deployment, and it correctly separates "what's in Postgres" from "what's still file-based" — a distinction that matters for anyone following it.

What it does not cover, and does not claim to: **point-in-time recovery (PITR).** `pg_dump` is a logical, point-in-time-of-the-dump snapshot only — it cannot recover to an arbitrary moment between backups (e.g. "restore to 5 minutes before an accidental bulk delete"), and `docker-compose.yml`'s Postgres service has no `wal_level`/archiving configuration that would make PITR possible even if desired (the default Alpine image ships with WAL archiving disabled). For BookWheel's apparent scale and self-hosted/single-operator target audience, periodic `pg_dump` is a defensible default, but the README should say explicitly that it provides periodic-snapshot recovery only, not PITR, so an operator relying on it understands the actual recovery-point objective (data since the last dump is unrecoverable) rather than assuming otherwise. This is a documentation-completeness gap, not a functional defect.

### 15. Package version pinning — coherent, thoughtful (positive observation)

`BookWheel.csproj` pins `Npgsql.EntityFrameworkCore.PostgreSQL` to `9.0.4` and `Microsoft.EntityFrameworkCore`/`.Design`/`.Relational` to `9.0.19`, with an inline comment explaining that `Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4` transitively pins EF Core to `9.0.1`, while `Microsoft.EntityFrameworkCore.Design 9.0.19` pulls `9.0.19` — an unresolved mismatch would produce a `CS1705` assembly-version-mismatch build error. Verified this reasoning is internally consistent (the comment correctly identifies *why* three otherwise-implicit transitive packages need explicit, matching pins) and confirmed live: `__EFMigrationsHistory.ProductVersion` shows `9.0.19` for every applied migration, so the pin is doing its job in practice, not just in theory. This is thoughtful, well-documented dependency management — flagged here as a positive observation per the review scope, not a finding requiring action.

---

## Legacy Migration Tooling

### 16. `DataMigrationService` / `PostgresMigrationService` — still reachable, mostly vestigial, safe as-is

`DataMigrationService` (JSON-schema normalization) and `PostgresMigrationService` (one-time JSON→Postgres import) are both still registered as singletons in `Program.cs` and reachable via `--migrate-data` / `--migrate-to-postgres` CLI flags and the `/api/system/migrations/status` / `/api/system/migrations/run` admin endpoints (per `README.md`).

- **`PostgresMigrationService.RunAsync()`** (`BookWheel/Services/PostgresMigrationService.cs`) is correctly idempotent and transactional: it wraps every Add across `Users`/`Books`/`PasswordResetTokens` in one `Database.BeginTransactionAsync()`/`CommitAsync()`, and guards re-runs with `if (await context.Users.AnyAsync()) throw ...` **before** any writes — so a second invocation is a clean no-op error, not a partial or duplicate import, exactly as `README.md` claims ("a second run is a no-op error, not a duplicate-data hazard" — verified true by reading the code, not just trusting the doc). This is legacy (relevant only to operators upgrading from a pre-Postgres version of BookWheel), but it is dormant, self-guarding, and low-risk to leave in place — no action needed beyond eventually removing it once no realistic upgrade path from file-based storage remains.
- **`DataMigrationService.RunAsync()`** (JSON-schema normalization of `App_Data/user.cred` / `App_Data/books.json`) is the more interesting case: it runs **unconditionally on every single app startup** (`Program.cs:189`, outside the `--migrate-data`/`--migrate-to-postgres` flag branches), not just as an opt-in tool. Traced `JsonCredentialRepository.HasLegacyPayloadAsync`/`MigrateLegacyPayloadAsync` — both are safely no-ops when the legacy file doesn't exist or is already in the current schema shape (`IsCurrentCredentialDocument(json)` short-circuits), so this doesn't corrupt anything and the per-startup cost is one conditional file read. But given that `IBookRepository`/`ICredentialRepository` are wired to the Postgres implementations exclusively (`Program.cs:51-64`) and `JsonBookRepository`/`JsonCredentialRepository` are otherwise unused at runtime, this is dead-weight startup work for any deployment that has already completed its one-time migration (which, per the README's own instructions, is expected to be every deployment past its first upgrade). **Recommendation:** this is a reasonable candidate for removal, or at minimum for being gated behind an explicit opt-in flag/config value rather than running unconditionally forever — it's legacy scaffolding for a one-time transition that has presumably already happened for this project's only real deployment.

---

## Positive Observations

- Migration history is clean, sequentially named (`yyyyMMddHHmmss_PascalCaseName`, verified against all 7 migrations), and — with the one noted caveat about `defaultValueSql: "now()"` triggering a table rewrite — safe to roll forward without a backward-compatibility window.
- Live schema has zero drift from the EF Core model snapshot across all 6 tables, verified column-by-column.
- `username` uniqueness is enforced correctly at both layers (DB unique index + app-level `DbUpdateException`/`PostgresErrorCodes.UniqueViolation` handling) — the concurrent-signup race this pattern usually leaves open is actually closed.
- `citext` used correctly and deliberately for case-insensitive usernames, with the extension explicitly provisioned rather than assumed.
- `PostgresMigrationService`'s one-time import is properly transactional and idempotent — the correct pattern exists in the codebase, it's just not applied consistently (see Finding 7).
- Dependency pinning in `BookWheel.csproj` is well-reasoned and verified consistent with the live `ProductVersion` in `__EFMigrationsHistory`.
- Backup/restore guidance in `README.md` correctly distinguishes Postgres-backed data from remaining file-based state (logs, Data Protection keys) rather than treating storage as monolithic.

## Recommendations (Priority Order)

1. **High** — Split the runtime Postgres role from a superuser grant to least-privilege DML, with a separate migration-time-only elevated role for `MigrateAsync()`/`dotnet ef database update`. Update `SECURITY_AUDIT_REPORT.md`/`IMPROVEMENT_ROADMAP.md` item 10 to reflect that the current grant is full superuser, not just DDL-capable — this changes its true severity.
2. **Medium** — Wrap `UsersController.DeleteUser`'s three-step delete/purge/purge sequence in a single database transaction (or one repository method that does so internally) to eliminate the orphaned-data-on-crash window.
3. **Medium** — Pin `SSL Mode=Require` (or `VerifyFull`) on the production connection string before any deployment topology change moves the app and Postgres off the same trusted host/network.
4. **Low** — Add a composite `(UserId, DeletedAtUtc)` index (or a partial index `WHERE "DeletedAtUtc" IS NULL`) on `books` to keep the soft-delete-filtered "active books for user" query — the app's single most common read — cheap as the table grows.
5. **Low** — Document in `README.md`'s Data Storage section that the current backup guidance provides periodic-snapshot recovery only, not point-in-time recovery, so operators size their backup cadence to their actual acceptable data-loss window.
6. **Low** — Gate `DataMigrationService`'s unconditional per-startup legacy-JSON check behind an explicit flag, or remove it/`PostgresMigrationService` once no realistic upgrade-from-JSON path remains for this deployment.
7. **Informational** — Add a one-line comment in `BookWheelDbContext.OnModelCreating` documenting the "no FK constraints, indexed bare Guid columns only" pattern as deliberate, so a future contributor doesn't add `HasForeignKey` inconsistently to a single entity.

## Limitations

- No load/scale testing was performed; the indexing assessment (Finding 10) is inferred from live query plans' *absence of a matching index*, not from `EXPLAIN ANALYZE` under production-representative row counts (live tables are tiny — 18 `books` rows, 4 `users`, 0 `spin_selections`/`password_reset_tokens` — so no query-plan behavior at scale could be observed directly).
- The concurrent-migration race in Finding 6 is a reasoned analysis of EF Core/Npgsql's documented locking behavior (no cross-process advisory lock, transactional-DDL rollback as the safety net), not an empirically reproduced race — BookWheel's current single-replica Compose topology doesn't exercise it, and no container restarts were performed to test it live, per the task's constraints.
- No penetration testing, network traffic capture, or infrastructure/firewall review was performed for the connection-security finding (Finding 13) — consistent with how `SECURITY_AUDIT_REPORT.md` itself scopes that same finding.
- Password hashes, token hashes, and other sensitive column contents were not inspected; only schema (`\d+`), index (`\di`), and row-count (`SELECT count(*)`) queries were run against the live database, per the read-only constraint.
- The review covered the six tables the task specified; it did not enumerate every Postgres-level object (sequences, extensions beyond `citext`, roles other than `bookwheel`) exhaustively beyond what was needed to answer the privilege-model question in Finding 12.
