# Self-Hosting Guide

Deploying, configuring, operating, and upgrading a Book Wheel instance. For
what the app does, see the [User Guide](user-guide.md); for first-run and
account administration, see the [Admin Guide](admin-guide.md).

## Prerequisites

- .NET SDK 10.0+
- PowerShell or terminal capable of running `dotnet` CLI commands
- Docker Desktop (or Docker Engine) for containerized runs
- PostgreSQL 16+ (or use the bundled `docker-compose.yml` service) — required at startup; set `ConnectionStrings:BookWheel` or the `ConnectionStrings__BookWheel` environment variable

## Getting Started

From the solution root:

```bash
dotnet restore BookWheel.slnx
dotnet build BookWheel.slnx
```

## Environment Variable Reference

Every setting below can be set via `appsettings.json` (the `Section:Key` form), the matching
ASP.NET Core environment variable (`Section__Key`, double underscore), or — for the ones
`docker-compose.yml` reads directly from your `.env` file — the plain name in that last column.

| Config key | Environment variable | `.env` (docker-compose) | Default | Purpose |
|---|---|---|---|---|
| `ConnectionStrings:BookWheel` | `ConnectionStrings__BookWheel` | — | *(required)* | Postgres connection string used for all runtime request handling |
| `ConnectionStrings:BookWheelMigrations` | `ConnectionStrings__BookWheelMigrations` | — | falls back to `ConnectionStrings:BookWheel` | Connection string used only for the one-time startup migration, when using the least-privilege roles split |
| `App:BaseUrl` | `App__BaseUrl` | `APP_BASE_URL` | *(empty — disables self-service reset emails)* | Public base URL used to build password-reset/setup links; never derived from the request `Host` header |
| `AllowedHosts` | `AllowedHosts` | `ALLOWED_HOSTS` | `*` | Semicolon-separated allowed `Host` header values; restrict for public deployments |
| `Smtp:Host` | `Smtp__Host` | `SMTP_HOST` | *(empty — disables outbound email)* | SMTP server hostname |
| `Smtp:Port` | `Smtp__Port` | `SMTP_PORT` | `587` | SMTP server port |
| `Smtp:Username` | `Smtp__Username` | `SMTP_USERNAME` | *(empty)* | SMTP auth username; leave blank with password to connect unauthenticated |
| `Smtp:Password` | `Smtp__Password` | `SMTP_PASSWORD` | *(empty)* | SMTP auth password |
| `Smtp:EnableSsl` | `Smtp__EnableSsl` | — | `true` | `true` uses STARTTLS; `false` connects unencrypted |
| `Smtp:FromAddress` | `Smtp__FromAddress` | `SMTP_FROM_ADDRESS` | *(empty)* | Sender address on outgoing emails |
| `Smtp:FromName` | `Smtp__FromName` | — | `Book Wheel` | Sender display name on outgoing emails |
| `BookMetadata:TitleSearchResultLimit` | `BookMetadata__TitleSearchResultLimit` | — | `10` | Max results returned from a title search lookup |
| `BookMetadata:GoogleBooks:ApiKey` | `BookMetadata__GoogleBooks__ApiKey` | `GOOGLE_BOOKS_API_KEY` | *(empty — uses the public keyless endpoint)* | Google Books API key for a higher request quota |
| `Analytics:GoogleAnalyticsId` | `Analytics__GoogleAnalyticsId` | `GOOGLE_ANALYTICS_ID` | *(empty — analytics disabled)* | GA4 property ID; leave unset to ship with no telemetry |
| `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks` | `FORWARDED_KNOWN_NETWORKS` | *(empty; loopback always trusted)* | Comma-separated CIDR ranges of trusted reverse proxies |
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies` | — | *(empty; loopback always trusted)* | Individual trusted reverse-proxy IPs |
| `Security:AlwaysSecureCookie` | `Security__AlwaysSecureCookie` | — | `false` | Force the `Secure` cookie flag when the proxy can't forward `X-Forwarded-Proto` |
| `Security:ContentSecurityPolicyReportOnly` | `Security__ContentSecurityPolicyReportOnly` | — | `true` | Set `false` to enforce the CSP instead of only reporting violations |
| `Security:MetadataLookupRateLimitPerMinute` | `Security__MetadataLookupRateLimitPerMinute` | — | `30` | Per-user-session rate limit for book metadata lookups |
| `Security:IdleTimeoutHours` | `Security__IdleTimeoutHours` | — | `8` | Sliding idle-session timeout |
| `Security:AbsoluteSessionLifetimeHours` | `Security__AbsoluteSessionLifetimeHours` | — | `24` | Absolute session cap, regardless of activity |
| `Security:UsernameLockoutThreshold` | `Security__UsernameLockoutThreshold` | — | `5` | Failed logins before a username is locked out |
| `Security:UsernameLockoutMinutes` | `Security__UsernameLockoutMinutes` | — | `3` | Lockout duration after the threshold is hit |
| `Security:LogRetentionDays` | `Security__LogRetentionDays` | — | `14` | Days of JSONL audit logs to retain |
| `Security:LogMaxFileSizeMb` | `Security__LogMaxFileSizeMb` | — | `5` | Size-based rotation threshold for JSONL audit logs |
| `DataProtection:KeyDirectory` | `DataProtection__KeyDirectory` | — | `BookWheel/App_Data/DataProtection-Keys` in production | Explicit Data Protection key storage path |
| `Observability:EnableRequestCorrelationLogging` | `Observability__EnableRequestCorrelationLogging` | — | `true` | Toggle request-correlation log fields |
| `Observability:EnableStartupDiagnostics` | `Observability__EnableStartupDiagnostics` | — | `true` | Toggle startup writable-path diagnostics |
| `Observability:LogShipping:Enabled` | `Observability__LogShipping__Enabled` | — | `false` | Enable centralized log shipping |
| `Observability:LogShipping:EndpointUrl` | `Observability__LogShipping__EndpointUrl` | — | *(empty)* | Log-shipping sink URL |
| `Observability:LogShipping:ApiKey` | `Observability__LogShipping__ApiKey` | — | *(empty)* | Log-shipping sink API key |
| `Observability:LogShipping:BatchSize` | `Observability__LogShipping__BatchSize` | — | `100` | Log-shipping batch size |
| `Observability:LogShipping:PollIntervalSeconds` | `Observability__LogShipping__PollIntervalSeconds` | — | `30` | Log-shipping poll interval |
| — | — | `POSTGRES_PASSWORD` | dev default | Bootstrap Postgres superuser password (bundled `docker-compose.yml` only) |
| — | — | `POSTGRES_MIGRATOR_PASSWORD` | dev default | `bookwheel_migrator` role password (schema owner) |
| — | — | `POSTGRES_APP_PASSWORD` | dev default | `bookwheel_app` role password (runtime CRUD only) |
| — | — | `POSTGRES_BACKUP_PASSWORD` | dev default | `bookwheel_backup` role password (read-only, used by the backup sidecar) |
| — | — | `BACKUP_SCHEDULE` | `@daily` | Cron expression (or `@daily`/`@hourly`/etc.) for the backup sidecar |
| — | — | `BACKUP_KEEP_DAYS` | `7` | Daily backups to retain |
| — | — | `BACKUP_KEEP_WEEKS` | `4` | Weekly backups to retain |
| — | — | `BACKUP_KEEP_MONTHS` | `6` | Monthly backups to retain |

## Retailer Links

Book Search's "find this book online" links (`BookWheel/Models/RetailerLinksOptions.cs`) are configured under `RetailerLinks:Retailers`, an array rather than a flat set of keys, so there's no single `Section__Key` env var form for it — override entries by index instead (see below).

Each entry has:

| Field | Purpose |
|---|---|
| `Name` | Display name. Entries sharing the same `Name` are locale variants of one retailer "slot" — see below. |
| `IsbnUrlTemplate` | Optional. Used when an ISBN is known; `{isbn}` is replaced with it. |
| `TextUrlTemplate` | Used otherwise; `{query}` is replaced with the ISBN (if known but no `IsbnUrlTemplate` is set) or the title/author text. |
| `Enabled` | Set `false` to hide this entry entirely. |
| `AffiliateTag` | Empty by default — no monetization is configured out of the box. Set alongside `AffiliateParam` to opt in. |
| `AffiliateParam` | The query-string parameter name the tag is appended under (e.g. `tag` for Amazon Associates). |
| `Locales` | Language codes (`en`/`es`/`pl`) this entry applies to. Empty means "default" — used when no locale-specific entry sharing this `Name` matches the signed-in user's language. |

The shipped defaults: Google Books, Open Library, and Goodreads (shown for every locale); Amazon, with regional domains for `en` (`.com`, the default), `es`, and `pl`; Barnes & Noble for `en`; and Bookshop.org for `es`/`pl`. Setting `AffiliateTag`+`AffiliateParam` on an entry makes its link open with `rel="sponsored noopener"` and shows a disclosure note under the links in the UI — neither happens unless both are set.

Configuration binds array entries by index, so an env var or a higher-priority `appsettings.*.json` targets one entry at a time rather than replacing the whole list — for example, `RetailerLinks__Retailers__6__Enabled=false` disables just the 7th entry (Barnes & Noble in the shipped default order). To ship a fully custom retailer list, edit the array in `BookWheel/appsettings.json` directly.

## Version Stamping (CI/CD and Docker)

`BookWheel/BookWheel.csproj`'s `InformationalVersion` is the single source of truth for the app version. The footer and `/api/version` read it from the built assembly's `AssemblyInformationalVersion` attribute at runtime; everything else derives from or is validated against this value:

- CI builds (`.github/workflows/ci.yml`): read the csproj's `InformationalVersion`, strip any suffix, and append `-ci.<run>+<sha>` via `/p:InformationalVersion=...`
- Docker builds (`Dockerfile`): accept an optional `ARG APP_VERSION`; when unset, the build falls through to the csproj default rather than a second hardcoded value
- Release builds (`.github/workflows/docker-release.yml`): derive the version from the GitHub Release tag, but the workflow **fails** if that version doesn't match the csproj's `InformationalVersion`, so a release can't ship without bumping the csproj first

Examples:

```bash
dotnet build BookWheel.slnx /p:InformationalVersion=2.13.0
docker build --build-arg APP_VERSION=2.13.0 -t jasonkryst/bookwheel:2.13.0 .
```

## Automated Docker Publish on Version Release

GitHub Actions publishes Docker images to Docker Hub and GHCR when a GitHub Release is published (for example, tagged `v2.13.0`).

Workflow file:

- `.github/workflows/docker-release.yml`

Published tags:

- `jasonkryst/bookwheel:<version-without-v>` (for example `2.13.0`)
- `jasonkryst/bookwheel:latest` (only for non-prerelease versions)
- `ghcr.io/jasonkryst/bookwheel:<version-without-v>` (for example `2.13.0`)
- `ghcr.io/jasonkryst/bookwheel:latest` (only for non-prerelease versions)

Required repository secrets:

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN` (Docker Hub access token)

