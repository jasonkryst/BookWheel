# PWA Support — Design

- **Issue:** [#33 — PWA](https://github.com/jasonkryst/BookWheel/issues/33)
- **Branch:** `33`
- **Date:** 2026-08-06

## Problem

Issue #33: *"Investigate if this can have PWA functionality added to it. If it can, do so. If not add comments regarding the complexity."*

Book Wheel is a single-origin ASP.NET Core app serving a static frontend (`wwwroot/index.html`, `js/app.js`, `js/i18n.js`, `css/site.css`) plus a JSON API under `/api`. There is no manifest, service worker, or icon set today, so the app cannot be installed and has no offline behavior.

**Feasibility finding:** installability and app-shell offline caching are straightforward to add. Full offline *data* support (queued book edits made while offline, conflict resolution, offline-aware cookie auth) is materially harder and is called out below as the complexity the issue asks to document if not implemented.

## Scope

**In scope:**
- Web app manifest making the app installable (desktop Chrome/Edge, Android, best-effort iOS Safari).
- A service worker that precaches the app shell (HTML/CSS/JS/icons) so the UI loads offline, while never intercepting `/api/*` requests — auth, book data, and spin results always require a live network round-trip.
- Generated icon set (192/512/512-maskable/180/favicon) via a committed, dependency-free Python script.
- A minimal offline fallback page for the edge case of a first-ever visit happening offline.
- An online/offline connectivity toast so users get feedback when live data stops working.
- Positive/negative test coverage matching the existing `BookWheel.Tests` string/HTTP-assertion style.
- README documentation of the new capability.
- Minor version bump (`1.8.1` → `1.9.0`) per the project's `InformationalVersion` convention.

**Out of scope (complexity noted per the issue's fallback instruction):**
- **Full offline data sync.** Queuing add/edit/delete/spin actions made while offline and replaying them on reconnect requires: a durable client-side write queue, conflict resolution against concurrent server-side changes (books are per-user but multi-device), and offline-aware handling of the cookie-based auth session (a session can expire or be revoked while offline, and the app currently has no refresh-token/silent-reauth mechanism to paper over that). This is a distinct project, not a PWA-caching add-on, and is left for a future issue if wanted.
- Push notifications / background sync APIs — no server-side event source exists that would use them.
- Native app-store packaging (TWA/Capacitor) — out of scope for a self-hosted web app.

## Architecture

```
BookWheel/
  Program.cs                        (new /sw.js route; .webmanifest content-type registration)
  wwwroot/
    manifest.webmanifest            (new)
    offline.html                    (new — static fallback shell)
    icons/
      icon-192.png                  (new)
      icon-512.png                  (new)
      icon-512-maskable.png         (new)
      icon-180.png                  (new — apple-touch-icon)
      favicon-32.png                (new)
    js/
      sw.js                         (new — service worker, served via Program.cs route, not static files)
      app.js                        (SW registration; online/offline toast wiring)
      i18n.js                       (new common.offlineToast / common.onlineToast keys, en/es/pl)
    index.html                      (manifest link, theme-color, apple-touch-icon, favicon links)

scripts/
  generate-pwa-icons.py             (new — stdlib-only PNG rasterizer for the icon set)

BookWheel.Tests/
  BookWheelPwaTests.cs              (new)

README.md                           (new "Progressive Web App" section + Features bullet)
IMPROVEMENT_ROADMAP.md              (note shipped scope; flag full offline-sync complexity)
BookWheel/BookWheel.csproj          (InformationalVersion 1.8.1 -> 1.9.0)
```

### Why `/sw.js` is a Program.cs route, not a static file

`index.html` is already served through `WriteConfiguredIndexAsync`, which reads the file from disk and substitutes `__ASSET_VERSION__`/`__GOOGLE_ANALYTICS_ID__` at request time. `sw.js` needs the same treatment for one reason: its `CACHE_NAME` constant must change every release, or a returning user's browser will keep serving a stale precached shell indefinitely after a deploy. Piggybacking on the existing version-substitution pattern (reading `BookWheel/BookWheel.csproj`'s `InformationalVersion` via the same `appVersion` variable already in scope in `Program.cs`) means a version bump automatically busts the service worker cache with no separate manual step. The route reuses `index.html`'s response headers (`no-store, no-cache, must-revalidate`) so browsers always fetch the current script rather than caching it long-term.

`manifest.webmanifest` has no volatile content, so it stays a plain static file; it only needs a content-type mapping added to `StaticFileOptions`' `FileExtensionContentTypeProvider` since `.webmanifest` isn't a default-recognized extension (`application/manifest+json`).

### Service worker caching strategy

- **`install`**: precache `/`, `css/site.css`, `js/app.js`, `js/i18n.js`, `manifest.webmanifest`, the icon files, and `offline.html` into `bookwheel-shell-v<version>`; `self.skipWaiting()`.
- **`activate`**: delete any cache key prefixed `bookwheel-shell-` that isn't the current version; `self.clients.claim()`.
- **`fetch`**:
  - Non-GET or cross-origin requests: not intercepted (default browser behavior).
  - Any request whose path starts with `/api/`: not intercepted, explicitly, so auth cookies and live data are never served stale or from cache.
  - Navigation requests (`event.request.mode === 'navigate'`): network-first, caching a fresh copy of `/` on success, falling back to the cached `/` and then `offline.html` on failure.
  - Other same-origin GET requests (css/js/icons/manifest): cache-first, refreshing the cache from the network in the background.

This means: the app installs and its shell loads offline; anything requiring live data (login, books, spin) correctly shows the existing "cannot connect" messaging when offline, augmented by the new connectivity toast.

### Icon generation

No image tooling (ImageMagick/PIL/Inkscape) is available in this environment. `scripts/generate-pwa-icons.py` uses only `zlib`/`struct` from the Python standard library to rasterize a simplified version of the app's own spin wheel — a dark (`#0f172a`) circular background with six wedges in the existing `--wheel-slice-1..6` palette — directly to PNG at each required size. The maskable variant keeps wedge content within the safe-zone circle (per maskable-icon guidelines) so Android's adaptive-icon mask doesn't clip it. The script is committed so icons can be regenerated if the palette changes.

### Frontend changes

`app.js`: after the existing bootstrap IIFE, register the service worker guarded by `'serviceWorker' in navigator` feature detection (must not register unconditionally — throws in unsupporting browsers). Add `window.addEventListener('online'/'offline', ...)` calling `showToast(t('common.onlineToast'/'common.offlineToast'), ...)`.

`i18n.js`: add `common.offlineToast` / `common.onlineToast` for `en`/`es`/`pl`, following the existing machine-translation-as-first-pass convention already documented in the README for this project's i18n strings.

`index.html`: `<link rel="manifest">`, `<meta name="theme-color" content="#0f172a">`, `<link rel="apple-touch-icon">`, favicon `<link>`, `<meta name="apple-mobile-web-app-capable" content="yes">`.

## Testing

New `BookWheel.Tests/BookWheelPwaTests.cs`, using `BookWheelWebAppFactory` + `HttpClient`, mirroring the assertion style in `BookWheelFrontendTests.cs`:

- **Manifest correctness**: `GET /manifest.webmanifest` → 200, `Content-Type: application/manifest+json`; parse as JSON and assert `name`, `short_name`, `start_url == "/"`, `display == "standalone"`, and that the `icons` array includes 192/512/maskable entries.
- **Manifest icons actually resolve**: for each icon `src` parsed from the manifest, issue a real `GET` and assert `200` + `Content-Type: image/png` + non-empty body — catches a manifest pointing at a file that doesn't exist, rather than trusting the JSON alone.
- **Service worker served correctly**: `GET /js/sw.js` → 200, JS content type, contains `CACHE_NAME` and the current app version string (read the same way the app itself exposes it, e.g. via `/api/version`), and contains the three lifecycle listener registrations (`install`, `activate`, `fetch`).
- **Negative — API bypass is real**: assert `sw.js` contains the literal `/api/` bypass guard used in the fetch handler, pinning the intended behavior rather than asserting a vague absence of caching logic.
- **Offline fallback served**: `GET /offline.html` → 200 with recognizable offline messaging text.
- **Home page wiring**: `/` response contains `rel="manifest"`, `name="theme-color"`, `rel="apple-touch-icon"`.
- **Registration guarded, not unconditional**: `app.js` contains `'serviceWorker' in navigator` before `navigator.serviceWorker.register(...)`.
- **Locale completeness**: `i18n.js` contains the new toast keys with non-English text present for `es`/`pl`, matching the existing per-locale completeness checks in `BookWheelFrontendTests.cs`.

## Documentation

- `README.md`: new "Progressive Web App" section (what's installable, what works offline vs. requires network, where the files live) plus a bullet under Features.
- `IMPROVEMENT_ROADMAP.md`: record that installable + app-shell-offline PWA support shipped, and explicitly document that full offline data sync was evaluated and scoped out due to the write-queue/conflict-resolution/session-handling complexity described above.
- `BookWheel/BookWheel.csproj`: bump `InformationalVersion` from `1.8.1` to `1.9.0` (minor bump per user request — new capability, no breaking change).
