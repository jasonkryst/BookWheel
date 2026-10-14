# Changelog

All notable changes to Book Wheel are documented here. The format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions correspond to
`BookWheel/BookWheel.csproj`'s `InformationalVersion` (see "Version Stamping" in
[docs/self-hosting.md](docs/self-hosting.md)).

This file was introduced alongside the README split in GH #198. Entries from
3.2.0 onward are reconstructed from commit history; versions not listed below
(including everything before 3.2.0) predate a consistent per-commit version tag
in this repository — see `git log` or the GitHub Releases page for that history.

## [3.16.1]

- Fixed long book titles and author names not truncating with an ellipsis in
  the active-books list, Book Search's multi-result picker, and recent-pick
  rows that share the same markup. The title/author elements are a `<button>`
  and `<span>`, which default to shrink-to-fit sizing with no width of their
  own — without an explicit width, `white-space: nowrap` + `text-overflow:
  ellipsis` had nothing to clip against, so a long title just grew past its
  row (and, before the mobile `overflow-x: hidden` fix, past the page).

## [3.16.0]

- Added configurable retailer links for Book Search: the retailer list (name,
  ISBN/text URL templates, enabled flag, optional affiliate tag) moved from
  hardcoded values into `RetailerLinks:Retailers` config, with locale-aware
  defaults (regional Amazon domains for en/es/pl, Bookshop.org replacing
  Barnes & Noble for es/pl) and an affiliate disclosure note shown only when
  an affiliate tag is actually configured (none are, by default) (GH #208)

## [3.15.2]

- Split the README into a short pitch/quickstart plus `docs/user-guide.md`,
  `docs/admin-guide.md`, `docs/self-hosting.md`, and `docs/architecture.md`,
  consolidated every environment variable into one reference table, and
  introduced this changelog (GH #198)

## [3.15.1]

- Fixed the page growing wider than the viewport on mobile (`body` now clips
  horizontal overflow as a safety net against untracked transformed/absolutely
  positioned content), and a related admin user-directory toolbar grid that was
  missing a column definition for its fourth field.

## [3.15.0]

- Added a "Follow system" theme option that tracks OS color-scheme/contrast
  changes live instead of only resolving once (GH #232)
- Added a "New version available" toast so a deployed update no longer silently
  swaps the service worker under an open tab (GH #233)
- Added PWA install shortcuts for "Add a Book" and "Scan a Book", plus a
  `share_target` so the OS share sheet can send a book into Book Search (GH #243)
- Added optional spin celebration effects (tick sound, haptics, confetti) and
  switched the spin-result reveal to the wheel's `transitionend` event instead
  of a fixed timer (GH #245)
- Added last-login time and book count to the admin user directory, with
  sorting and a delete-confirmation count (GH #197)
- Added server-side persistence for the language preference, so it follows a
  signed-in user to a new device (GH #203)

## [3.14.1]

- Fixed the admin stats aggregate's active-user count to reflect real login/spin
  activity instead of an always-true placeholder, and split out a real
  total-user count (GH #229)

## [3.14.0]

- Added search, filter, sort, and configurable page size to the active book
  list (GH #180)

## [3.13.2]

- Fixed a cluster of correctness bugs (GH #192, #193, #194, #195)

## [3.13.1]

- Fixed "Recent picks" and the last-spin line to scope to the signed-in user
  only, so a shared device never shows another account's picks (GH #263)

## [3.13.0]

- Added automated Postgres backups, an app healthcheck, and a tested restore
  path (GH #182)

## [3.12.0]

- Added a Trash/Restore flow for soft-deleted books (GH #181)
- Added duplicate-add warnings when adding a book already on the wheel or
  previously removed (GH #201)
- Added an admin email-readiness banner and "Send test email" button (GH #178)

## [3.11.0]

- Added HiDPI-aware wheel rendering (GH #177)
- Added a spin-history UI (GH #206)

## [3.10.0]

- Added self-service password change for signed-in users (GH #179)

## [3.9.1]

- Hardened logging: sanitized user-controlled log inputs and masked email
  addresses in structured logs

## [3.9.0]

- Added persistent (database-backed) auto-lockout after repeated failed
  logins (GH #184)

## [3.8.0]

- Added session revocation on account changes (disable/lock/demote/rename/
  force-reset/password-reset) (AUTH-1)
- Added reverse-proxy forwarded-header trust configuration (OPS-1)

## [3.5.0]

- Fixed service-worker cache URL encoding, a metrics naming inconsistency,
  plural-rule handling, and added admin-guard test coverage

## [3.2.0]

- Improved the "Last selected" display and added a media-type breakdown to
  Stats

## Earlier versions

Versions before 3.2.0 (the initial account/login system, book CRUD, the spin
wheel, ISBN lookup (GH #57), Book Search and retailer links (GH #123), book
type classification (GH #73), book info providers (GH #70), the consolidated
Settings dialog (GH #52), SMTP email and self-service recovery (GH #115), and
the PWA manifest/shortcuts, among others) predate this changelog's version
tagging convention. See `git log` for the full history.