Notes:

- GHCR publish uses the built-in `GITHUB_TOKEN` and does not require extra secrets.
- The workflow grants `packages: write` permission to allow GHCR pushes.

## Running the Application

Option 1 (from solution root):

```bash
dotnet run --project BookWheel/BookWheel.csproj
```

Option 2 (from app project folder):

```bash
cd BookWheel
dotnet run
```

By default, the app serves static files and API endpoints from the same host.

## Container Support

This repository includes:

- `Dockerfile` for building and running the app container
- `Dockerfile.postgres` for the bundled PostgreSQL service and its role initializer
- `.dockerignore` for leaner and safer build contexts
- `docker-compose.yml` for local container orchestration with persistent volumes
- Non-root container runtime with writable app data and Data Protection key paths

### Build and Run with Docker

From the solution root:

```bash
docker build -t bookwheel:latest .
docker run --rm -p 32700:8080 --name bookwheel bookwheel:latest
```

Open `http://localhost:32700`.

### Run with Docker Compose

From the solution root:

```bash
docker-compose up --build
```

To run detached:

```bash
docker-compose up -d --build
```

The Compose build context must include the repository files. This is especially important for
Portainer: deploy from the repository's Git source rather than pasting only the Compose file
into the Web Editor, so it can build both the app and bundled PostgreSQL images.

