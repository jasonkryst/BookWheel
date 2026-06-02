# Book Wheel Solution

Book Wheel is a .NET 8 web app for managing a list of books and spinning a wheel to pick a title at random.

This solution is split into separate application and test projects:

- `BookWheel/` contains the ASP.NET Core web application (API + static frontend)
- `BookWheel.Tests/` contains integration tests
- `BookWheel.slnx` ties both projects together

## Features

- First-run account creation plus cookie-based login/logout
- First created account is automatically assigned administrator role
- Administrator-only user management for creating, updating, and removing other user accounts
- New-user onboarding uses admin-shared setup links instead of admin-supplied passwords
- Administrator-generated password reset links (24-hour expiry) instead of direct password setting
- User management link is visible only to administrators
- Add, edit, and remove books
- Book collections are scoped per user account
- Interactive spin wheel UI
- Light/dark mode icon toggle with saved browser preference
- Theme toggle frontend test coverage
- Spin selection does not remove the selected book
- "Last selected" message displayed below the wheel
- Active books list with pagination after 10 books
- Book count plus page status summary in the books panel
- Delete confirmation modal for book removal
- Login form reset on logout so credentials are not left in the UI
- Wheel entropy shuffle when adding books
- Import/export icon button with JSON file upload/download modal tabs
- Persistent storage in `App_Data/books.json`
- Encrypted credential storage in `App_Data/user.cred`
- Structured audit logs for failed login and rate-limit events
- Persistent JSONL log files in `App_Data/logs/`
- Log retention and size-based rotation for JSONL audit files
- Proxy-aware request handling with forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`)
- Username-aware login lockout/backoff after repeated failed attempts
- Account disable, lock, and forced password-reset controls for administrators
- Health check endpoints for liveness and readiness
- Corruption quarantine and recovery messaging for credential/book storage files
- Structured operational metrics for login outcomes, spin count, and total book count
- Request correlation header propagation and scoped request lifecycle logging
- Optional centralized log shipping to an HTTP sink for production operations
- Startup diagnostics for writable storage and expected runtime directories
- First-time empty-state guidance when no books are present

## Solution Structure

```text
Book Wheel/
  BookWheel.slnx
  README.md
  BookWheel/
    BookWheel.csproj
    Program.cs
    appsettings.json
    App_Data/
    Controllers/
    Models/
    Services/
    wwwroot/
  BookWheel.Tests/
    BookWheel.Tests.csproj
    BookWheelApiTests.cs
    BookWheelWebAppFactory.cs
```

## Prerequisites

- .NET SDK 8.0+
- PowerShell or terminal capable of running `dotnet` CLI commands
- Docker Desktop (or Docker Engine) for containerized runs

## Getting Started

From the solution root:

```bash
dotnet restore BookWheel.slnx
dotnet build BookWheel.slnx
```

## Version Stamping (CI/CD and Docker)

The footer version is sourced from `AssemblyInformationalVersion`.

- Local default: `1.0.10-local` (set in `BookWheel/BookWheel.csproj`)
- CI builds: `.github/workflows/dotnet.yml` sets `APP_VERSION` and passes it via `/p:InformationalVersion=...`
- Docker builds: `Dockerfile` accepts `ARG APP_VERSION` and passes it to `dotnet publish`

Examples:

```bash
dotnet build BookWheel.slnx /p:InformationalVersion=1.0.10
docker build --build-arg APP_VERSION=1.0.10 -t jasonkryst/bookwheel:1.0.10 .
```

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

This repository now includes:

- `Dockerfile` for building and running the app container
- `.dockerignore` for leaner and safer build contexts
- `docker-compose.yml` for local container orchestration with persistent volumes
- Non-root container runtime with writable app data and Data Protection key paths

### Build and Run with Docker

From the solution root:

```bash
docker build -t bookwheel:latest .
docker run --rm -p 8080:8080 --name bookwheel bookwheel:latest
```

Open `http://localhost:8080`.

### Run with Docker Compose

From the solution root:

```bash
docker-compose up --build
```

To run detached:

```bash
docker-compose up -d --build
```

To stop and remove containers:

```bash
docker-compose down
```

The compose setup persists:

- App data (`/app/App_Data`) including books, credentials, and logs
- ASP.NET Core Data Protection keys (`/home/app/.aspnet/DataProtection-Keys`)

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

- `/app/App_Data` for books, credentials, and logs
- `/home/app/.aspnet/DataProtection-Keys` for Data Protection keys

To upgrade safely:

```bash
docker compose pull
docker compose up -d
```

Important:

- Do not use `docker compose down -v` unless you intentionally want to delete persisted volumes.
- Do not mount the entire `/app` directory as a volume, because that can hide the application files shipped in the image.
- Store any user-generated or persistent content under the existing mounted data paths, not elsewhere under `/app`.

## First-Run Account Setup

