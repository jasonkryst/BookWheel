// Feature audit 2026-10 — issue catalog (single source of truth).
// Consumed by file-issues.mjs to create GitHub issues and generate ISSUES.md.
//
// impact:     H | M | L   — user/operator value or risk reduced
// complexity: S | M | L   — S ≈ ≤1 day, M ≈ 2–5 days, L ≈ 1–3 weeks
// phase:      1 = self-hosted, 2 = public face, 3 = SaaS
// tier is derived: score = impact(3/2/1)*2 - complexity(1/2/3)
//   5 → Tier 1 (quick wins), 4 → Tier 2, 3 → Tier 3, 2 → Tier 4, ≤1 → Tier 5

export const areas = {
  auth: 'Authentication & Sessions',
  accounts: 'Account Recovery & Email',
  admin: 'User Administration',
  library: 'Book Library Management',
  wheel: 'Spin Wheel',
  lookup: 'Metadata Lookup, Scanner & Book Search',
  stats: 'Stats & Insights',
  transfer: 'Import / Export & Backup',
  prefs: 'Preferences, Theming, i18n & Analytics',
  pwa: 'PWA & Offline',
  ops: 'Deployment & Operations',
  core: 'Core / Cross-cutting',
};

export const issues = [
  // ───────────────────────────── AUTH ─────────────────────────────
  {
    key: 'AUTH-1', area: 'auth', type: 'bug', security: true, advisory: true, impact: 'H', complexity: 'S', phase: 1,
    title: 'Security: session lifecycle hardening (private advisory)',
    current: 'Tracked as a private GitHub Security Advisory until fixed. Full write-up in the gitignored advisories.local.mjs.',
    rec: 'See the advisory.',
    ac: ['Advisory resolved and published'],
    refs: [],
  },
  {
    key: 'AUTH-2', area: 'auth', type: 'bug', impact: 'M', complexity: 'S', phase: 1,
    title: 'Auth cookie has a fixed 8h expiry while the server session slides — users are logged out mid-use',
    current: `\`SignInAsync\` sets \`Expires = now + 8h\` once, at login. \`GetAuthenticatedUser\` extends the server-side session on every request, but the cookie is never re-issued. The browser therefore drops the cookie exactly 8 hours after login, even for an active user, and the SPA shows the login screen with no warning.`,
    rec: `Re-issue the cookie when more than half its lifetime has passed (sliding refresh), or adopt ASP.NET Core cookie authentication with \`SlidingExpiration\` (CORE-1). Optionally add a "Keep me signed in" checkbox: a 30-day persistent cookie versus a session cookie.`,
    ac: ['An active user is not logged out at the 8h mark', 'Idle timeout behaviour is documented', 'Optional "keep me signed in" is persisted per session'],
    refs: ['BookWheel/Controllers/AuthController.cs (SignInAsync)', 'BookWheel/Services/AuthService.cs (GetAuthenticatedUser)'],
  },
  {
    key: 'AUTH-3', area: 'auth', type: 'enhancement', impact: 'H', complexity: 'S', phase: 1,
    title: 'Self-service password change for signed-in users',
    current: `A signed-in user can't change their own password. The only paths are an admin-generated reset link or the email "forgot password" flow, and the email flow needs SMTP and \`App:BaseUrl\` configured. This was listed as the #2 "next change" in IMPROVEMENT_ROADMAP.md and is still open.`,
    rec: `Add \`POST /api/auth/password\` with \`{ currentPassword, newPassword }\`, rate-limited and audited, that also revokes the user's *other* sessions. Add a "Security" section to a new Account tab in Settings (see ACCOUNTS-4).`,
    ac: ['Wrong current password → 400 with a localized message', 'Success revokes other sessions but keeps the current one', 'Available to admins and non-admins', 'en/es/pl strings added'],
    refs: ['IMPROVEMENT_ROADMAP.md Priority 4 item 1', 'Related: #47'],
  },
  {
    key: 'AUTH-4', area: 'auth', type: 'enhancement', security: true, impact: 'M', complexity: 'M', phase: 2,
    title: 'Strengthen password policy: breached-password check and strength feedback',
    current: `The only rule is a minimum length of 8 (\`[StringLength(128, MinimumLength = 8)]\`). There is no feedback on weak or common passwords. That's acceptable for a family self-host, but too weak for a public-facing instance.`,
    rec: `Add an optional Have I Been Pwned k-anonymity range check (configurable, off when there's no egress) and a client-side strength meter. Apply it to setup, reset, and change-password.`,
    ac: ['Config flag `Security:CheckBreachedPasswords`', 'Breached passwords rejected with a localized explanation', 'Strength meter is accessible (text, not only colour)'],
    refs: ['BookWheel/Models/SetupAccountRequest.cs', 'BookWheel/Models/CompletePasswordResetRequest.cs'],
  },
  {
    key: 'AUTH-5', area: 'auth', type: 'enhancement', impact: 'L', complexity: 'M', phase: 2,
    title: 'Active-session list with "sign out other devices"',
    current: `Sessions are invisible to users. There's no way to see where you're signed in or to end a session on a lost device.`,
    rec: `Once sessions are persisted (CORE-2), record the user agent, IP, and last-seen time. Show them in Account settings with per-session and "all other sessions" revoke.`,
    ac: ['List shows the current session flagged', 'Revoking a session ends it on the next request'],
    refs: ['Depends on CORE-2'],
  },
  {
    key: 'AUTH-6', area: 'auth', type: 'enhancement', security: true, impact: 'M', complexity: 'L', phase: 2,
    title: 'Optional TOTP two-factor authentication (required-for-admins option)',
    current: `Password is the only factor. Admins can create, delete, and lock users and read cross-user stats.`,
    rec: `Add opt-in TOTP with recovery codes, plus an instance setting to require 2FA for admins. Store secrets encrypted with Data Protection.`,
    ac: ['Enrol/verify/disable flows', 'Recovery codes (one-time use)', 'Admin-enforced policy', 'Lockout applies to the TOTP step'],
    refs: ['Depends on CORE-1'],
  },
  {
    key: 'AUTH-7', area: 'auth', type: 'enhancement', impact: 'M', complexity: 'L', phase: 2,
    title: 'Optional external sign-in via OpenID Connect (Authentik/Keycloak/Google)',
    current: `Only local username/password accounts exist. Self-hosters who already run an IdP can't use it, and a public launch would benefit from social sign-in. IMPROVEMENT_ROADMAP.md lists this as a recommended next step.`,
    rec: `Add a generic OIDC provider configured by environment (authority, client ID, secret), mapping to local users by verified email, with admin-controlled auto-provisioning. Keep local auth as the default.`,
    ac: ['Works with at least one self-hosted IdP and Google', 'Account linking by verified email', 'Local login can be disabled by config'],
    refs: ['IMPROVEMENT_ROADMAP.md Priority 4 item 3', 'Depends on CORE-1'],
  },

  // ─────────────────────────── ACCOUNTS ───────────────────────────
  {
    key: 'ACCOUNTS-1', area: 'accounts', type: 'enhancement', impact: 'H', complexity: 'S', phase: 1,
    title: 'Surface email-delivery readiness to admins (SMTP + App:BaseUrl status, "send test email")',
    current: `When \`Smtp:Host\` or \`App:BaseUrl\` is unset, password-reset and username emails are skipped silently and only logged. That's by design for anti-enumeration, but the admin also has no signal. Self-hosters assume "forgot password" works until someone is locked out. With BaseUrl empty, admin-generated setup links are relative (\`/?resetToken=…\`), and pasting them into chat produces a broken link.`,
    rec: `Add an admin-only \`GET /api/system/config-status\` (SMTP configured, BaseUrl set, Google Books key present, proxy trust configured) and \`POST /api/system/test-email\` (sends to the admin's own address and reports the SMTP error inline). Show a warning banner in Manage Users when anything that recovery depends on is missing.`,
    ac: ['Banner visible to admins when SMTP or BaseUrl is missing', 'Test email reports success or the SMTP error text', 'No secrets returned by the status endpoint'],
    refs: ['BookWheel/Services/SmtpEmailSender.cs', 'BookWheel/Models/AppOptions.cs'],
  },
  {
    key: 'ACCOUNTS-2', area: 'accounts', type: 'bug', impact: 'M', complexity: 'S', phase: 1,
    title: 'New-user invites send a "Reset your password" email instead of a welcome/invite email',
    current: `\`UsersController.CreateUser\` calls \`AuthService.CreatePasswordResetLinkAsync\`. When the new user has an email (now required), that fires \`SendPasswordResetEmailAsync\` with the subject "Reset your Book Wheel password" and the body "A password reset was requested…". A brand-new invitee gets a confusing, phishing-looking message.`,
    rec: `Add \`SendInvitationEmailAsync\` ("<admin> invited you to Book Wheel — set your password") and pass the purpose (invite vs reset) through \`CreatePasswordResetLinkAsync\`. Let the admin choose "email the invite" or "just give me the link".`,
    ac: ['Invite email has distinct subject and copy', 'Reset email unchanged', 'Admin UI shows whether the invite email was queued'],
    refs: ['BookWheel/Services/AuthService.cs (CreatePasswordResetLinkAsync)', 'BookWheel/Services/AccountEmailService.cs'],
  },
  {
    key: 'ACCOUNTS-3', area: 'accounts', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Localize outbound emails and add an HTML template',
    current: `Emails are hard-coded English plain text in \`AccountEmailService\`, even though the UI supports en/es/pl. The server can't know a user's language because the language preference lives only in localStorage (see PREFS-1).`,
    rec: `Move templates to resx (or per-locale template files), render multipart text and HTML, and pick the locale from the stored user preference, falling back to the request culture.`,
    ac: ['Reset, username, and invite emails in en/es/pl', 'HTML + text multipart', 'Snapshot tests per locale'],
    refs: ['BookWheel/Services/AccountEmailService.cs', 'Depends on PREFS-1'],
  },
  {
    key: 'ACCOUNTS-4', area: 'accounts', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Account settings tab: users manage their own email (with verification) and profile',
    current: `Non-admin users can't view or change their own email, and admins can only edit their own email through a special case in \`UpdateUser\`. If a user's email changes, the self-service recovery path silently breaks. Issue #47 asks for "enhanced self account" without specifics. This provides the foundation.`,
    rec: `Add Settings → Account (all users): show username and email, change email (sends a verification link to the new address before switching), change password (AUTH-3), and later sessions (AUTH-5) and 2FA (AUTH-6). Remove the admin self-edit special case from \`UsersController.UpdateUser\`.`,
    ac: ['Email change requires verification of the new address', 'Old address notified of the change', 'Admin self-edit special case removed'],
    refs: ['BookWheel/Controllers/UsersController.cs (UpdateUser self branch)', 'Related: #47'],
  },
  {
    key: 'ACCOUNTS-5', area: 'accounts', type: 'enhancement', impact: 'M', complexity: 'M', phase: 3,
    title: 'Durable email outbox with retry instead of fire-and-forget sends',
    current: `Emails are sent with \`_ = SendAsync(...)\` (deliberately, for timing safety). A transient SMTP failure loses the message permanently, and a restart drops in-flight sends.`,
    rec: `Write emails to an \`email_outbox\` table and deliver from a hosted background service with exponential backoff and a dead-letter state. Timing safety is preserved because the request only does a constant-time insert.`,
    ac: ['Failed sends retried with backoff', 'Admin can see failed messages (no bodies containing tokens)', 'Tokens in outbox rows are purged after send or expiry'],
    refs: ['BookWheel/Services/AuthService.cs', 'BookWheel/Services/SmtpEmailSender.cs'],
  },

  // ───────────────────────────── ADMIN ────────────────────────────
  {
    key: 'ADMIN-1', area: 'admin', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Show last-login time and library size in the user directory',
    current: `The admin user list shows status flags only. Admins can't tell dormant accounts from active ones, or see how much data a deletion would remove until after confirming.`,
    rec: `Add \`LastLoginAtUtc\` to \`users\` (set on successful login) and return \`bookCount\` and \`lastLoginAtUtc\` in \`GET /api/users\`. Show the counts in the delete-confirmation dialog too.`,
    ac: ['Migration adds nullable `last_login_at_utc`', 'Directory column + sort by last login', 'Delete dialog states "N books will be removed"'],
    refs: ['BookWheel/Storage/Postgres/PostgresCredentialRepository.cs (GetUsersAsync)'],
  },
  {
    key: 'ADMIN-2', area: 'admin', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Persist an admin audit trail in the database and show it in the admin UI',
    current: `Role changes, account state changes, user creation and deletion, and reset-link generation are logged only to JSONL files (14-day retention). An admin can't answer "who disabled this account?" without shell access to the container.`,
    rec: `Add an \`audit_events\` table (actor, target, action, before/after JSON, timestamp, request ID). Write to it alongside the existing logs, and add an admin "Activity" tab with filtering. Retention is configurable.`,
    ac: ['All existing audited actions are persisted', 'Admin-only read endpoint with paging', 'Retention job'],
    refs: ['BookWheel/Controllers/UsersController.cs'],
  },

  // ──────────────────────────── LIBRARY ───────────────────────────
  {
    key: 'LIBRARY-1', area: 'library', type: 'enhancement', impact: 'H', complexity: 'S', phase: 1,
    title: 'Search, filter, and sort the active book list (and configurable page size)',
    current: `The book list is ordered by \`Id\` (a random GUID, so effectively random) and paginated client-side at a fixed 10 per page. There's no search, sort, or filter. With 50+ books, finding one to edit means paging through. This is the #1 "next change" in IMPROVEMENT_ROADMAP.md.`,
    rec: `Client-side for now, since the full list is already loaded: a search box (title/author/ISBN), a type filter, sort (title, author, date added, last spun), and a page size of 10/25/50/all. Persist choices in localStorage. Also change the server ordering from \`Id\` to \`CreatedAtUtc\` so default order is stable and meaningful.`,
    ac: ['Search narrows results as you type (debounced)', 'Sort and filter survive reload', 'Pagination text reflects filtered counts', 'Keyboard and screen-reader friendly'],
    refs: ['BookWheel/wwwroot/js/app.js (renderActiveBooks, BOOKS_PER_PAGE)', 'BookWheel/Storage/Postgres/PostgresBookRepository.cs (GetAllAsync OrderBy Id)'],
  },
  {
    key: 'LIBRARY-2', area: 'library', type: 'enhancement', impact: 'H', complexity: 'S', phase: 1,
    title: 'Trash: undo a removal and restore soft-deleted books',
    current: `Removing a book soft-deletes it (\`DeletedAtUtc\`), but nothing in the UI or API can see or restore deleted books. Re-adding the same title by import is also blocked (by design) because deleted titles count as duplicates. Soft-delete currently protects data without letting users recover it.`,
    rec: `Add an "Undo" action on the removal toast (about 8s). Add \`GET /api/books/deleted\` and \`POST /api/books/{id}/restore\`, with a "Recently removed" view in the library (optionally purged after N days).

**Owner decisions (2026-10-11):**
- Removal is **user-scoped only**: it never affects other users or the shared catalog (CORE-16).
- A removed book can be **re-added at any time**. Adding, scanning or importing a title that matches one of the user's removed books **restores** that entry (with its history and formats) instead of being blocked as a duplicate. This replaces today's import rule that skips previously deleted titles.`,
    ac: ['Undo restores the book to the wheel', 'Restore endpoint scoped to the owner', 'Restored books keep their spin history', 'Re-adding, scanning or importing a removed title restores it rather than skipping it', 'Removal never affects other users or catalog data'],
    refs: ['BookWheel/Storage/Postgres/PostgresBookRepository.cs (RemoveAsync)', 'BookWheel/Controllers/BooksController.cs'],
  },
  {
    key: 'LIBRARY-3', area: 'library', type: 'enhancement', impact: 'H', complexity: 'M', phase: 1,
    title: 'Reading status lifecycle: To Read → Reading → Finished / Did Not Finish',
    current: `A book is either on the wheel or removed. After a spin picks a book, the user has to remember to delete it once read. Deleting erases the fact that they finished it, and the spin stats can't distinguish "read" from "removed". Listed in IMPROVEMENT_ROADMAP.md (P1 item 10).`,
    rec: `Add \`status\` (to_read, reading, finished, dnf), plus \`started_at\` and \`finished_at\` to books. Only \`to_read\` books are eligible for spins by default. The spin result offers "Start reading" (WHEEL-4), and the library gets status tabs. Export and import carry the status. **Owner decision (2026-10-11): a spin never changes status automatically; status changes only by explicit user action.** Status is per user (it lives on the library entry, CORE-16), and any status can move back to To Read at any time (a "Read again" action), with each completed read kept in a small reading log so re-reads don't erase history.`,
    ac: ['Migration with default `to_read`', 'Finished/DNF → To Read ("Read again") allowed at any time; past reads are kept', 'Spin excludes non-to_read by default', 'A spin never changes a book\'s status automatically', 'Status visible and editable in the list and edit dialog', 'Stats can count finished books (STATS-1)'],
    refs: ['BookWheel/Storage/Postgres/Entities/BookEntity.cs', 'IMPROVEMENT_ROADMAP.md Priority 1 item 10'],
  },
  {
    key: 'LIBRARY-4', area: 'library', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Warn on duplicate titles/ISBNs when adding a book manually',
    current: `Import de-duplicates by normalized title or ISBN, but \`POST /api/books\` (manual add, scanner, or Book Search "Add to Wheel") accepts exact duplicates silently. Scanning the same shelf twice doubles the wheel.`,
    rec: `Return \`409\` with the existing book when the title or ISBN matches an active book. The client then offers "Add anyway" (sending \`allowDuplicate: true\`) or "View existing". Two extra cases:
- If the match is a book the user previously **removed**, offer **Restore** instead (LIBRARY-2).
- Once CORE-16 lands, a title/author match on an existing entry asks **"Is this another format of X?"**: yes adds a format, no adds a new entry.`,
    ac: ['Duplicate detected for title (case-insensitive) and ISBN', 'User can override', 'Scanner flow shows the duplicate notice without adding', 'Match on a removed book offers Restore', 'All add paths (manual, scan, Book Search, import) use one shared resolve step returning Exact / Removed / Likely / New', 'Batch flows (import, shelf scan) show outcomes as a review list instead of individual prompts'],
    refs: ['BookWheel/Controllers/BooksController.cs (Add)'],
  },
  {
    key: 'LIBRARY-11', area: 'library', type: 'enhancement', impact: 'L', complexity: 'M', phase: 2,
    title: 'Fuzzy duplicate suggestions with pg_trgm (near-miss titles)',
    current: `The match key from CORE-16 catches normalized duplicates, but not near-misses. Examples are regional titles ("Sorcerer's Stone" vs "Philosopher's Stone"), typos, or translated subtitles.`,
    rec: `Enable the \`pg_trgm\` extension with a GIN index on the match key, and add similarity results (threshold about 0.6) to the possible-duplicates review and the add-time prompt. **Suggestions only, never a block or an automatic merge**, and "not a duplicate" answers are respected.`,
    ac: ['Near-miss pairs appear in the review list', 'Never blocks an add', 'Respects remembered "not a duplicate" pairs', 'Indexed (no sequential scans on large libraries)'],
    refs: ['Depends on CORE-16'],
  },
  {
    key: 'LIBRARY-5', area: 'library', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Tags/collections on books (genre, mood, owner, book club) usable as wheel filters',
    current: `The only classification is the fixed book type (Physical/Digital/Nook Only). Households can't separate whose book is whose, and readers can't spin "something short" or "sci-fi".`,
    rec: `Add user-scoped \`tags\` and a \`book_tags\` join table. Add tag chips in the edit dialog and list, a tag filter in the library (LIBRARY-1), and spin filters (WHEEL-2). Include tags in export and import.`,
    ac: ['Create, rename, and delete tags', 'Multi-tag per book', 'Filter list by tag', 'Export/import round-trips tags'],
    refs: ['IMPROVEMENT_ROADMAP.md Priority 1 item 10'],
  },
  {
    key: 'LIBRARY-6', area: 'library', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Cache cover images locally instead of hot-linking third-party URLs',
    current: `\`CoverUrl\` points at covers.openlibrary.org, books.google.com, or any user-supplied URL, and the browser loads it directly. Every page view leaks the reader's IP and library to third parties, covers break when providers change URLs, and covers never work offline (PWA).`,
    rec: `Fetch the cover once server-side on add or update (size-capped, content-type checked, SSRF-safe allow-list of provider hosts), store it in Postgres (a \`book_covers\` table holding \`bytea\` plus content type, so the \`pg_dump\` backup from OPS-2 captures covers with no extra volume to back up; decided 2026-10-11), and serve it from \`/api/books/{id}/cover\` with long cache headers. Keep the original URL for re-fetch.`,
    ac: ['No third-party image requests from the UI for cached covers', 'SSRF protections (host allow-list, size cap, timeout)', 'Covers available offline via the SW cache'],
    refs: ['BookWheel/wwwroot/js/app.js (renderActiveBooks coverImg.src)'],
  },
  {
    key: 'LIBRARY-7', area: 'library', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Admin-managed book types (and data-driven providers) instead of hard-coded IDs',
    current: `Book types live in a \`book_types\` table, but the API validates with \`[Range(1, 3)]\`, and the UI hard-codes \`BOOK_TYPE_ICONS\`/\`BOOK_TYPE_I18N_KEYS\` and the \`<option>\`s. Providers are similar: \`[Range(1, 2)]\` plus a hard-coded dictionary in \`BookMetadataLookupDispatcher\`. Adding "Audiobook" or "Library loan" needs code changes in four places.`,
    rec: `Add \`GET /api/reference\` that returns types and providers (with icon and i18n key). Validate IDs against the table, not a range, and render options from the API. Then add an admin-only **Book types** tab in Settings to create, rename, change the emoji icon, reorder, and retire types. A retired type stays on existing books but is hidden from pickers, and a type that's in use can't be hard-deleted. Admin-created type names are stored per locale (en/es/pl, falling back to the default). **Owner decision (2026-10-11): keep the existing "Nook Only" type as-is;** the three seeded types become ordinary admin-editable rows.`,
    ac: ['No numeric ranges in request models', 'UI builds selects from the API', 'Admins can add, rename, reorder and retire types without a deploy', 'Types in use can be retired but not deleted', 'Seeded Physical/Digital/Nook Only types unchanged by the migration'],
    refs: ['BookWheel/Models/UpdateBookRequest.cs', 'BookWheel/Services/BookMetadataLookupDispatcher.cs', 'BookWheel/wwwroot/index.html (bookType select)'],
  },
  {
    key: 'LIBRARY-8', area: 'library', type: 'enhancement', impact: 'L', complexity: 'M', phase: 2,
    title: 'Bulk actions in the library (multi-select remove, change type/tag/status)',
    current: `Every edit or removal is one book at a time through a modal. Cleaning up after an import is tedious.`,
    rec: `Add a selection mode with checkboxes, a sticky bulk-action bar, and \`POST /api/books/bulk\` (operation + IDs) running in a single transaction.`,
    ac: ['Select all on page / all filtered', 'Single transaction', 'Undo for bulk remove (uses LIBRARY-2)'],
    refs: [],
  },
  {
    key: 'LIBRARY-9', area: 'library', type: 'enhancement', impact: 'L', complexity: 'M', phase: 2,
    title: 'Richer book details: notes, page count, series, description',
    current: `Books have only title, author, ISBN, cover, and type. Providers already return page counts, descriptions, and series data that is discarded.`,
    rec: `Capture optional page count, series name/number, description (from lookup), and a free-text personal note. Show them in a book detail view. Page count enables "short read" spin filters (WHEEL-2) and pages-read stats.`,
    ac: ['Lookup fills new fields when available', 'Fields are optional and round-trip in export'],
    refs: ['BookWheel/Services/OpenLibraryBookMetadataLookupService.cs', 'BookWheel/Services/GoogleBooksBookMetadataLookupService.cs'],
  },
  {
    key: 'LIBRARY-10', area: 'library', type: 'enhancement', impact: 'L', complexity: 'S', phase: 2,
    title: 'First-run onboarding: guided first book and optional sample list',
    current: `A new user sees an empty wheel with two hint paragraphs. There's no guidance about scanning, Book Search, or import, which are the fastest ways to fill a wheel.`,
    rec: `Replace the empty state with three call-to-action cards: Scan a barcode, Search for a book, Import a list (including Goodreads CSV, TRANSFER-4). Optionally offer "Try with 8 sample classics" so the wheel can be spun immediately, and remove the samples with one click.`,
    ac: ['Empty state CTAs open the right flow', 'Sample list is clearly removable'],
    refs: ['BookWheel/wwwroot/js/app.js (renderActiveBooks empty state)'],
  },

  // ───────────────────────────── WHEEL ────────────────────────────
  {
    key: 'WHEEL-1', area: 'wheel', type: 'bug', impact: 'H', complexity: 'S', phase: 1,
    title: 'Wheel rendering doesn\'t scale: blurry on HiDPI, unreadable beyond ~30 books',
    current: `The canvas is a fixed 420×420 with no \`devicePixelRatio\` scaling, so it's blurry on phones and retina screens. Labels are cut at 18 characters with a fixed \`bold 18px Arial\` font regardless of slice angle. At 40+ books the labels overlap into an unreadable band. Slice text colour reuses \`--input-bg\` across 6 slice colours without a per-slice contrast check.`,
    rec: `Scale the backing store by \`devicePixelRatio\`. Derive font size from slice arc length (clamped), and hide labels below a minimum and rely on the highlighted result plus the SR list. Pick black or white per slice by computed contrast. Optionally show at most N labels and an "N more" ring when the list is very large.`,
    ac: ['Crisp at DPR 2 and 3', 'Readable at 10/30/60/100 books (screenshots in PR)', 'Each slice label meets 4.5:1 or is omitted', 'No regression in the reduced-motion path'],
    refs: ['BookWheel/wwwroot/js/app.js (drawWheel)', 'BookWheel/wwwroot/index.html (wheelCanvas width=420)'],
  },
  {
    key: 'WHEEL-2', area: 'wheel', type: 'enhancement', impact: 'H', complexity: 'M', phase: 1,
    title: 'Spin filters: spin only a subset (type, tag, status, length)',
    current: `Every spin draws from the entire active list. "I want a digital book tonight" or "only book-club picks" means temporarily deleting books.`,
    rec: `Add a filter row above Spin (type now; tags, status, and page count as LIBRARY-3/5/9 land). \`POST /api/books/spin\` accepts the filter, the wheel redraws with only eligible books, and the filter is remembered per device.`,
    ac: ['Server-side filtering (the client can\'t pick a book outside the filter)', 'Wheel shows only eligible slices', 'Empty-filter state handled'],
    refs: ['BookWheel/Storage/Postgres/PostgresBookRepository.cs (SelectRandomAsync)'],
  },
  {
    key: 'WHEEL-3', area: 'wheel', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Weighted spins and a "don\'t repeat recent picks" cooldown',
    current: `Selection is uniform \`Random.Shared.Next\`. The same book can be picked twice in a row, and there's no way to favour books that have waited longest. Listed in IMPROVEMENT_ROADMAP.md (P3 items 3–4).`,
    rec: `Per-user spin settings: (a) exclude the last N picks, (b) "favour neglected books" weighting by days since added or last spun, (c) a per-book manual weight (1–5). Show slice sizes proportional to weight so the wheel stays honest.`,
    ac: ['Deterministic tests with a seeded RNG', 'Slice size reflects weight', 'Settings persisted server-side'],
    refs: ['BookWheel/Storage/Postgres/PostgresBookRepository.cs (SelectRandomAsync)', 'IMPROVEMENT_ROADMAP.md Priority 3 items 3–4'],
  },
  {
    key: 'WHEEL-4', area: 'wheel', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Spin result actions: Start reading / Spin again / Not this one',
    current: `After a spin the result is shown in text and a toast, with no actions. Every pick is recorded identically in history, so stats can't tell picks the user accepted from ones they re-spun past.`,
    rec: `Show a result card with "Start reading" (sets status to reading, LIBRARY-3), "Spin again" (records the pick as \`declined\`), and "Remove from wheel". Add an \`outcome\` column to \`spin_selections\`.`,
    ac: ['Outcome persisted', 'Stats can exclude declined picks', 'Keyboard accessible card with focus moved to it after the spin'],
    refs: ['BookWheel/wwwroot/js/app.js (spin handler)', 'BookWheel/Storage/Postgres/Entities/SpinSelectionEntity.cs'],
  },
  {
    key: 'WHEEL-5', area: 'wheel', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Spin history timeline in the UI',
    current: `\`GET /api/books/spin-history\` exists, but no UI uses it. The user sees only the last result, which is lost on reload.`,
    rec: `Add a "Recent picks" panel (last 10, with relative dates and outcome) under the wheel and a full paged history in Stats. Restore "Last selected" from history on page load.`,
    ac: ['Last pick survives reload', 'History paged server-side', 'Dates localized'],
    refs: ['BookWheel/Controllers/BooksController.cs (GetSpinHistory)'],
  },
  {
    key: 'WHEEL-6', area: 'wheel', type: 'enhancement', impact: 'L', complexity: 'S', phase: 2,
    title: 'Spin celebration polish: tick sound, haptics, confetti (opt-in, reduced-motion aware)',
    current: `The spin is a CSS rotation with a 4.2s timeout. It works, but it has none of the tactile feedback that makes a wheel fun, which matters for the public face and shareability.`,
    rec: `Add optional slice-tick audio, \`navigator.vibrate\` on stop, and a short confetti burst, all off under \`prefers-reduced-motion\` and behind a Preferences toggle. Use \`transitionend\` instead of a fixed \`setTimeout\` so the result can't desync from the animation.`,
    ac: ['All effects disabled with reduced motion', 'Preference toggle', 'Result shown on transitionend'],
    refs: ['BookWheel/wwwroot/js/app.js (spin handler setTimeout)'],
  },

  // ──────────────────────────── LOOKUP ────────────────────────────
  {
    key: 'LOOKUP-1', area: 'lookup', type: 'enhancement', impact: 'H', complexity: 'M', phase: 1,
    title: 'Barcode scanner fallback for iOS Safari and Firefox',
    current: `The scanner depends on the native \`BarcodeDetector\` API (Chrome, Edge, Android). On iPhone or iPad Safari and on Firefox, the camera button just explains it's unsupported. For a home-library app that excludes a large share of phone users.`,
    rec: `Lazy-load a WASM decoder (e.g. zxing-wasm, ~300 KB, only when \`BarcodeDetector\` is absent) behind the same \`openBarcodeScanner\` interface. Keep native detection when available.`,
    ac: ['Scanning works on iOS Safari 17+ and Firefox Android', 'Decoder downloaded only on first scanner open', 'CSP-compatible (CORE-3)'],
    refs: ['BookWheel/wwwroot/js/app.js (BARCODE_SCANNER_SUPPORTED, openBarcodeScanner)'],
  },
  {
    key: 'LOOKUP-2', area: 'lookup', type: 'enhancement', security: true, impact: 'M', complexity: 'S', phase: 1,
    title: 'Cache metadata lookups and rate-limit /lookup and /info per user',
    current: `\`/api/books/lookup\` and \`/api/books/info\` proxy every request to Open Library or Google Books with no caching and no per-user limit (the global limiter covers only login and reset). A signed-in user or script can hammer third-party APIs through the server (and burn a Google API key quota), and repeat scans of the same ISBN pay the full round trip each time.`,
    rec: `Add \`IMemoryCache\` (or a \`metadata_cache\` table) keyed by provider, ISBN, or normalized title, with a TTL of about 7 days for hits and 1 hour for misses. Add a per-user fixed-window limiter (e.g. 30/min) on both endpoints.`,
    ac: ['Repeat ISBN lookup served from cache (test with fake provider counter)', '429 after the limit with a localized message', 'Cache bounded in size'],
    refs: ['BookWheel/Controllers/BooksController.cs (Lookup, GetBookInfo)', 'BookWheel/Program.cs (AddRateLimiter)'],
  },
  {
    key: 'LOOKUP-3', area: 'lookup', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Unify the duplicated lookup flows (/lookup vs /info, add-row vs Book Search dialog)',
    current: `Two endpoints do near-identical work (\`Lookup\` and \`GetBookInfo\` differ only in response shape and links), and the frontend has two parallel pickers (\`openLookupPicker\` and \`openBookInfoPicker\`) plus two result renderers. Fixes have to be made twice, and the two flows already behave differently (e.g. ISBN miss: 404 versus a fallback result).`,
    rec: `Keep one \`GET /api/books/lookup\` that returns results with links, deprecate \`/info\`, and extract one shared picker and result component used by the add row, edit dialog, and Book Search.`,
    ac: ['One endpoint, one picker implementation', 'Existing behaviour preserved by tests', '/info returns 410 or redirects after one release'],
    refs: ['BookWheel/Controllers/BooksController.cs', 'BookWheel/wwwroot/js/app.js (openLookupPicker, openBookInfoPicker)'],
  },
  {
    key: 'LOOKUP-4', area: 'lookup', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Continuous "scan a shelf" mode for adding many books quickly',
    current: `Each scan closes the scanner, fills the add row, and requires a manual Add. Cataloguing a shelf of 40 books takes about 160 taps.`,
    rec: `Add a batch-scan mode: the camera stays open, each detected ISBN is looked up and appended to a pending list (with cover and duplicate badge, LIBRARY-4), then "Add N books" commits in one request.`,
    ac: ['Debounce the same ISBN', 'Audible or haptic confirmation per scan', 'Single batch commit'],
    refs: ['BookWheel/wwwroot/js/app.js (startScanLoop, onBarcodeDetected)'],
  },
  {
    key: 'LOOKUP-5', area: 'lookup', type: 'enhancement', impact: 'L', complexity: 'S', phase: 1,
    title: 'Faster provider fallback: shorter timeouts, and show which provider answered',
    current: `The dispatcher tries providers sequentially, each with an 8s \`HttpClient\` timeout, so a provider outage means 8s+ of spinner before fallback. Results don't say which provider supplied them.`,
    rec: `Lower the per-provider timeout (about 3s) with an overall budget, add a simple circuit breaker that skips a provider for 60s after consecutive failures, and display a "via Open Library/Google Books" caption.`,
    ac: ['Worst-case lookup under 5s when one provider is down', 'Provider caption in results'],
    refs: ['BookWheel/Services/BookMetadataLookupDispatcher.cs', 'BookWheel/Program.cs (HttpClient timeouts)'],
  },
  {
    key: 'LOOKUP-6', area: 'lookup', type: 'enhancement', impact: 'M', complexity: 'S', phase: 2,
    title: 'Configurable retailer links (region and affiliate tags) for Book Search',
    current: `\`BookSearchLinksService\` hard-codes amazon.com, barnesandnoble.com, and others. Polish- and Spanish-locale users get US stores, and there's no hook for affiliate tags (#45, a sustainability goal).`,
    rec: `Move the retailer list into configuration (name, URL template for ISBN and text, enabled, optional affiliate parameter) with locale-aware defaults (amazon.pl, amazon.es, Bookshop.org). Add \`rel="sponsored noopener"\` where affiliate tags apply, and disclose affiliate links in the UI.`,
    ac: ['Retailers configurable by env/appsettings', 'Locale picks the regional store', 'Affiliate disclosure text shown when tags are set'],
    refs: ['BookWheel/Services/BookSearchLinksService.cs', 'Related: #45'],
  },

  // ───────────────────────────── STATS ────────────────────────────
  {
    key: 'STATS-1', area: 'stats', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Stats over time: spins/finished per month, date-range filter',
    current: `Stats are all-time totals only: total spins, unique, never-spun, top books, and type breakdown. There's no trend or time window, and with no reading status (LIBRARY-3) no reading pace.`,
    rec: `Add a date-range selector (30d / 12m / all) and a monthly bar chart of spins (and finished books once LIBRARY-3 lands). Do the aggregation in SQL, grouped by month.`,
    ac: ['Range applies to every stat', 'Chart has an accessible table alternative', 'Server-side aggregation'],
    refs: ['BookWheel/Storage/Postgres/PostgresSpinStatsRepository.cs'],
  },
  {
    key: 'STATS-2', area: 'stats', type: 'bug', impact: 'L', complexity: 'S', phase: 1,
    title: 'Admin aggregate "active users" counts every account, not active ones',
    current: `\`GetAggregateAsync\` sets \`ActiveUserCount = context.Users.CountAsync()\`, which includes disabled, locked, and never-logged-in accounts. The label misleads.`,
    rec: `Rename to \`totalUserCount\`, and add a real \`activeUserCount\` (users with a spin or login in the last 30 days, using ADMIN-1's \`LastLoginAtUtc\`).`,
    ac: ['Both counts shown with clear labels', 'Test covers a disabled user'],
    refs: ['BookWheel/Storage/Postgres/PostgresSpinStatsRepository.cs (GetAggregateAsync)'],
  },
  {
    key: 'STATS-3', area: 'stats', type: 'enhancement', impact: 'L', complexity: 'S', phase: 1,
    title: 'Replace single longest/shortest "on wheel" cards with a "most neglected" list',
    current: `"Longest on wheel" and "shortest on wheel" each show one book. The more actionable insight, a list of books waiting longest without ever being picked, isn't surfaced.`,
    rec: `Show the top 5 oldest never-spun books with days waiting, and link to WHEEL-3's "favour neglected" option.`,
    ac: ['List of 5 with days waiting', 'Empty state when every book has been spun'],
    refs: ['BookWheel/Storage/Postgres/PostgresSpinStatsRepository.cs (GetForUserAsync)'],
  },
  {
    key: 'STATS-4', area: 'stats', type: 'enhancement', impact: 'L', complexity: 'M', phase: 2,
    title: 'Shareable "year in books" summary card',
    current: `Stats live in a modal with no export or share option. A shareable image is a cheap, organic growth loop for a public launch.`,
    rec: `Generate a PNG card (canvas) with spins, finished count, and top picks for a year, and use the Web Share API where available (download otherwise). Include no personal data beyond what the user chooses.`,
    ac: ['Card renders in all themes', 'Share or download works on mobile and desktop'],
    refs: [],
  },

  // ─────────────────────────── TRANSFER ───────────────────────────
  {
    key: 'TRANSFER-1', area: 'transfer', type: 'bug', impact: 'M', complexity: 'S', phase: 1,
    title: 'Import ignores bookTypeId validation — invalid type IDs are persisted',
    current: `\`ImportBookItem.BookTypeId\` has no \`[Range]\` (unlike \`UpdateBookRequest\`), and \`Import\` passes \`item.BookTypeId ?? 1\` straight to the repository. There are no FK constraints (CORE-6), so a file with \`"bookTypeId": 99\` stores 99 and the UI renders a fallback icon with no label.`,
    rec: `Validate against the known types (LIBRARY-7) and treat unknown types as 1, counting them as warnings in the import result.`,
    ac: ['Test: import with bookTypeId 99 → stored as 1, reported as warning'],
    refs: ['BookWheel/Models/ImportBooksRequest.cs', 'BookWheel/Controllers/BooksController.cs (Import)'],
  },
  {
    key: 'TRANSFER-2', area: 'transfer', type: 'bug', impact: 'M', complexity: 'S', phase: 1,
    title: 'Importing an export into a new account/instance resurrects removed books as active',
    current: `Export includes \`deletedAtUtc\` for soft-deleted books, but \`parseImportBooks\` (client) drops the field and \`ImportBookItem\` (server) doesn't model it. Within the same account the duplicate check hides this. Moving a library to a fresh instance (the main reason to export) puts every previously removed book back on the wheel.`,
    rec: `Carry \`deletedAtUtc\` through the parser and request model. On import, either skip deleted entries (default) or import them as deleted (when TRANSFER-5's restore mode is used).`,
    ac: ['Round-trip test: export with a deleted book → import into an empty account → book not active'],
    refs: ['BookWheel/wwwroot/js/app.js (parseImportBooks)', 'BookWheel/Models/ImportBooksRequest.cs'],
  },
  {
    key: 'TRANSFER-3', area: 'transfer', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Make import atomic, size-capped, and report per-row results',
    current: `Import calls \`AddAsync\` once per book. Each call opens a new DbContext and \`SaveChanges\`, so 1,000 rows means 1,000 round trips, and a failure halfway leaves a partial import. There's no cap on the number of books, and the result is just \`{added, skipped}\` with no indication of *which* rows were skipped or why.`,
    rec: `Insert in one DbContext and transaction (\`AddRange\`), cap at a configurable maximum (e.g. 5,000), and return \`rows: [{index, title, outcome: added|duplicate|invalid, reason}]\` for the UI to display.`,
    ac: ['All-or-nothing on DB failure', '413/400 above the cap', 'UI lists skipped rows with reasons'],
    refs: ['BookWheel/Controllers/BooksController.cs (Import)', 'BookWheel/Storage/Postgres/PostgresBookRepository.cs (AddAsync)'],
  },
  {
    key: 'TRANSFER-4', area: 'transfer', type: 'enhancement', impact: 'H', complexity: 'M', phase: 1,
    title: 'CSV import/export, including Goodreads and StoryGraph library exports',
    current: `Only BookWheel's own JSON format is supported. Most readers' TBR lists live in Goodreads or StoryGraph, which export CSV. #95 already recommends Goodreads CSV import as the realistic integration path (there's no Goodreads API).`,
    rec: `Detect the CSV dialect by header row (Goodreads: \`Exclusive Shelf\`, \`ISBN13\`, …; StoryGraph: \`Read Status\`, …; generic: title, author, isbn). Let the user choose which shelves to import (default: to-read), strip Goodreads' \`="…"\` ISBN wrapping, and reuse the TRANSFER-6 preview. Export to generic CSV too.`,
    ac: ['Goodreads export sample imports to-read shelf correctly', 'StoryGraph sample imports', 'Generic CSV round-trips', 'Preview before commit'],
    refs: ['Related: #95', 'IMPROVEMENT_ROADMAP.md Priority 3 item 6'],
  },
  {
    key: 'TRANSFER-5', area: 'transfer', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Full-fidelity personal backup and restore (books, history, status, tags)',
    current: `Export includes spin history, but import deliberately discards it (documented). "Export" therefore isn't a real backup: moving instances loses all stats. The only full backup path is an instance-level \`pg_dump\`, which a non-admin can't use.`,
    rec: `Add a "Restore from backup" mode, separate from "merge import", that targets an empty library: it preserves book IDs (or remaps them consistently), recreates spin history, deleted state, status, and tags, and validates \`schemaVersion\`. Keep merge import as the default.`,
    ac: ['Export → restore into an empty account yields identical stats', 'Refuses restore into a non-empty library unless confirmed', 'schemaVersion bump documented'],
    refs: ['BookWheel/Controllers/BooksController.cs (Export/Import)', 'README.md import notes'],
  },
  {
    key: 'TRANSFER-6', area: 'transfer', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Import preview (dry run) before committing',
    current: `Import commits immediately after a file is chosen. Users can't see what will be added or skipped, or catch a wrong file, before their wheel changes.`,
    rec: `Add \`POST /api/books/import?dryRun=true\` returning the TRANSFER-3 per-row results. The UI shows a table (new / duplicate / invalid) with "Import N books" and "Cancel".`,
    ac: ['Dry run has no side effects (test)', 'Preview is accessible (table with headers)'],
    refs: ['Depends on TRANSFER-3'],
  },

  // ───────────────────────────── PREFS ────────────────────────────
  {
    key: 'PREFS-1', area: 'prefs', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Persist language preference server-side so it follows the user across devices',
    current: `Theme, analytics consent, and provider are stored per user in Postgres, but language is localStorage-only. A Polish user on a new device gets the browser default, and the server can't localize emails (ACCOUNTS-3).`,
    rec: `Add \`locale\` to \`users\` and \`UserPreferences\`, saved when the language select changes while signed in and applied after login (server wins over localStorage, like theme).`,
    ac: ['Locale round-trips via /api/preferences', 'Applied on login on a fresh device', 'Validated against supported locales'],
    refs: ['BookWheel/Models/UpdatePreferencesRequest.cs', 'BookWheel/wwwroot/js/app.js (langSelect)'],
  },
  {
    key: 'PREFS-2', area: 'prefs', type: 'enhancement', impact: 'L', complexity: 'S', phase: 1,
    title: 'Add a "Follow system" theme option',
    current: `On first visit the theme is derived from \`prefers-color-scheme\` / \`prefers-contrast\`, but it's then written to localStorage and pinned. Users whose OS switches light/dark by time of day can't keep following it.`,
    rec: `Add \`system\` to the theme cycle and select (now the default), with a \`matchMedia\` change listener. Extend the server validation regex accordingly.`,
    ac: ['System theme tracks OS changes live', 'Explicit choice still persists'],
    refs: ['BookWheel/wwwroot/js/app.js (getPreferredTheme, applyTheme)', 'BookWheel/Models/UpdatePreferencesRequest.cs'],
  },
  {
    key: 'PREFS-3', area: 'prefs', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Move translations to per-locale files and set up native-speaker review',
    current: `All three catalogs live in one 1,010-line \`i18n.js\` that every visitor downloads. The README notes that the es/pl translations were machine-authored and never reviewed. Adding a language means editing three places (JS, resx, Program.cs).`,
    rec: `Split into \`/i18n/{locale}.json\` loaded on demand, add a CI check that every key exists in every locale (frontend and resx), drive \`supportedCultures\` from one list, and open the catalogs to community translation (e.g. Weblate or Crowdin free OSS tier).`,
    ac: ['Only the active locale is downloaded', 'CI fails on missing keys', 'Single source for the supported-locale list'],
    refs: ['BookWheel/wwwroot/js/i18n.js', 'BookWheel/Program.cs (supportedCultures)', 'BookWheel/Resources/SharedErrors*.resx'],
  },
  {
    key: 'PREFS-4', area: 'prefs', type: 'enhancement', impact: 'M', complexity: 'S', phase: 2,
    title: 'Make the Google Analytics opt-out discoverable and consistent (keep opt-out default)',
    current: `When a GA ID is configured, \`gtag.js\` loads on every page, including the login screen, and tracking is **on by default**. The only way to opt out is a checkbox buried in Settings → Preferences, and visitors aren't told analytics is running. Consent is stored in localStorage and synced to the server only after login, so a signed-in user's opt-out doesn't apply on the login screen of a new device.`,
    rec: `**Owner decision (2026-10-11): keep Google Analytics, and keep the opt-out model (tracking on by default), not opt-in.** Improve it:
- Show a small, dismissible first-visit notice ("This site uses Google Analytics to improve the app — Opt out") with a one-click opt-out.
- Add an "Analytics & privacy" link in the footer that reaches the same toggle without signing in.
- Honour the browser's Global Privacy Control signal (\`navigator.globalPrivacyControl\`) as an automatic opt-out.
- When signed in, apply the server-side preference before any GA hit.

_Note for phase 2:_ opt-out by default may not meet GDPR/ePrivacy prior-consent rules for EU visitors. Revisit this decision before a public launch.`,
    ac: ['First-visit notice shown once, localized and accessible, with one-click opt-out', 'Opt-out reachable from the footer while signed out', 'GPC signal disables GA (test)', 'Signed-in preference applied before the first GA hit'],
    refs: ['BookWheel/Services/GoogleAnalyticsHtmlInjector.cs', 'README.md Analytics section'],
  },

  // ────────────────────────────── PWA ─────────────────────────────
  {
    key: 'PWA-1', area: 'pwa', type: 'enhancement', impact: 'L', complexity: 'S', phase: 1,
    title: '"New version available" prompt instead of silent service-worker takeover',
    current: `\`sw.js\` calls \`skipWaiting()\` and \`clients.claim()\` immediately, so a deploy swaps the worker under an open tab. The page keeps the old \`app.js\` in memory while the API may have changed, and the user isn't told a refresh is needed.`,
    rec: `Wait in the \`waiting\` state, have the page show a toast ("Update available — Reload"), and on click post \`SKIP_WAITING\` and reload. Optionally show "What's new" (OPS-3) after the reload.`,
    ac: ['Toast appears after deploy without a manual refresh', 'Reload activates the new SW'],
    refs: ['BookWheel/wwwroot/sw.js'],
  },
  {
    key: 'PWA-2', area: 'pwa', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Offline read-only library and offline spin (queued history sync)',
    current: `The SW never caches \`/api/*\`, so offline users see the shell but no books and can't spin. That's the main reason to install the PWA, e.g. in a bookstore with bad signal. Full offline writes were deferred (#33). A read-only plus spin subset is much smaller.`,
    rec: `Cache the last \`GET /api/books\` response (stale-while-revalidate) and allow a client-side spin when offline. Queue the selection in IndexedDB and POST it to a new \`/api/books/spin-history\` endpoint with the original timestamp on reconnect. Edits stay online-only.`,
    ac: ['Offline: list and wheel render from cache with an "offline" badge', 'Offline spin recorded after reconnect with the original timestamp', 'Cache cleared on logout'],
    refs: ['BookWheel/wwwroot/sw.js', 'IMPROVEMENT_ROADMAP.md (deferred #33)'],
  },
  {
    key: 'PWA-3', area: 'pwa', type: 'enhancement', impact: 'L', complexity: 'S', phase: 2,
    title: 'Manifest shortcuts and share target ("Spin now", share a book link/ISBN into BookWheel)',
    current: `The manifest has icons only. Installed users can't long-press the icon to spin, and can't share a book page from a store or Goodreads into the app.`,
    rec: `Add \`shortcuts\` (Spin, Add book, Scan) that route via the existing \`?view=\` handling, and a \`share_target\` (GET) that opens Book Search pre-filled with the shared title, URL, or ISBN.`,
    ac: ['Shortcuts work on Android/Chrome desktop', 'Shared text with an ISBN triggers a lookup'],
    refs: ['BookWheel/wwwroot/manifest.webmanifest', 'BookWheel/wwwroot/js/app.js (applyRequestedView)'],
  },

  // ────────────────────────────── OPS ─────────────────────────────
  {
    key: 'OPS-1', area: 'ops', type: 'bug', security: true, advisory: true, impact: 'H', complexity: 'S', phase: 1,
    title: 'Security: reverse-proxy deployment hardening (private advisory)',
    current: 'Tracked as a private GitHub Security Advisory until fixed. Full write-up in the gitignored advisories.local.mjs.',
    rec: 'See the advisory.',
    ac: ['Advisory resolved and published'],
    refs: [],
  },
  {
    key: 'OPS-2', area: 'ops', type: 'enhancement', impact: 'H', complexity: 'S', phase: 1,
    title: 'Self-hosting kit: automated Postgres backups and an app healthcheck in docker-compose',
    current: `Backups are documented ("use pg_dump on the schedule your data warrants") but not automated, so most self-hosters will never set them up. The \`bookwheel\` service has no compose \`healthcheck\` (only postgres does), so \`restart: unless-stopped\` can't detect a hung app and orchestrators like Portainer show it as healthy.`,
    rec: `Add an optional \`backup\` service (e.g. a \`prodrigestivill/postgres-backup-local\`-style image or a small cron + pg_dump container) with daily, weekly, and monthly retention to a named volume or bind mount. Add a \`healthcheck\` hitting \`/health/ready\`, and a tested \`scripts/restore.sh\`. Document the restore drill.`,
    ac: ['`docker compose up` produces a dump within 24h by default (or documented opt-in)', 'Restore script tested in CI against a dump', 'App container reports health'],
    refs: ['docker-compose.yml', 'README.md backup section'],
  },
  {
    key: 'OPS-3', area: 'ops', type: 'enhancement', impact: 'L', complexity: 'S', phase: 2,
    title: 'In-app "What\'s new" after upgrades',
    current: `The footer shows the version, but users and admins aren't told what changed after an upgrade. Release notes exist only on GitHub.`,
    rec: `Ship a small \`changelog.json\` (version → user-facing bullet points) and show a dismissible "What's new in vX" dialog once per user after the version changes (remembered in localStorage).`,
    ac: ['Shown once per version', 'Localized bullets (at least en)'],
    refs: ['BookWheel/BookWheel.csproj (InformationalVersion)'],
  },
  {
    key: 'OPS-4', area: 'ops', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Standard metrics export (OpenTelemetry/Prometheus) replacing in-memory counters',
    current: `\`/api/metrics\` returns in-memory counters that reset on every restart, are only reachable with an admin cookie, and can't be scraped by Prometheus, Grafana, or Uptime Kuma. Custom log shipping duplicates what OTel exporters provide.`,
    rec: `Add OpenTelemetry metrics and traces (ASP.NET Core, HttpClient, and Npgsql instrumentation plus custom counters for spins, logins, and lookups) with an optional Prometheus \`/metrics\` endpoint protected by a token or bound to an internal port. Keep \`/api/metrics\` as a thin compatibility view, and consider deprecating the custom \`LogShippingService\` in favour of the OTLP log exporter.`,
    ac: ['Prometheus scrape works with token', 'Sample Grafana dashboard JSON in docs', 'No PII in metric labels'],
    refs: ['BookWheel/Services/AppMetricsService.cs', 'BookWheel/Services/LogShippingService.cs'],
  },

  // ───────────────────────────── CORE ─────────────────────────────
  {
    key: 'CORE-1', area: 'core', type: 'enhancement', security: true, impact: 'H', complexity: 'M', phase: 1,
    title: 'Adopt ASP.NET Core authentication/authorization middleware instead of per-action session checks',
    current: `Every action starts with \`var user = _authService.GetAuthenticatedUser(HttpContext); if (user is null) return Unauthorized();\`, and admin endpoints add \`if (!user.IsAdmin) return 403\`. That's 22 copies across 7 controllers. One forgotten check is an auth bypass, \`[Authorize]\` tooling and OpenAPI security metadata can't see any of it, and per-request concerns (role and account-state checks) can't be centralized.`,
    rec: `Implement a custom \`AuthenticationHandler\` (or ASP.NET Core cookie auth with a \`ITicketStore\` backed by CORE-2) that populates \`HttpContext.User\` with claims. Add policies \`RequireUser\` (fallback policy) and \`RequireAdmin\`, mark anonymous endpoints with \`[AllowAnonymous]\`, and expose a \`CurrentUser\` accessor. Migrate controllers one at a time with existing tests as the safety net.`,
    ac: ['Fallback policy requires authentication by default', 'No manual GetAuthenticatedUser checks remain in controllers', 'Test asserting every endpoint is either [AllowAnonymous] or authorized', 'Role and disabled changes take effect on the next request'],
    refs: ['BookWheel/Controllers/*.cs', 'BookWheel/Services/AuthService.cs'],
  },
  {
    key: 'CORE-2', area: 'core', type: 'enhancement', impact: 'H', complexity: 'M', phase: 1,
    title: 'Persistent, revocable sessions (survive restarts; ready for multiple instances)',
    current: `Sessions, failed-login counters, and reset rate-limit windows live in \`ConcurrentDictionary\`s inside the singleton \`AuthService\`. Every container restart or upgrade logs out every user and resets lockout state, and the app can't run more than one replica. That's a hard blocker for zero-downtime deploys and for SaaS.`,
    rec: `Store sessions in a \`sessions\` table (hashed token, user ID, created, last seen, expires, user agent, IP) with a small in-process cache, and a background job that purges expired rows. Move lockout counters to the DB (or to \`IDistributedCache\` with a Postgres or Redis provider). This underpins AUTH-2 and AUTH-5.`,
    ac: ['Users stay signed in across an app restart (test)', 'Token stored hashed', 'Lockout persists across restart', 'Expired sessions purged'],
    refs: ['BookWheel/Services/AuthService.cs'],
  },
  {
    key: 'CORE-3', area: 'core', type: 'enhancement', security: true, impact: 'M', complexity: 'S', phase: 1,
    title: 'Add a Content-Security-Policy and Permissions-Policy',
    current: `Security headers cover \`nosniff\`, \`X-Frame-Options\`, and \`Referrer-Policy\` (GH #145), but there's no CSP. The UI builds markup with \`innerHTML\` in places (e.g. the empty state and user rows), and cover images load from arbitrary user-supplied URLs. There's also no \`Permissions-Policy\` restricting camera access to self.`,
    rec: `Ship a CSP: \`default-src 'self'; img-src 'self' https://covers.openlibrary.org https://books.google.com data:; script-src 'self' 'nonce-…'\` (nonce applied by the existing HTML injector for gtag), \`connect-src 'self'\` plus analytics, \`frame-ancestors 'none'\`. Start in Report-Only for one release. Add \`Permissions-Policy: camera=(self), microphone=(), geolocation=()\`.`,
    ac: ['CSP enforced with no console violations in E2E', 'GA still works when configured (nonce)', 'Permissions-Policy present'],
    refs: ['BookWheel/Program.cs (security header middleware)', 'BookWheel/Services/GoogleAnalyticsHtmlInjector.cs'],
  },
  {
    key: 'CORE-4', area: 'core', type: 'enhancement', impact: 'H', complexity: 'L', phase: 1,
    title: 'Modularize the 3,200-line app.js into ES modules with unit tests and linting',
    current: `The whole SPA is one script: about 185 \`getElementById\` lookups, global mutable state (\`activeBooks\`, \`wheelBooks\`, \`currentUser\`, …), and every feature (auth, wheel, library, scanner, stats, admin, settings, Book Search) interleaved. Static-content tests assert on source text rather than behaviour, and there's no JS linting or unit testing. Every adjacent feature in this audit lands in this file, so it's the main sustainability risk.`,
    rec: `Keep vanilla JS with no framework, but split into native ES modules (\`api.js\`, \`state.js\`, \`wheel.js\`, \`library.js\`, \`scanner.js\`, \`stats.js\`, \`admin.js\`, \`settings.js\`, \`book-search.js\`, \`dialogs.js\`) loaded via \`<script type="module">\`. Add ESLint and Vitest (jsdom) for pure logic (wheel angle math, import parsing, filtering). Migrate incrementally, one feature per PR, and keep the minification step from #150 working.`,
    ac: ['No module over ~500 lines', 'ESLint in CI', 'Vitest covering wheel maths, import parsing, i18n plural rules', 'SW precache list updated for module files'],
    refs: ['BookWheel/wwwroot/js/app.js', 'BookWheel.Tests/BookWheelStaticContentTests.cs'],
  },
  {
    key: 'CORE-5', area: 'core', type: 'enhancement', impact: 'H', complexity: 'M', phase: 1,
    title: 'Real browser end-to-end tests (Playwright) with axe accessibility checks',
    current: `The .NET suite is strong for APIs (Testcontainers, 400+ tests), but the frontend tests check served HTML and JS *text*. No test clicks Spin, adds a book, or opens a dialog in a real browser, so UI regressions (focus traps, dialog flows, scanner fallbacks, theme rendering) reach users. The a11y audit was a one-off manual pass.`,
    rec: `Add a Playwright project (TS or .NET) that runs against the docker-compose stack in CI. Cover setup → add book → spin → stats, the import/export round trip, the admin create user → reset link flow, offline shell, and three themes, each with an \`@axe-core/playwright\` scan that fails on serious or critical violations.`,
    ac: ['CI job runs E2E on PRs (Chromium; WebKit nightly)', 'axe gate on key screens', 'Traces and screenshots uploaded on failure'],
    refs: ['.github/workflows/ci.yml', 'docs/audits/testing.md', 'docs/audits/a11y.md'],
  },
  {
    key: 'CORE-6', area: 'core', type: 'enhancement', impact: 'M', complexity: 'M', phase: 1,
    title: 'Add foreign-key constraints now that legacy JSON storage is gone',
    current: `By convention there are no FK constraints: \`books.user_id\`, \`books.book_type_id\`, \`books.book_info_provider_id\`, and \`spin_selections.user_id/book_id\` are bare columns, so integrity depends on every code path validating IDs. TRANSFER-1 is already one gap. The original reason (simpler migrations alongside JSON storage) no longer applies.`,
    rec: `Write a migration that first cleans orphans (logging counts), then adds FKs with \`ON DELETE RESTRICT\` for reference tables and \`CASCADE\` for user-owned rows (books and spin selections on user delete), and simplify \`DeleteUserWithDataAsync\` accordingly. Update the project conventions in CLAUDE.md and memory.`,
    ac: ['Orphan cleanup is idempotent and reported', 'FKs present (test via information_schema)', 'User deletion still removes all data'],
    refs: ['BookWheel/Storage/Postgres/BookWheelDbContext.cs', 'BookWheel/Services/UserManagementService.cs'],
  },
  {
    key: 'CORE-7', area: 'core', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Retire legacy JSON storage, startup data migration, and /api/system/migrations',
    current: `PostgreSQL has been the only store since 2.x, yet \`JsonBookRepository\` (394 lines), \`JsonCredentialRepository\` (679), \`JsonPasswordResetTokenRepository\`, \`DataMigrationService\` (run on **every** startup), \`PostgresMigrationService\`, and \`MigrationController\` are still registered. \`/api/system/migrations/*\` is reachable **anonymously** when no account exists yet, and about 600 lines of tests maintain dead code paths.`,
    rec: `Announce removal in the next minor release notes (with "upgrade to vX first if you're still on JSON"), then delete the JSON repositories, migration services, controller, and \`--migrate-*\` CLI flags, keeping a one-time detection that logs a clear error if legacy files are found without a database.`,
    ac: ['Legacy classes and tests removed', 'Startup no longer touches App_Data JSON', 'Upgrade note in README/CHANGELOG'],
    refs: ['BookWheel/Storage/Json*.cs', 'BookWheel/Services/DataMigrationService.cs', 'BookWheel/Controllers/MigrationController.cs', 'BookWheel/Program.cs'],
  },
  {
    key: 'CORE-8', area: 'core', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Consistent API error contract (ProblemDetails + error codes) and an OpenAPI document',
    current: `Errors come back in three shapes: \`{message}\` (most controllers), \`ValidationProblemDetails\` (model binding), and bare status codes (401/403 with no body). Server messages are localized by looking up the *English sentence* as the resx key, so rewording a message silently breaks translations. There's no OpenAPI spec for integrators or future mobile clients.`,
    rec: `Return RFC 9457 ProblemDetails everywhere with a stable \`code\` (e.g. \`book.title_required\`) plus a localized \`detail\`. Switch resx keys to codes, generate \`/openapi/v1.json\` with \`Microsoft.AspNetCore.OpenApi\`, and update \`requestJson\` to read \`detail\`.`,
    ac: ['All 4xx/5xx bodies are ProblemDetails', 'resx keyed by code', 'OpenAPI document published in CI artifact'],
    refs: ['BookWheel/Services/ApiMessageLocalizer.cs', 'BookWheel/wwwroot/js/app.js (requestJson)'],
  },
  {
    key: 'CORE-9', area: 'core', type: 'enhancement', impact: 'L', complexity: 'S', phase: 1,
    title: 'Trim duplicated API payloads (GET /api/books, POST /spin)',
    current: `\`GET /api/books\` returns the same list twice (\`books\` and \`activeBooks\`), doubling the payload, and \`POST /api/books/spin\` re-queries and returns the full list after every spin even though nothing changed.`,
    rec: `Return \`{ books }\` only (the client already falls back), and have spin return \`{ selected }\`. Keep \`activeBooks\` for one release behind a deprecation note if external callers exist.`,
    ac: ['Payload halved (measure in PR)', 'Spin no longer re-reads the book list'],
    refs: ['BookWheel/Controllers/BooksController.cs (GetAll, Spin)'],
  },
  {
    key: 'CORE-16', area: 'core', type: 'enhancement', impact: 'H', complexity: 'L', phase: 1,
    title: 'Master library: shared edition catalog + per-user library entries with multiple formats',
    current: `Each \`books\` row mixes public book data (title, author, ISBN, cover) with personal data (owner, type, scanner flag, deleted state), and the row *is* the wheel entry. As a result:
- The same book owned in two formats has to be two rows. A paperback and an ebook (different ISBNs) are unrelated.
- Fixing a title or cover is per row; nothing is shared or reusable.
- Every user's lookup and cover fetch for a popular ISBN is repeated.
- Wheels (CORE-10) would have nothing stable to reference.`,
    rec: `Split the model into three layers (design: \`docs/audits/feature-audit-2026-10/design-master-library.md\`).

1. **Shared edition catalog (instance-wide, public bibliographic data only):**
   - \`catalog_editions\`: one row per ISBN-13 (unique; ISBN-10 normalized) with title, author, cover (LIBRARY-6), page count, provider, fetched_at.
   - Store the Open Library work key when present, unused for now, so a work level can be added later without re-fetching.
   - Lookup and scan results upsert here; the lookup cache (LOOKUP-2) and stored covers (LIBRARY-6) attach here.
2. **Library entries (per user, the master list):**
   - \`library_entries\`: user, display title/author/cover (seeded from the first edition, overridable), status (LIBRARY-3), tags (LIBRARY-5), notes, added_at, removed_at.
   - \`entry_formats\`: entry, book type (Physical/Digital/Nook Only/admin types per LIBRARY-7), optional edition. **One entry, many formats.**
3. **Wheels** reference library entries (CORE-10).

**Grouping is done by the user, not by matching algorithms (owner decision, 2026-10-11).** There's no work table, no automatic merging and no admin merge tool. When a scanned or looked-up edition's normalized title and author resemble an entry already in the user's library, the app asks *"Is this another format of X?"*
- **Yes:** add a format to that entry.
- **No:** create a new entry.

Users can also move a format to a different entry, or split it out, from the edit dialog. Wrong merges can't happen without the user choosing them.

**Owner decisions (2026-10-11):**
- Several editions/versions of the same book → **one library entry with many formats** (user-confirmed).
- **Deleting is user-scoped only.** Removing a book removes the user's library entry (soft, restorable). Catalog editions are never deleted by users.
- Catalog corrections (bad title or cover) are **admin-only** and benefit everyone. Users who disagree set a **personal override** on their own entry, visible only to them.
- Removed books can be re-added at any time (restores the entry and its history; LIBRARY-2).

**Migration (non-breaking, staged):**
- (a) Create \`catalog_editions\` and backfill from existing books that have ISBNs. Books without an ISBN simply have formats with no edition.
- (b) Create **one library entry per existing book row**, with no automatic merging. Optionally show a one-time "possible duplicates" review where the user can combine entries.
- (c) Re-point \`spin_selections\` to entries.
- (d) Keep \`/api/books\` as a compatibility view for one release.`,
    ac: ['Design doc reviewed before implementation', 'Backfill is idempotent and never merges entries automatically', 'Formats are a child list of an entry', 'A title/author match prompts "another format of X?" with yes/no', 'A format can be moved to another entry or split out', 'User deletion never removes catalog rows', 'Admin can correct catalog editions; users can only override their own entry', 'Personal overrides never leak to other users', 'Open Library work key stored on editions when available', 'Export/import round-trips entries, formats and overrides',
      'Duplicate prevention: unique ISBN-13 on catalog_editions with normalized upsert (ON CONFLICT)', 'Duplicate prevention: unique (user, edition) across entry_formats, including removed entries; re-adding restores', 'Duplicate prevention: unique (wheel, entry) on wheel_items', 'Duplicate prevention: ISBN-less books never create catalog rows', 'Indexed match key (normalized title + author surname) on library entries', '"Not a duplicate" answers remembered per pair', 'Possible-duplicates review with a per-user, undoable Merge (moves formats, wheels, tags, history)'],
    refs: ['BookWheel/Storage/Postgres/Entities/BookEntity.cs', 'BookWheel/Storage/Postgres/BookWheelDbContext.cs', 'Enables CORE-10, LIBRARY-6, LOOKUP-2'],
  },
  {
    key: 'CORE-10', area: 'core', type: 'enhancement', impact: 'H', complexity: 'L', phase: 2,
    title: 'Multiple wheels over the master library: manual and smart (rule-based) wheels',
    current: `Each user has exactly one implicit wheel: all their active books. Users who want "Book club", "Kids' bedtime", and "Summer reads" can't have them, and keeping any list current is manual work. #58 (a wheel of questions) can't be built without duplicating the whole stack.`,
    rec: `Build on the master library (CORE-16): wheels reference the user's **library entries**, never copies of books. See \`docs/audits/feature-audit-2026-10/design-master-library.md\`.

**Data:** \`wheels\` (id, owner, name, kind = manual | smart | custom, rules JSON, settings JSON); \`wheel_items\` (wheel, library_entry, added_at, pinned, excluded) for manual membership and per-wheel overrides.

**Owner decisions (2026-10-11):**
- A library entry can be on **any number of wheels** at once. Editing it updates it everywhere.
- Smart-wheel behaviour is **entirely user-controlled**. Each user writes their own rules (e.g. \`status = to_read AND tag = sci-fi\`); nothing is hard-wired, and one user's rules never affect another user.
- A book that drops off a wheel can be **re-added at any time**: change its status back (e.g. "Read again"), or **pin** it to the wheel, which overrides the rules. Users can also **exclude** a book from a smart wheel without changing its status.
- "Remove from wheel" only removes membership. "Remove from library" is user-scoped and restorable (LIBRARY-2). Neither ever touches the shared catalog.

**UX:** a wheel switcher in the header; "Add to wheels…" checkboxes on any library entry; a rule builder (status, format, tags, page count, date added); and a live count of matching books while editing rules. Spin, stats, filters and weights are per wheel.

**Migration:** each user gets a default **smart** wheel, "My to-read list" (rule: status = to_read), which reproduces today's behaviour exactly. A \`custom\` kind with plain-text items covers #58.`,
    ac: ['Existing users land on a default smart wheel with no visible change', 'Create, rename, and delete manual and smart wheels', 'An entry can be on any number of wheels', 'Smart rules are per user and per wheel; nothing is applied globally', 'Pin (force include) and exclude overrides per wheel', 'A dropped book can be re-added at any time (status change or pin)', 'Remove-from-wheel, remove-from-library and catalog data are independent', 'Spin history records the wheel', 'Stats per wheel'],
    refs: ['Depends on CORE-16', 'Related: #58', 'BookWheel/Storage/Postgres/BookWheelDbContext.cs'],
  },
  {
    key: 'CORE-11', area: 'core', type: 'enhancement', impact: 'M', complexity: 'L', phase: 2,
    title: 'Sharing: household and book-club wheels with viewer/spinner/editor roles',
    current: `Libraries are strictly per user. A family or book club on one instance can't share a list or spin together. Admins can see all users but not collaborate.`,
    rec: `After CORE-10, add \`wheel_members\` (wheel, user, role). Shared wheels appear in each member's switcher, spins are recorded with the spinning user, and invites go by username (instance) or email link (public). Consider a public read-only link for a book club's current wheel.`,
    ac: ['Role enforcement tested per endpoint', 'Owner can revoke', 'Stats attribute spins per member'],
    refs: ['Depends on CORE-10'],
  },
  {
    key: 'CORE-12', area: 'core', type: 'enhancement', impact: 'H', complexity: 'M', phase: 2,
    title: 'Configurable public self-registration (closed / invite-only / open) with email verification',
    current: `Accounts can only be created by the first-run setup or by an admin. A public face (#46) needs visitors to be able to sign up, and an open instance needs abuse controls.`,
    rec: `Add \`Registration:Mode\` = closed (default, current behaviour) | invite (admin-issued codes or links) | open. For open: email verification required before first login, CAPTCHA (Turnstile or hCaptcha, configurable), per-IP signup limit, and no admin role ever granted. Add a sign-up form on the login card when enabled.`,
    ac: ['Default remains closed (no behaviour change for self-hosters)', 'Unverified accounts can\'t log in', 'Signup rate-limited and CAPTCHA-protected when open'],
    refs: ['Related: #46', 'BookWheel/Controllers/AuthController.cs'],
  },
  {
    key: 'CORE-13', area: 'core', type: 'enhancement', impact: 'M', complexity: 'M', phase: 2,
    title: 'Self-service data rights: download all my data and delete my account',
    current: `Users can export books and history, but not their account data, and only an admin can delete an account. A public or SaaS service in GDPR/CCPA reach needs self-service access and erasure.`,
    rec: `In Settings → Account: "Download my data" (account, preferences, books, history, audit events about me as JSON) and "Delete my account" (password re-entry, 7-day grace period with email confirmation, then hard delete through the existing transactional delete). The first or last admin can't self-delete.`,
    ac: ['Complete data export', 'Deletion grace period and cancellation', 'Last-admin guard'],
    refs: ['BookWheel/Services/UserManagementService.cs', 'Related: #47'],
  },
  {
    key: 'CORE-14', area: 'core', type: 'enhancement', impact: 'M', complexity: 'L', phase: 3,
    title: 'Plan entitlements and usage limits groundwork (prerequisite for payments)',
    current: `There's no notion of plan, quota, or feature gating. #48 (payments) and #45 (ads/affiliate) will need limits such as wheels per user, books per wheel, shared members, and an ad-free flag, and these are cheapest to add before features multiply.`,
    rec: `Add a \`plans\` table with an \`entitlements\` JSON, \`users.plan_id\`, and one \`IEntitlementService.Check(user, feature, currentUsage)\` used at the enforcement points (add book, create wheel, invite member). Self-hosted defaults to an "unlimited" plan. Billing integration (Stripe) stays in #48.`,
    ac: ['Unlimited by default on self-host', 'Limits enforced server-side with friendly errors', 'Single entitlement check API'],
    refs: ['Related: #48, #45'],
  },
  {
    key: 'CORE-15', area: 'core', type: 'enhancement', impact: 'M', complexity: 'S', phase: 1,
    title: 'Split the 47 KB README into user, admin, and self-hosting guides',
    current: `README.md is 47 KB and mixes feature changelog bullets (with GH numbers), user docs, deployment, CI, versioning, and storage migration history. That's hard for a new self-hoster or contributor to navigate, and it's the first impression for a public launch.`,
    rec: `Keep the README to pitch, screenshots, quick start (docker compose), and links. Move content into \`docs/\`: self-hosting (env reference, reverse proxy, backups (OPS-2), upgrades), admin guide, user guide, contributing and architecture, and a CHANGELOG.md for the GH-numbered feature history. Optionally publish with GitHub Pages.`,
    ac: ['README < 8 KB', 'Env var reference table in one place', 'CHANGELOG.md introduced'],
    refs: ['README.md'],
  },
];

const IMPACT = { H: 3, M: 2, L: 1 };
const COMPLEXITY = { S: 1, M: 2, L: 3 };

export function tierOf(issue) {
  const score = IMPACT[issue.impact] * 2 - COMPLEXITY[issue.complexity];
  if (score >= 5) return 1;
  if (score === 4) return 2;
  if (score === 3) return 3;
  if (score === 2) return 4;
  return 5;
}

export function sortByPriority(list) {
  return [...list].sort((a, b) =>
    tierOf(a) - tierOf(b)
    || a.phase - b.phase
    || (a.type === 'bug' ? 0 : 1) - (b.type === 'bug' ? 0 : 1)
    || IMPACT[b.impact] - IMPACT[a.impact]
    || a.key.localeCompare(b.key));
}