To stop and remove containers:

```bash
docker-compose down
```

The compose setup persists:

- App data (`/app/App_Data`) including logs
- ASP.NET Core Data Protection keys (`/home/app/.aspnet/DataProtection-Keys`)
- PostgreSQL data (`bookwheel_pg_data` volume)

Note:

- The container listens on HTTP port `8080` internally.
- For production, terminate TLS at a reverse proxy or load balancer in front of the container.

If you previously created Docker volumes before the runtime permission fix, recreate them once:

```bash
docker compose down -v
docker compose up --build -d
```

### Upgrading Without Losing Data

When you pull a newer image, Docker replaces the container filesystem from the new image. That is expected, and it means you should not rely on the image's `/app` directory for persistent data.

Book Wheel persists the important mutable paths through Docker volumes:

- `/app/App_Data` for logs
- `/home/app/.aspnet/DataProtection-Keys` for Data Protection keys
- `bookwheel_pg_data` for PostgreSQL data (books, credentials, and password reset tokens)

Do not run `docker compose down -v` unless data loss is intended — it deletes the `bookwheel_pg_data` volume along with the others.

To upgrade safely:

```bash
docker compose pull
docker compose up -d
```

Important:

- Do not use `docker compose down -v` unless you intentionally want to delete persisted volumes.
- Do not mount the entire `/app` directory as a volume, because that can hide the application files shipped in the image.
- Store any user-generated or persistent content under the existing mounted data paths, not elsewhere under `/app`.