On the first visit, the login screen switches into account-creation mode if no credential file exists yet.

Flow:

1. Open the app.
2. If `BookWheel/App_Data/user.cred` does not exist, the UI prompts you to create the first account.
3. Submitting the form creates the first user account as an administrator and signs the user in.
4. Future visits use the normal login flow.

Credential storage details:

- Account records are stored in `BookWheel/App_Data/user.cred`
- Each record includes user id, username, password hash, admin flag, and created timestamp
- The record is encrypted at rest with ASP.NET Core Data Protection
- The password is hashed with `PasswordHasher<T>` before being written to disk
- The credential file is created only when the user explicitly submits the setup form

Administrator details:

- The first account is marked as `isAdmin = true`
- Only administrators can create, update, remove, or generate password reset links for other user accounts
- Non-admin users cannot access user-management endpoints
- The first account cannot be removed
- Removing an account also deletes books assigned to that account

Password reset link details:

- Administrators do not set or reset user passwords directly
- Administrators generate a secure reset link for a user account
- Reset links expire after 24 hours
- Reset links are one-time use and become invalid after successful password update
- Reset token records are stored as hashed values and encrypted at rest

Important:

- There is no default username/password in `appsettings.json`
- If you delete `BookWheel/App_Data/user.cred`, the app will prompt for first-run setup again

## Data Storage

Book data is stored in:

- `BookWheel/App_Data/books.json`

Books are grouped by user id in the JSON payload, so each account has an isolated collection.
The file is created automatically if it does not exist.

Credential data is stored in:

- `BookWheel/App_Data/user.cred`

The file is created only after account setup is completed.

Log data is stored in:

- `BookWheel/App_Data/logs/bookwheel-YYYY-MM-DD.jsonl`

Each line is a JSON object with structured fields such as timestamp, level, category, message, request id, path, client IP, and user agent.

Backup and restore guidance:

- Back up the full `BookWheel/App_Data/` directory (books, credentials, reset tokens, logs, and corrupt-file quarantine artifacts).
- Keep Data Protection keys backed up alongside app data for encrypted payload continuity.
- Restore by stopping the app, replacing `App_Data/` with the backup copy, and starting the app again.
- If corruption quarantine occurs, review `BookWheel/App_Data/corrupt/` and restore known-good files from backup.

Filesystem permission guidance for logs:

- Restrict `BookWheel/App_Data/logs/` so only the application runtime identity and trusted operators can read/write.
- Linux/macOS runtime hardening applies restrictive `rwxr-x---` permissions to the logs directory at startup when possible.
- On Windows, apply equivalent ACL restrictions manually (for example, remove broad Users read access and grant only service identity + operators).

Data Protection key storage:

- Production startup now supports explicit key persistence via `DataProtection:KeyDirectory`.
- If not provided, production defaults to `BookWheel/App_Data/DataProtection-Keys`.
- In containerized deployments, continue mounting persistent key storage (`/home/app/.aspnet/DataProtection-Keys`) and set `DataProtection:KeyDirectory` accordingly.

## Legacy Data Migration Utility

Book Wheel now supports an explicit migration utility for converting legacy payloads before normal runtime operations.

What it migrates:

- Legacy single-user credential payloads in `BookWheel/App_Data/user.cred` into the current `users` document structure
- Legacy flat-array `BookWheel/App_Data/books.json` payloads into the current user-id keyed object format

Runtime behavior:

- The app executes migration at startup so legacy formats are converted before endpoint use
- Startup logs include migration visibility fields (what migrated and affected counts)

One-shot command mode:

```bash
dotnet run --project BookWheel/BookWheel.csproj -- --migrate-data
```

This runs migration only, prints a JSON report to stdout, and exits.

API utility (admin when account exists):

- `GET /api/system/migrations/status`
- `POST /api/system/migrations/run`

If an account exists, these endpoints require an authenticated administrator.

## API Overview

Base route: `/api`

Auth endpoints:

- `GET /api/auth/status`
- `POST /api/auth/setup`
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `GET /health/live`
- `GET /health/ready`

Operational endpoint (admin only):

- `GET /api/metrics`

`GET /api/auth/status` returns whether first-run setup is required. `POST /api/auth/setup` creates the initial account when no credential file exists.

User-management endpoints (administrator only):

- `GET /api/users`
- `POST /api/users`
- `PUT /api/users/{id}`
- `DELETE /api/users/{id}`
- `POST /api/users/{id}/password-reset-link`

`POST /api/users` behavior:

- Request body accepts `username` and `isAdmin` only
- Administrators do not provide a password when creating a user
- Response includes `setupLink` and `setupLinkExpiresAtUtc` for secure account setup sharing

Password reset endpoint:

- `POST /api/auth/password-reset/validate`
- `POST /api/auth/password-reset/complete`

Migration utility endpoints:

