# Admin Guide

Running Book Wheel for other people: first-run setup, creating and managing
accounts, email delivery, and legacy data migration. For deployment and
environment configuration, see the [Self-Hosting Guide](self-hosting.md); for
what the app does day to day, see the [User Guide](user-guide.md).

## First-Run Account Setup

On the first visit, the login screen switches into account-creation mode if no user account exists in the database yet.

Flow:

1. Open the app.
2. If no user account exists in the database, the UI prompts you to create the first account.
3. Submitting the form creates the first user account as an administrator and signs the user in.
4. Future visits use the normal login flow.

Credential storage details:

- Account records are stored in PostgreSQL (the `users` table), configured via `ConnectionStrings:BookWheel`
- Each record includes user id, username, password hash, admin flag, email, and timestamps
- The password is hashed with `PasswordHasher<T>` before being written to the database
- The account row is created only when the user explicitly submits the setup form

Administrator details:

- The first account is marked as `isAdmin = true`
- Only administrators can create, update, remove, or generate password reset links for other user accounts
- Non-admin users cannot access user-management endpoints
- The first account cannot be removed
- Removing an account also deletes books assigned to that account, including any already soft-deleted

Password reset link details:

- Administrators do not set or reset user passwords directly
- Administrators generate a secure reset link for a user account
- Reset links expire after 24 hours
- Reset links are one-time use and become invalid after successful password update
- Reset token records are stored as hashed values and encrypted at rest

Important:

- There is no default username/password in `appsettings.json`
- To reset to first-run setup state, delete all rows from the `users` table in PostgreSQL; the app will prompt for account creation on the next visit

## Managing Users

The Manage Users tab (Settings dialog, admin-only) lists every account with searchable status filters and at-a-glance state indicators. Each entry shows its last-login date (or "Never logged in") and the directory can be sorted by it; the remove-account confirmation dialog states how many books will be removed before an admin confirms.

User-management endpoints (administrator only):

- `GET /api/users` — includes `lastLoginAtUtc` (`null` if the account has never logged in) and `bookCount` (active plus soft-deleted books, i.e. what `DELETE /api/users/{id}` would actually remove) per user, alongside the existing status fields
- `POST /api/users`
- `PUT /api/users/{id}`
- `DELETE /api/users/{id}`
- `POST /api/users/{id}/password-reset-link`

`POST /api/users` behavior:

- Request body accepts `username`, `isAdmin`, and `email` (required)
- Administrators do not provide a password when creating a user
- Response includes `setupLink` and `setupLinkExpiresAtUtc` for secure account setup sharing
- `email` must be unique across all accounts (case-insensitive); `PUT /api/users/{id}` can also set/clear/change an existing account's email
- Administrators cannot update their own account via `PUT /api/users/{id}` except for `email` — a self-edit request is rejected unless every other field matches the caller's current values

Session revocation: active sessions for a user end immediately when an administrator disables, locks, demotes, renames, or force-resets that account, and when a password reset is completed for it (editing only an email address does not sign anyone out).

## Email (SMTP)

Password-reset and forgotten-username emails are sent via SMTP, configured through `appsettings.json`'s `Smtp` section or environment variable overrides (see the [environment variable reference](self-hosting.md#environment-variable-reference)) — there is no in-app settings UI for this, matching how the Google Books API key is configured.

Leaving `Smtp:Host` empty disables email sending entirely; delivery is silently skipped and logged, never surfaced as an error to the caller.

Administrators can check readiness from Settings > Manage users: a banner appears if `Smtp:Host` or `App:BaseUrl` is unset, and "Send test email" sends a message to the administrator's own address and shows the SMTP error if delivery fails. The same status is available at `GET /api/system/config-status` (booleans only; no secrets are returned) and `POST /api/system/test-email`.

Self-service password-reset emails also need `App:BaseUrl` set to this deployment's public URL (e.g. `https://books.example.com`) so the link mailed to the user points somewhere real — it is deliberately never derived from the incoming request's `Host` header, since that header is attacker-controlled. It is empty by default, which silently disables the self-service "forgot my password" email (the admin-triggered reset-link flow in the Users admin page is unaffected, since it returns the link directly to the authenticated admin rather than emailing it).

Any administrator account without an email on file is prompted for one immediately after login with a non-dismissible dialog — this covers accounts created before email became a required field, since an admin has no self-service recovery path without one. This is the one exception to the rule that administrators can't edit their own account: `PUT /api/users/{id}` allows an admin to change only their own `email` (any other field in that request is rejected); non-admin users cannot self-edit at all.

## Legacy Data Migration Utility

Book Wheel supports an explicit migration utility for converting legacy payloads before normal runtime operations.

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

### Migrating from file-based storage to PostgreSQL

If you are upgrading from a version of Book Wheel that stored data in `App_Data/books.json` and `App_Data/user.cred`, run the one-shot migration once, with `ConnectionStrings:BookWheel` pointed at your new PostgreSQL database and the existing `App_Data/` directory still in place:

```bash
dotnet run --project BookWheel/BookWheel.csproj -- --migrate-to-postgres
```

This normalizes any legacy JSON schema first (same as `--migrate-data`), then copies all users, books, and password reset tokens into PostgreSQL in a single transaction, and exits. It refuses to run if PostgreSQL already has user data, so it is safe to leave in a deployment script — a second run is a no-op error, not a duplicate-data hazard. The original `App_Data/books.json` and `App_Data/user.cred` files are left untouched on disk as a historical backup; the running application no longer reads them.