## Reverse proxy and forwarded headers

By default BookWheel trusts `X-Forwarded-For` / `X-Forwarded-Proto` only from loopback (`127.0.0.1`, `::1`). This is the secure default: it stops an arbitrary client from spoofing its IP to dodge the per-IP login and password-reset rate limiters.

If you terminate TLS at a reverse proxy that is **not** on loopback (typically another container on a Docker network such as `172.x.x.x`), you must tell BookWheel to trust it, otherwise:

- every client appears to come from the proxy's IP, so the per-IP rate limiters collapse into one shared bucket and audit logs record the proxy address;
- `X-Forwarded-Proto: https` is ignored, so the app thinks the request is plain HTTP and the auth cookie is issued **without** the `Secure` flag.

Trust is opt-in and additive (loopback stays trusted). **Trust the narrowest scope that reaches your proxy — anything inside a trusted range can forge `X-Forwarded-For`, so an over-broad range re-opens the rate-limit bypass this setting exists to prevent.** In order of preference:

1. **Pin the proxy's IP** (best). Give your proxy container a static address and trust only that:

   ```yaml
   environment:
     ForwardedHeaders__KnownProxies__0: "172.18.0.5"   # the proxy's exact IP
   ```

2. **Trust the proxy's actual subnet** — the specific network your proxy and BookWheel share, not the whole private block. Find it with `docker network inspect <network> --format '{{(index .IPAM.Config 0).Subnet}}'` (typically a /16 or /24 such as `172.18.0.0/16`):

   ```yaml
   environment:
     FORWARDED_KNOWN_NETWORKS: "172.18.0.0/16"          # comma-separated CIDRs
     # or: ForwardedHeaders__KnownNetworks__0: "172.18.0.0/16"
   ```

> ⚠️ Do **not** blanket-trust `172.16.0.0/12` (the entire Docker private range). On a host that runs anything besides this stack, any other container in that range could then spoof client IPs and bypass the rate limiters. Only use the whole block on a single-tenant host you fully control, and prefer options 1–2 everywhere else.

The same keys work in `appsettings.json` under `"ForwardedHeaders": { "KnownNetworks": [...], "KnownProxies": [...] }`. Invalid entries are ignored (not fatal). On startup a warning is logged outside Development when nothing is configured, and an information line with the counts when something is.

