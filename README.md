# Book Wheel Solution

[![CI](https://github.com/jasonkryst/BookWheel/actions/workflows/ci.yml/badge.svg)](https://github.com/jasonkryst/BookWheel/actions/workflows/ci.yml)
[![Copilot](https://github.com/jasonkryst/BookWheel/actions/workflows/agents/copilot-pull-request-reviewer/badge.svg)](https://github.com/jasonkryst/BookWheel/actions/workflows/agents/copilot-pull-request-reviewer)
[![Docker Release](https://github.com/jasonkryst/BookWheel/actions/workflows/docker-release.yml/badge.svg)](https://github.com/jasonkryst/BookWheel/actions/workflows/docker-release.yml)

Book Wheel is a .NET 8 web app for managing a list of books and spinning a wheel to pick a title at random.

This solution is split into separate application and test projects:

- `BookWheel/` contains the ASP.NET Core web application (API + static frontend)
- `BookWheel.Tests/` contains integration tests
- `BookWheel.slnx` ties both projects together

## Features

- First-run account creation plus cookie-based login/logout
- First created account is automatically assigned administrator role
- Administrator-only user management for creating, updating, and removing other user accounts, with searchable status filters and at-a-glance account-state indicators
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
- Settings menu (theme switcher + language selector) with saved browser preference and localized server error messages
- Installable Progressive Web App with an offline-capable app shell (manifest, service worker, offline fallback page)
- Mobile and tablet layouts keep the wheel and import/export dialog within the viewport without horizontal scrolling or browser zoom, including the Import tab's native file picker

## Internationalization

Book Wheel supports English, Spanish, and Polish.

- A gear-icon Settings button opens a dialog with the theme switcher and a language dropdown; the language choice persists in the browser's `localStorage`. It's positioned inline in the toolbar when logged in, and in the corner of the login/setup card when logged out.
- On first visit, the language defaults to the browser's language if it's one of the supported three, otherwise English.
- The frontend sends the selected language as an `Accept-Language` header on every API call, so server-generated error messages (e.g. "Book title is required.") come back already translated.
- Frontend strings live in `BookWheel/wwwroot/js/i18n.js`; backend error-message translations live in `BookWheel/Resources/SharedErrors*.resx`, looked up through `ApiMessageLocalizer`.

### Adding a new language

1. Add the locale code to `SUPPORTED_LOCALES` in `BookWheel/wwwroot/js/i18n.js` and add a full `TRANSLATIONS.<locale>` catalog (copy the `en` object as a starting point — every key must be present or that string falls back to English).
2. Add a `BookWheel/Resources/SharedErrors.<locale>.resx` file with all of the same resource keys as `SharedErrors.resx`.
3. Add the locale code to the `supportedCultures` array in `BookWheel/Program.cs`'s `RequestLocalizationOptions` setup.

Spanish and Polish translations were authored by the assistant as a first pass and have not been reviewed by native speakers — treat them as a solid starting point, not final copy.

## Progressive Web App

Book Wheel can be installed as a standalone app (desktop Chrome/Edge, Android, and — with reduced polish — iOS Safari) and its UI shell keeps working when the network drops.

- A web app manifest (`BookWheel/wwwroot/manifest.webmanifest`) drives the install prompt/icon. Icons live in `BookWheel/wwwroot/icons/` and are generated by `scripts/generate-pwa-icons.py` (Python standard library only, no image tooling required — re-run it if the wheel-slice color palette in `site.css` ever changes).
- A service worker (`BookWheel/wwwroot/sw.js`, served at `/sw.js` through `Program.cs` so its cache name automatically picks up the current app version) precaches the HTML/CSS/JS/icon app shell on install, so the UI keeps loading while offline.
- The service worker never intercepts `/api/*` requests — login, book data, and spin results always require a live connection. A toast notifies the user when the browser goes offline or comes back online.
- `BookWheel/wwwroot/offline.html` is a minimal fallback page shown only if a user's very first visit happens while offline (rare, since the shell is precached on install).
- **Not implemented:** full offline data support (queuing book edits made while offline and syncing them on reconnect). That needs a durable write queue, conflict resolution for concurrent edits, and offline-aware session handling — evaluated for #33 and scoped out as a separate future project (see `IMPROVEMENT_ROADMAP.md`).

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
    Storage/
    wwwroot/
  BookWheel.Tests/
    BookWheel.Tests.csproj
    BookWheelApiTests.cs
    BookWheelWebAppFactory.cs
    Storage/
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

`BookWheel/BookWheel.csproj`'s `InformationalVersion` is the single source of truth for the app version. The footer and `/api/version` read it from the built assembly's `AssemblyInformationalVersion` attribute at runtime; everything else derives from or is validated against this value:

- Local default: `1.9.6` (set in `BookWheel/BookWheel.csproj`)
- CI builds (`.github/workflows/ci.yml`): read the csproj's `InformationalVersion`, strip any suffix, and append `-ci.<run>+<sha>` via `/p:InformationalVersion=...`
- Docker builds (`Dockerfile`): accept an optional `ARG APP_VERSION`; when unset, the build falls through to the csproj default rather than a second hardcoded value
- Release builds (`.github/workflows/docker-release.yml`): derive the version from the GitHub Release tag, but the workflow **fails** if that version doesn't match the csproj's `InformationalVersion`, so a release can't ship without bumping the csproj first

Examples:

```bash
dotnet build BookWheel.slnx /p:InformationalVersion=1.9.6
docker build --build-arg APP_VERSION=1.9.6 -t jasonkryst/bookwheel:1.9.6 .
```

## Automated Docker Publish on Version Release

GitHub Actions publishes Docker images to Docker Hub and GHCR when a GitHub Release is published (for example, tagged `v1.9.6`).

Workflow file:

- `.github/workflows/docker-release.yml`

Published tags:

- `jasonkryst/bookwheel:<version-without-v>` (for example `1.9.6`)
- `jasonkryst/bookwheel:latest` (only for non-prerelease versions)
- `ghcr.io/jasonkryst/bookwheel:<version-without-v>` (for example `1.9.6`)
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
- CI dependency-audit gate (`scripts/check-vulnerable-packages.sh`): passes clean `dotnet list --vulnerable` output through unchanged and exits 0, and exits 1 when the report contains a vulnerable-packages finding
- PWA manifest, icon, and service-worker behavior, including that `/api/*` requests are never intercepted or cached by the service worker

Frontend-focused tests also verify that the HTML, JavaScript, and CSS expose the account setup mode, selected-book UI, pagination summary, delete confirmation flow, logout form reset behavior, icon-based dark/light/high-contrast theme toggle behavior, and file-based import/export behavior.

The frontend also includes import/export interactions (JSON tabbed modal) and wheel shuffle behavior when books are added.

## Project Documents

Additional project documentation is available in:

- `SECURITY_AUDIT_REPORT.md` for the latest audit summary, findings, and remediation priorities
- `IMPROVEMENT_ROADMAP.md` for a forward-looking roadmap covering security, UX, operations, and product enhancements

## Theme Toggle

The application includes a theme control (inside the Settings dialog, opened via the gear-icon button) that cycles through dark, light, and high-contrast modes.

- Theme choice is persisted in browser `localStorage` under `bookwheel-theme`.
- On first load, when no saved preference exists, the UI follows the system contrast preference (`prefers-contrast: more`) if set, otherwise the system color preference (`prefers-color-scheme`).
- The toggle updates the root `data-theme` attribute (`dark`, `light`, or `high-contrast`) so CSS variables can switch the entire palette, including the icon/label shown on the toggle button.
- The high-contrast theme uses a pure black/white/yellow palette, solid (non-blended) surfaces, thicker borders, and a colorblind-distinguishable wheel-slice palette so every theme meets accessibility contrast expectations.

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
- CI also runs secret scanning via gitleaks and workflow linting via actionlint to prevent accidental token/credential commits and malformed workflow changes.
- CI enforces a per-ref concurrency group (newer pushes cancel in-progress runs for the same branch/PR) and per-job timeouts, and Docker layer builds are cached via GitHub Actions cache (`type=gha`).
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

1. Update the version stamp in `BookWheel/BookWheel.csproj` (`InformationalVersion`) — the release workflow fails if the GitHub Release tag doesn't match it.
2. Run full tests: `dotnet test BookWheel.slnx`.
3. Run security-focused regression filter from CI workflow.
4. Run vulnerability scans (same gate CI's `dependency-audit` job uses — `dotnet list --vulnerable` alone always exits 0, so the script is what actually fails the build):
   - `scripts/check-vulnerable-packages.sh BookWheel/BookWheel.csproj BookWheel.Tests/BookWheel.Tests.csproj`
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
