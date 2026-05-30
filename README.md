# Book Wheel Solution

Book Wheel is a .NET 8 web app for managing a list of books and spinning a wheel to pick a title at random.

This solution is split into separate application and test projects:

- `BookWheel/` contains the ASP.NET Core web application (API + static frontend)
- `BookWheel.Tests/` contains integration tests
- `BookWheel.slnx` ties both projects together

## Features

- First-run account creation plus cookie-based login/logout
- Add, edit, and remove books
- Interactive spin wheel UI
- Light/dark mode toggle with saved browser preference
- Theme toggle frontend test coverage
- Spin selection does not remove the selected book
- "Last selected" message displayed below the wheel
- Active books list with pagination after 10 books
- Book count plus page status summary in the books panel
- Delete confirmation modal for book removal
- Login form reset on logout so credentials are not left in the UI
- Wheel entropy shuffle when adding books
- Import/export modal with JSON tabs for merge import and JSON export
- Persistent storage in `App_Data/books.json`
- Encrypted credential storage in `App_Data/user.cred`
- Structured audit logs for failed login and rate-limit events
- Persistent JSONL log files in `App_Data/logs/`

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
3. Submitting the form creates the credential file and signs the user in.
4. Future visits use the normal login flow.

Credential storage details:

- Username and password hash are stored together in `BookWheel/App_Data/user.cred`
- The record is encrypted at rest with ASP.NET Core Data Protection
- The password is hashed with `PasswordHasher<T>` before being written to disk
- The credential file is created only when the user explicitly submits the setup form

Important:

- There is no default username/password in `appsettings.json`
- If you delete `BookWheel/App_Data/user.cred`, the app will prompt for first-run setup again

## Data Storage

Book data is stored in:

- `BookWheel/App_Data/books.json`

The file is created automatically if it does not exist.

Credential data is stored in:

- `BookWheel/App_Data/user.cred`

The file is created only after account setup is completed.

Log data is stored in:

- `BookWheel/App_Data/logs/bookwheel-YYYY-MM-DD.jsonl`

Each line is a JSON object with structured fields such as timestamp, level, category, message, request id, path, client IP, and user agent.

## API Overview

Base route: `/api`

Auth endpoints:

- `GET /api/auth/status`
- `POST /api/auth/setup`
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me`

`GET /api/auth/status` returns whether first-run setup is required. `POST /api/auth/setup` creates the initial account when no credential file exists.

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
- Spin behavior preserving active book count
- Book update and remove flow
- Security regression checks for encrypted credential storage, failed-login audit logging, and rate limiting
- Persistent log file creation and structured audit logging checks

Frontend-focused tests also verify that the HTML, JavaScript, and CSS expose the account setup mode, selected-book UI, pagination summary, delete confirmation flow, logout form reset behavior, and theme toggle behavior.

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

Use the toolbar `Import/Export` button to open the transfer modal.

- Import tab accepts JSON in either `[{"title":"..."}]` form or `{ "books": [{"title":"..."}] }` form.
- Import merges into existing books and skips case-insensitive title matches.
- Export tab generates JSON of the current book list and supports clipboard copy.

## Development Notes

- The test host uses a temporary content root so tests do not mutate real app data.
- The test host also mirrors `wwwroot` into a temporary folder so frontend behavior is exercised against real static assets.
- The test host captures structured logs so security audit events can be asserted in tests.
- The test host also verifies that log entries are written to persistent JSONL files in the temp `App_Data/logs` folder.
- Frontend behavior is implemented in `BookWheel/wwwroot/js/app.js`.
- The wheel UI and styles are in `BookWheel/wwwroot/index.html` and `BookWheel/wwwroot/css/site.css`.

## Troubleshooting

- If `dotnet test` reports file lock warnings from `testhost`, re-run the command; this is usually transient.
- If authentication fails unexpectedly, verify whether `BookWheel/App_Data/user.cred` exists and whether the first-run setup was completed.
- If the app starts but books are missing, check `BookWheel/App_Data/books.json` permissions.
- If you need to reset the account, delete `BookWheel/App_Data/user.cred` and create a new account on next launch.
- If you need to inspect logs, open the current day file under `BookWheel/App_Data/logs/`.
- If the container starts but auth sessions break after restarts, verify Data Protection keys are persisted (compose handles this via `bookwheel_dp_keys`).
- If port `8080` is busy, change the host side mapping in `docker-compose.yml` (for example, `8081:8080`).