Setups by proxy — put the proxy and BookWheel on one dedicated Docker network and trust that network's exact subnet (option 2) or the proxy's pinned IP (option 1):

- **Caddy**: `reverse_proxy bookwheel:8080` forwards `X-Forwarded-For` and `X-Forwarded-Proto` automatically. Trust the shared network's subnet or pin Caddy's IP.
- **Traefik**: forwarded headers are added by default for entrypoints you expose. Trust Traefik's network subnet, e.g. `ForwardedHeaders__KnownNetworks__0=172.18.0.0/16`. Do not enable `forwardedHeaders.insecure` on a public entrypoint.
- **Nginx Proxy Manager**: it sets `X-Forwarded-For` / `X-Forwarded-Proto` for proxy hosts. Pin the NPM container's IP with `ForwardedHeaders__KnownProxies__0`, or trust its network's subnet.
- **Cloudflare Tunnel (`cloudflared`)**: the tunnel connects from the `cloudflared` container to `http://bookwheel:8080`, so the source is that container's Docker IP. Pin it, or trust the shared network's subnet. Cloudflare sends the visitor address in `X-Forwarded-For` and `CF-Connecting-IP`; BookWheel reads `X-Forwarded-For`.

If your proxy cannot forward `X-Forwarded-Proto`, set `Security:AlwaysSecureCookie` to `true` (env `Security__AlwaysSecureCookie=true`) to always mark the auth cookie `Secure`. Only do this when users reach the app over HTTPS, since browsers will not send a `Secure` cookie over plain HTTP.

### Allowed hosts

BookWheel uses ASP.NET Core host filtering, controlled by the `AllowedHosts` configuration key. The default is `*` (accept any `Host` header), which is fine for local use. For a public deployment, restrict it to the hostname(s) this instance serves so the app rejects requests with a forged `Host` header:

- `docker-compose.yml` reads it from `ALLOWED_HOSTS` in your `.env`: `ALLOWED_HOSTS=books.example.com`.
- Or set the container env var `AllowedHosts` directly (same name as the config key).
- Semicolon-separate multiple hosts, e.g. `ALLOWED_HOSTS=books.example.com;www.books.example.com`.
- If you add an in-container health probe that hits the app directly, include `localhost`: `ALLOWED_HOSTS=books.example.com;localhost`.

This is defense in depth. The main host-header risk — password-reset links built from the request `Host` — is already closed separately: those links come only from `App:BaseUrl`, never the incoming `Host` header.

## Data Storage

Book, credential, and password-reset-token data is stored in PostgreSQL, configured via the `ConnectionStrings:BookWheel` setting (or the `ConnectionStrings__BookWheel` environment variable in containerized deployments). EF Core migrations run automatically at startup.

`docker-compose.yml`'s bundled `postgres` service uses local-dev-only default passwords (`bookwheel`, `bookwheel_migrator`, `bookwheel_app` — see "Least-privilege database roles" below). Override them for anything beyond local dev by creating a `.env` file next to `docker-compose.yml` with `POSTGRES_PASSWORD=<your-password>`, `POSTGRES_MIGRATOR_PASSWORD=<your-password>`, and `POSTGRES_APP_PASSWORD=<your-password>` before running `docker compose up`.

**Least-privilege database roles:** fresh deployments of the bundled `docker-compose.yml` automatically provision two non-superuser Postgres roles: `bookwheel_migrator` (schema owner, used only for the one-time startup migration via `ConnectionStrings:BookWheelMigrations`) and `bookwheel_app` (`SELECT`/`INSERT`/`UPDATE`/`DELETE` only, used for all runtime request handling via `ConnectionStrings:BookWheel`). The Postgres bootstrap role (`POSTGRES_USER=bookwheel`) is never used by the application itself once these are provisioned — Postgres requires the bootstrap role to always keep its superuser attribute, so rather than trying to strip it, the app simply doesn't connect as it.

This is provisioned by `scripts/postgres/init-least-privilege-roles.sh`, which Postgres only runs automatically against a **fresh, empty** data volume. If you're upgrading an existing deployment's volume:

