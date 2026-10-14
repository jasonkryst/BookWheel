# User Guide

What Book Wheel does: managing your book list, spinning the wheel, and the
preferences available to any signed-in user. For running an instance for
other people, see the [Admin Guide](admin-guide.md) and
[Self-Hosting Guide](self-hosting.md).

## Features

- First-run account creation plus cookie-based login/logout
- First created account is automatically assigned administrator role
- Administrator-only user management uses separate creation and account-management tabs, with searchable status filters and at-a-glance account-state indicators; each entry shows its last-login date (or "Never logged in") and the directory can be sorted by it, and the remove-account confirmation dialog states how many books will be removed before an admin confirms
- New-user onboarding uses admin-shared setup links instead of admin-supplied passwords
- Signed-in users can change their own password (`POST /api/auth/password`, requires the current password; rate-limited to 5 attempts/minute per user); the current device stays signed in and all of the user's other sessions are signed out
- Spin wheel renders crisply on HiDPI/retina displays, with arc-sized, contrast-checked slice labels that drop out gracefully on crowded wheels
- Spin celebration effects: a synthesized slice-tick sound as the wheel decelerates, a short vibration pattern on stop (where `navigator.vibrate` is supported), and a confetti burst, toggleable in Settings → Preferences (default on) and always off under `prefers-reduced-motion` regardless of the toggle. The spin result is revealed on the wheel's `transitionend` event rather than a fixed timer, so it can't desync from the animation
- Library search, filter, sort, and configurable page size for the active book list: search by title/author/ISBN (debounced), filter by book type, sort (title, author, date added, last spun; default Title A-Z), and show 10/25/50/All books per page. Counts and pagination reflect the filtered view, and the choices are persisted per browser in `localStorage`
- "Recent picks" panel under the wheel shows **your own** last 10 spins with localized relative dates (the last pick survives reload); the panel and last-spin line are scoped to the signed-in user and are cleared on sign-out and refreshed on sign-in, so a shared device never shows another account's picks. Stats includes the full spin history paged server-side (`GET /api/books/spin-history?page=&pageSize=`)
- Administrator-generated password reset links (24-hour expiry) instead of direct password setting
- Email-based account recovery: users can request a password reset link (`POST /api/auth/password-reset/request`, by username) or a reminder of their username (`POST /api/auth/forgot-username`, by email) from the login screen; both always return the same generic confirmation regardless of whether the account/email exists, to prevent account enumeration
- Book Search (🔍 in the header): scan a barcode or enter an ISBN or title to look up a book, see its cover/author/ISBN, and open it directly on configured retailer sites (all links open in a new tab). The retailer list is operator-configurable (see the [Self-Hosting Guide](self-hosting.md#retailer-links)) and locale-aware by default: Amazon resolves to the regional domain (`.com`/`.es`/`.pl`) for the signed-in language, and Barnes & Noble (English) swaps for Bookshop.org (Spanish/Polish). A disclosure note appears below the links only if the operator has configured an affiliate tag on one of them — none are configured by default. Multi-result title searches show a picker; an "Add to Wheel" button pre-fills the add form from the result
- User management tab is visible only to administrators, inside the consolidated Settings dialog
- Trash / "Recently removed": removing a book soft-deletes it; restore it from the Recently removed list in the book list
- Duplicate-add warnings: adding a book already on your wheel (or one you removed earlier) warns you and offers "Add anyway" or "Restore" instead of silently creating a copy
- Book metadata lookups are cached server-side (7 days for hits, 1 hour for misses) and rate-limited per user session
- Add, edit, and remove books
- Optional ISBN tagging when adding or editing a book, with a Lookup action that queries the Open Library API to auto-fill author and cover art
- Title-only Lookup queries Open Library's general search (not a strict title-field match), so titles that aren't indexed verbatim (subtitles, alternate editions) still surface results; the "Choose a match" picker and the spin-result highlight/toast derive their accent color from the active theme
- Book collections are scoped per user account
- Interactive spin wheel UI
- Spin selection does not remove the selected book
- Removing a book soft-deletes it (excluded from the active list, spins, and total book count) rather than permanently erasing it; removing an entire user account still purges that user's books outright, including any already soft-deleted
- Every spin selection is recorded to a spin-history log (user, book, and timestamp), retrievable via `GET /api/books/spin-history`
- "Last selected" message displayed below the wheel, including the book's cover image and author when that metadata is available
- Active books list with pagination after 10 books
- Book count plus page status summary in the books panel
- Delete confirmation modal for book removal
- Login form reset on logout so credentials are not left in the UI
- Wheel entropy shuffle when adding books
- Import/export JSON file upload/download tabs, inside the consolidated Settings dialog
- First-time empty-state guidance when no books are present
- Single consolidated Settings dialog with Manage users (admin-only), Import/Export, and Preferences (theme switcher + language selector) tabs
- Installable Progressive Web App with an offline-capable app shell (manifest, service worker, offline fallback page)
- Mobile and tablet layouts keep the wheel and Settings dialog within the viewport without horizontal scrolling or browser zoom, including the Import tab's native file picker
- Barcode scanner for ISBN lookup: tap the camera (📷) button next to the ISBN field on the add-book row or the edit dialog to scan a book's barcode with the device camera; on detection the ISBN field is populated and the Open Library lookup fires automatically; the scanner supports camera flipping (front/rear) and shows a framing reticle; requires Chrome, Edge, or Android Chrome (uses the native Barcode Detection API — browsers that do not support it show a clear message instead of an error); books added via the scanner store an `AddedByScanner` flag in the database and display a 📷 badge in the book list for easy identification
- Book type classification: each book has a type — Physical 📖, Digital 📱, or Nook Only 🔖 — selected from a dropdown when adding or editing; the selected type icon appears in the book list alongside the title; type is included in the JSON export and respected on import
- Book info providers: book lookups can be served by Open Library or Google Books, selectable per-user from Settings → Preferences ("Book info source"); if the preferred provider has no match, the other provider is tried automatically before giving up. Each book records which provider actually supplied its data (null for manually-entered books, or books added before this feature)
- Spin Wheel Stats 📊: a Stats button in the header opens a modal showing per-user spin history analytics — total spins, unique books spun, never-spun count, longest and shortest time a book has been on the wheel, and a canvas bar chart plus ranked table of top-selected books by spin count; administrators additionally see a cross-user aggregate view with total spins across all users, total user count, active user count (not disabled/locked, with a login or spin in the last 30 days), and top users by spin count; a "Book Lists" tab shows the full list of unique-spun books (sorted by spin count) and all never-spun active books (sorted alphabetically)
- Session lifetime: sessions use a sliding idle timeout (default 8 hours) plus an absolute cap (default 24 hours). A user who is inactive for the idle window is signed out; an actively used session is renewed but still ends once the absolute cap is reached, regardless of activity

See [CHANGELOG.md](../CHANGELOG.md) for when each feature shipped and its GitHub issue reference.

## Internationalization

Book Wheel supports English, Spanish, and Polish.

- A gear-icon Settings button opens the consolidated Settings dialog; its Preferences tab holds the theme switcher and a language dropdown. The button is positioned inline in the toolbar when logged in, and in the corner of the login/setup card when logged out. When logged out, or when the signed-in user isn't an administrator, the Manage users and/or Import/Export tabs are hidden and Preferences is the only tab shown.
- On first visit, the language defaults to the browser's language if it's one of the supported three, otherwise English; the choice is cached in the browser's `localStorage` for logged-out use, but for a signed-in user it is also saved server-side (alongside theme) and takes precedence on login, so it follows that user to a new device.
- The frontend sends the selected language as an `Accept-Language` header on every API call, so server-generated error messages (e.g. "Book title is required.") come back already translated.
- Frontend strings live in `BookWheel/wwwroot/js/i18n.js`; backend error-message translations live in `BookWheel/Resources/SharedErrors*.resx`, looked up through `ApiMessageLocalizer`.

### Adding a new language

1. Add the locale code to `SUPPORTED_LOCALES` in `BookWheel/wwwroot/js/i18n.js` and add a full `TRANSLATIONS.<locale>` catalog (copy the `en` object as a starting point — every key must be present or that string falls back to English).
2. Add a `BookWheel/Resources/SharedErrors.<locale>.resx` file with all of the same resource keys as `SharedErrors.resx`.
3. Add the locale code to the `supportedCultures` array in `BookWheel/Program.cs`'s `RequestLocalizationOptions` setup.

Spanish and Polish translations were authored by the assistant as a first pass and have not been reviewed by native speakers — treat them as a solid starting point, not final copy.

## Analytics

BookWheel includes an optional Google Analytics (gtag.js) integration, configured by the operator via `Analytics:GoogleAnalyticsId` (see the [environment variable reference](self-hosting.md#environment-variable-reference)). **The shipped default is blank, so no analytics script loads and no telemetry is sent unless the operator explicitly sets a GA4 property ID.**

When a non-empty ID is configured, the script loads on every page (including the pre-login screen) and tracking is **on by default**. Users can opt out at any time via the "Allow anonymous usage analytics" checkbox in Settings → Preferences; the choice is stored in the browser's `localStorage` (`bookwheel-analytics-consent`) and takes effect immediately via Google's documented `window['ga-disable-<id>']` flag, without needing a page reload.

## Progressive Web App

Book Wheel can be installed as a standalone app (desktop Chrome/Edge, Android, and — with reduced polish — iOS Safari) and its UI shell keeps working when the network drops.

- A web app manifest (`BookWheel/wwwroot/manifest.webmanifest`) drives the install prompt/icon.
- Installed-app shortcuts (long-press the icon on Android, right-click on Chrome desktop) for Spin Wheel, Add a Book, Scan a Book, Books, and Stats, all routed through the existing `?view=` query handling rather than dedicated pages. A `share_target` (`GET`, `/?view=search`) lets the OS share sheet send a title/text/URL from another app (e.g. a store listing or Goodreads) straight into the Book Search dialog; a 978/979-prefixed ISBN-13 anywhere in the shared text, URL, or title auto-triggers the lookup, and a bare ISBN-10 does too when the literal word "ISBN" precedes it — otherwise the shared title just pre-fills the search field for the user to confirm.
- A service worker precaches the HTML/CSS/JS/icon app shell on install, so the UI keeps loading while offline.
- The service worker never intercepts `/api/*` requests — login, book data, and spin results always require a live connection. A toast notifies the user when the browser goes offline or comes back online.
- A new service worker waits in the `waiting` state instead of taking over an open tab immediately after a deploy; the page shows an "Update available" toast with a Reload action, which tells the waiting worker to activate and then reloads itself once it does.
- An offline fallback page is shown only if a user's very first visit happens while offline (rare, since the shell is precached on install).
- **Not implemented:** full offline data support (queuing book edits made while offline and syncing them on reconnect). That needs a durable write queue, conflict resolution for concurrent edits, and offline-aware session handling — evaluated for #33 and scoped out as a separate future project (see `IMPROVEMENT_ROADMAP.md`).

## Theme Toggle

The application includes a theme control (inside the Settings dialog's Preferences tab, opened via the gear-icon button) that cycles through **Follow system**, dark, light, and high-contrast modes.

- Theme choice is persisted in browser `localStorage` under `bookwheel-theme` as `system`, `dark`, `light`, or `high-contrast`.
- **Follow system** (the default for a user who hasn't made an explicit choice) tracks the OS color-scheme (`prefers-color-scheme`) and contrast (`prefers-contrast: more`) preferences live via `matchMedia` change listeners — including while the app stays open, not just on next load — without pinning whatever it last resolved to. Choosing dark/light/high-contrast explicitly persists that exact choice instead, and keeps it even if the OS preference changes.
- The toggle updates the root `data-theme` attribute to the *resolved* value (`dark`, `light`, or `high-contrast` — never `system` itself) so CSS variables can switch the entire palette; the toggle button's own icon/label reflect the stored preference (so it shows "Follow system" rather than whatever that currently resolves to).
- The high-contrast theme uses a pure black/white/yellow palette, solid (non-blended) surfaces, thicker borders, and a colorblind-distinguishable wheel-slice palette so every theme meets accessibility contrast expectations.

## Import and Export (JSON)

Open the gear-icon Settings button and select the Import/Export tab.

- **Export** (`GET /api/books/export`) downloads a JSON file for the signed-in account containing:
  - `schemaVersion`, `exportedAtUtc`, and `account.username` (who the file was exported from, for reference)
  - `books`: every book on the account, active and soft-deleted, each with `title`, `isbn`, `author`, `coverUrl`, and `deletedAtUtc` (`null` for active books)
  - `spinHistory`: a read-only record of past wheel spins (`title`, `selectedAtUtc`)
- **Import** (`POST /api/books/import`) accepts JSON in `[{"title":"..."}]`, `{"books":[{"title":"..."}]}`, or the richer export shape (with `isbn`/`author`/`coverUrl` per book). It merges into your existing books:
  - An incoming book is skipped if its title (case-insensitive) or ISBN matches a book you already have — **including books you've soft-deleted**, so re-importing an old backup won't resurrect something you removed on purpose.
  - Spin history in an import file is never recreated; it's exported for backup/reference only, since imported books get new IDs and replaying spin counts against them would be misleading.
  - If the file's `account.username` doesn't match the signed-in account, the import still proceeds but the UI shows a warning noting the mismatch.
- Import flow uses JSON file upload (`.json`); the download area is shown only when the Export tab is selected.