- `GET /api/system/migrations/status`
- `POST /api/system/migrations/run`

Book endpoints (authentication required):

- `GET /api/books`
- `POST /api/books`
- `PUT /api/books/{id}`
- `DELETE /api/books/{id}`
- `POST /api/books/spin`

## Testing

Run all tests:

```bash
dotnet test BookWheel.slnx
```

Run only the test project:

```bash
dotnet test BookWheel.Tests/BookWheel.Tests.csproj
```

Current integration tests cover:

- First-run account setup
- Auth protection for books endpoint
- Login and access to protected endpoints after setup
- First-user administrator assignment and admin-only user-management access control
- Admin create/update/delete user flows
- Password reset link generation and one-time token completion flow
- Admin user removal flow with first-account protection
- User removal cascade cleanup for user-scoped books
- Book list isolation across different users
- Spin behavior preserving active book count
- Book update and remove flow
- Security regression checks for encrypted credential storage, failed-login audit logging, and rate limiting
- Proxy-aware rate-limit behavior using forwarded client IP headers
- Corrupt/missing data file handling with quarantine and recovery responses
- Health check behavior for writable and unhealthy storage scenarios
- Metrics endpoint behavior and access control
- Container and startup smoke checks for runtime paths and health probes
- Persistent log file creation and structured audit logging checks

Frontend-focused tests also verify that the HTML, JavaScript, and CSS expose the account setup mode, selected-book UI, pagination summary, delete confirmation flow, logout form reset behavior, icon-based theme toggle behavior, and file-based import/export behavior.

The frontend also includes import/export interactions (JSON tabbed modal) and wheel shuffle behavior when books are added.

## Project Documents

Additional project documentation is available in:

- `SECURITY_AUDIT_REPORT.md` for the latest audit summary, findings, and remediation priorities
- `IMPROVEMENT_ROADMAP.md` for a forward-looking roadmap covering security, UX, operations, and product enhancements

## Theme Toggle

The application includes a toolbar toggle for switching between dark and light modes.

- Theme choice is persisted in browser `localStorage` under `bookwheel-theme`.
- On first load, when no saved preference exists, the UI follows the system color preference (`prefers-color-scheme`).
- The toggle updates the root `data-theme` attribute so CSS variables can switch the entire palette.

## Import and Export (JSON)

Use the toolbar import/export icon button to open the transfer modal.

- Import tab accepts JSON in either `[{"title":"..."}]` form or `{ "books": [{"title":"..."}] }` form.
- Import merges into existing books and skips case-insensitive title matches.
- Import flow uses JSON file upload (`.json`).
- Export tab generates a JSON file download of the current book list.
- The download area is shown only when the Export tab is selected.

## Development Notes

- The test host uses a temporary content root so tests do not mutate real app data.
- The test host also mirrors `wwwroot` into a temporary folder so frontend behavior is exercised against real static assets.
- The test host captures structured logs so security audit events can be asserted in tests.
- The test host also verifies that log entries are written to persistent JSONL files in the temp `App_Data/logs` folder.
- CI runs build, full tests, vulnerability scans, security-focused regressions, smoke tests, and Docker startup verification.
- CI also runs secret scanning via gitleaks to prevent accidental token/credential commits.
- Frontend behavior is implemented in `BookWheel/wwwroot/js/app.js`.
- The wheel UI and styles are in `BookWheel/wwwroot/index.html` and `BookWheel/wwwroot/css/site.css`.

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

1. Update version stamp and build metadata (`InformationalVersion`/`APP_VERSION`).
2. Run full tests: `dotnet test BookWheel.slnx`.
3. Run security-focused regression filter from CI workflow.
4. Run vulnerability scans:
   - `dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive`
   - `dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive`
5. Build container image with explicit tag and version build arg.
6. Start container and verify readiness endpoint (`/health/ready`) and basic login flow.
7. Verify persistent volumes for `/app/App_Data` and Data Protection keys.
8. Confirm observability configuration for request correlation and log shipping in production settings.

## Troubleshooting

- If `dotnet test` reports file lock warnings from `testhost`, re-run the command; this is usually transient.
- If authentication fails unexpectedly, verify whether `BookWheel/App_Data/user.cred` exists and whether the first-run setup was completed.
- If a reset link does not work, verify the link has not expired (24 hours) and was not already used.
- If the app starts but books are missing, check `BookWheel/App_Data/books.json` permissions.
- If you need to reset the account, delete `BookWheel/App_Data/user.cred` and create a new account on next launch.
- If you need to inspect logs, open the current day file under `BookWheel/App_Data/logs/`.
- If the container starts but auth sessions break after restarts, verify Data Protection keys are persisted (compose handles this via `bookwheel_dp_keys`).
- If port `8080` is busy, change the host side mapping in `docker-compose.yml` (for example, `8081:8080`).