```bash
docker exec -i bookwheel-postgres bash < scripts/postgres/init-least-privilege-roles.sh
```

Then transfer ownership of the existing tables to the new migrator role (the automatic script can't do this for you — `REASSIGN OWNED` can't be used here because the bootstrap role also owns the database itself):

```bash
docker exec bookwheel-postgres psql -U bookwheel -d bookwheel -c '
  ALTER TABLE "__EFMigrationsHistory" OWNER TO bookwheel_migrator;
  ALTER TABLE book_info_providers OWNER TO bookwheel_migrator;
  ALTER TABLE book_types OWNER TO bookwheel_migrator;
  ALTER TABLE books OWNER TO bookwheel_migrator;
  ALTER TABLE password_reset_tokens OWNER TO bookwheel_migrator;
  ALTER TABLE spin_selections OWNER TO bookwheel_migrator;
  ALTER TABLE users OWNER TO bookwheel_migrator;
'
```

Finally, point `ConnectionStrings__BookWheel` at the new `bookwheel_app` role and `ConnectionStrings__BookWheelMigrations` at the new `bookwheel_migrator` role, then restart the app. If `ConnectionStrings:BookWheelMigrations` is left unset, the app falls back to using `ConnectionStrings:BookWheel` for both migrations and runtime queries — today's original single-role behavior — so this upgrade is entirely opt-in.

Log data is stored in:

- `BookWheel/App_Data/logs/bookwheel-YYYY-MM-DD.jsonl`

Each line is a JSON object with structured fields such as timestamp, level, category, message, request id, path, client IP, and user agent. Log storage did not move to PostgreSQL — it remains file-based, same as before.

Backup and restore guidance:

- **Automated backups (docker-compose):** the `backup` service (`prodrigestivill/postgres-backup-local`) runs `pg_dump` against the `postgres` service on a schedule and writes gzipped SQL dumps (`*.sql.gz`) to `/backups` in the `bookwheel_pg_backups` named volume. By default it runs `@daily`, so `docker compose up` produces a dump within 24 hours. It connects as a dedicated **read-only** `bookwheel_backup` role (SELECT only — provisioned by `scripts/postgres/init-least-privilege-roles.sh`), not the bootstrap superuser. On an existing deployment whose data volume predates this role, run that script manually (and set `POSTGRES_BACKUP_PASSWORD`) before enabling the sidecar — same caveat as the `bookwheel_migrator`/`bookwheel_app` roles.
- Tune it from your `.env` file: `BACKUP_SCHEDULE` (cron expression or `@daily`/`@hourly`, default `@daily`), `BACKUP_KEEP_DAYS` (7), `BACKUP_KEEP_WEEKS` (4), `BACKUP_KEEP_MONTHS` (6). To disable it, comment out the `backup` service in `docker-compose.yml` (or set `BACKUP_SCHEDULE` to something rare like `@yearly`).
- List and copy dumps out of the volume - a backup on the same Docker host is not off-site, so copy them elsewhere regularly: `docker compose exec backup ls -l /backups/daily` and `docker cp bookwheel-backup:/backups/last/bookwheel-latest.sql.gz .`
- If you use managed Postgres or skip the sidecar, back up with `pg_dump`/`pg_restore` (or your provider's snapshot tooling) on the schedule your data warrants.
- Back up `BookWheel/App_Data/logs/` and `BookWheel/App_Data/DataProtection-Keys/` (or your configured `DataProtection:KeyDirectory`) separately - these remain file-based.
- **Restore drill** (run it once before you need it):
  1. Stop the app: `docker compose stop bookwheel`
  2. Make Postgres reachable from where you run the script (for example, temporarily add `ports: ["5432:5432"]` to the `postgres` service, or run the script from a container on the compose network).
  3. Restore: `PGPASSWORD=<postgres password> scripts/restore.sh ./bookwheel-latest.sql.gz --host localhost --port 5432 --db bookwheel --user bookwheel`. The script needs `psql` and `gunzip`, and refuses to run if the database already contains tables unless you add `--force` (sidecar dumps use `--clean --if-exists`, so `--force` replaces the existing objects).
  4. Replace the log/Data-Protection-key directories from their file backups if needed, then start the app: `docker compose start bookwheel`.
- CI takes a dump and restores it with `scripts/restore.sh` on every push (`backup-restore-test` job), so the restore path stays working.

Filesystem permission guidance for logs:

- Restrict `BookWheel/App_Data/logs/` so only the application runtime identity and trusted operators can read/write.
- Linux/macOS runtime hardening applies restrictive `rwxr-x---` permissions to the logs directory at startup when possible.
- On Windows, apply equivalent ACL restrictions manually (for example, remove broad Users read access and grant only service identity + operators).

Data Protection key storage:

- Production startup supports explicit key persistence via `DataProtection:KeyDirectory`.
- If not provided, production defaults to `BookWheel/App_Data/DataProtection-Keys`.
- In containerized deployments, continue mounting persistent key storage (`/home/app/.aspnet/DataProtection-Keys`) and set `DataProtection:KeyDirectory` accordingly.

## Observability and Operations

Request correlation guidance:

- Every request includes or is assigned `X-Correlation-ID` and the response echoes this header.
- Request lifecycle logs include method, path, status, and correlation id.
- For troubleshooting, capture correlation id from a failing response and search structured logs for matching request entries.

Metrics guidance:

- Use `GET /api/metrics` as an administrator to retrieve structured counters:
  - `loginFailureCount`
  - `loginLockoutCount`
  - `successfulLoginCount`
  - `spinCount`
  - `totalBookCount`

Centralized log shipping:

- Configure `Observability:LogShipping` in `BookWheel/appsettings.json`.
- Set `Enabled=true` and provide `EndpointUrl` (and optionally `ApiKey`) to push recent JSONL batches to a central sink.

Startup diagnostics:

- On startup, the app validates writable access for:
  - `App_Data`
  - `App_Data/logs`
  - `App_Data/corrupt`
- Failures are logged as critical diagnostics to surface volume/permission issues early.

## Release Checklist

1. Update the version stamp in `BookWheel/BookWheel.csproj` (`InformationalVersion`) to match the intended release tag (e.g. `2.13.0`) — the release workflow fails if the GitHub Release tag doesn't match it.
2. Run full tests: `dotnet test BookWheel.slnx`.
3. Run security-focused regression filter from CI workflow.
4. Run vulnerability scans (same gate CI's `dependency-audit` job uses — `dotnet list --vulnerable` alone always exits 0, so the script is what actually fails the build):
   - `scripts/check-vulnerable-packages.sh BookWheel/BookWheel.csproj BookWheel.Tests/BookWheel.Tests.csproj`
5. Verify CI's `trivy` job and `codeql` workflow both passed — check the GitHub Security → Code Scanning tab for any new findings before tagging.
6. Build container image with explicit tag and version build arg.
7. Start container and verify readiness endpoint (`/health/ready`) and basic login flow.
8. Verify persistent volumes for `/app/App_Data` and Data Protection keys.
9. Confirm observability configuration for request correlation and log shipping in production settings.

## Troubleshooting

- If `dotnet test` reports file lock warnings from `testhost`, re-run the command; this is usually transient.
- If authentication fails unexpectedly, verify that the database is reachable (`/health/ready`), that the `users` table contains at least one row, and that first-run setup was completed.
- If a reset link does not work, verify the link has not expired (24 hours) and was not already used.
- If the app starts but books/users are missing, verify PostgreSQL connectivity via `GET /health/ready` and check the `ConnectionStrings:BookWheel` value.
- If you need to reset to first-run setup state, delete all rows from the `users` table in PostgreSQL and restart the app; the setup screen will appear on next visit.
- If you need to inspect logs, open the current day file under `BookWheel/App_Data/logs/`.
- If the container starts but auth sessions break after restarts, verify Data Protection keys are persisted (compose handles this via `bookwheel_dp_keys`).
- If port `32700` is busy, change the host side mapping in `docker-compose.yml` (for example, `32701:8080`).
