# PWA Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Book Wheel installable as a PWA with an offline-capable app shell (manifest, service worker, generated icons, offline fallback page), while leaving all `/api/*` traffic live-network-only, per `docs/superpowers/specs/2026-08-06-pwa-support-design.md`.

**Architecture:** A static web app manifest and a version-stamped service worker (served through a new `Program.cs` route, mirroring how `index.html` is already version-stamped) precache the HTML/CSS/JS/icon shell on install. The service worker explicitly bypasses `/api/*` so auth and book data always hit the network. Icons are rasterized to PNG by a stdlib-only Python script since no image tooling is available in this environment.

**Tech Stack:** ASP.NET Core 8 minimal APIs (C#), vanilla JS (no bundler), xUnit + `WebApplicationFactory<Program>` for tests, Python 3 stdlib for icon generation.

## Global Constraints

- No new runtime dependencies (no image libraries, no JS build step, no NuGet packages) — this app has none today and the spec doesn't call for adding any.
- `sw.js` must never intercept requests under `/api/`.
- Every new/changed frontend string must be translated for all three supported locales (`en`, `es`, `pl`), per the existing i18n convention (`README.md`'s Internationalization section) — Spanish/Polish are a first-pass machine translation, consistent with existing strings.
- Follow the existing test style in `BookWheel.Tests`: `WebApplicationFactory`-backed HTTP calls with plain `Assert.Contains`/`Assert.Equal` string and JSON assertions (see `BookWheelFrontendTests.cs`).
- `BookWheel/BookWheel.csproj`'s `InformationalVersion` bumps `1.8.1` → `1.9.0` (minor bump, no breaking change).

---

### Task 1: Generate the PWA icon set

**Files:**
- Create: `scripts/generate-pwa-icons.py`
- Create (generated output, committed): `BookWheel/wwwroot/icons/icon-192.png`, `BookWheel/wwwroot/icons/icon-512.png`, `BookWheel/wwwroot/icons/icon-512-maskable.png`, `BookWheel/wwwroot/icons/icon-180.png`, `BookWheel/wwwroot/icons/favicon-32.png`

**Interfaces:**
- Produces: five PNG files at `BookWheel/wwwroot/icons/<name>.png`, referenced by filename in Task 2 (manifest icons, `index.html` links) and Task 3 (`sw.js` precache list).

- [ ] **Step 1: Write the icon generator script**

Create `scripts/generate-pwa-icons.py`:

```python
#!/usr/bin/env python3
"""Generate BookWheel PWA icon PNGs using only the Python standard library.

Rasterizes a simplified version of the app's own spin wheel (a dark
background square with a circle divided into the six --wheel-slice-*
colors from site.css, plus a light hub at the center) at each size the
PWA manifest and index.html reference. Re-run this script and commit
the output whenever the wheel-slice palette in site.css changes.
"""
import math
import os
import struct
import zlib

BG_COLOR = (0x0F, 0x17, 0x2A)
HUB_COLOR = (0xF1, 0xF5, 0xF9)
SLICE_COLORS = [
    (0x38, 0xBD, 0xF8),
    (0x60, 0xA5, 0xFA),
    (0x81, 0x8C, 0xF8),
    (0xF4, 0x72, 0xB6),
    (0x34, 0xD3, 0x99),
    (0xFB, 0xBF, 0x24),
]

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ICONS_DIR = os.path.join(SCRIPT_DIR, "..", "BookWheel", "wwwroot", "icons")


def _png_chunk(chunk_type, data):
    return (
        struct.pack(">I", len(data))
        + chunk_type
        + data
        + struct.pack(">I", zlib.crc32(chunk_type + data) & 0xFFFFFFFF)
    )


def write_png(path, size, pixels):
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter type: none
        row_start = y * size * 4
        raw.extend(pixels[row_start:row_start + size * 4])

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", ihdr)
        + _png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + _png_chunk(b"IEND", b"")
    )

    with open(path, "wb") as handle:
        handle.write(png)


def render_wheel_icon(size, wheel_radius_fraction):
    pixels = bytearray(size * size * 4)
    center = size / 2.0
    wheel_radius = size * wheel_radius_fraction
    hub_radius = size * 0.08

    for y in range(size):
        dy = (y + 0.5) - center
        for x in range(size):
            dx = (x + 0.5) - center
            dist = math.hypot(dx, dy)

            if dist <= wheel_radius:
                if dist <= hub_radius:
                    color = HUB_COLOR
                else:
                    angle = math.atan2(dy, dx)
                    normalized = (angle + math.pi) / (2 * math.pi)
                    index = int(normalized * len(SLICE_COLORS)) % len(SLICE_COLORS)
                    color = SLICE_COLORS[index]
            else:
                color = BG_COLOR

            offset = (y * size + x) * 4
            pixels[offset] = color[0]
            pixels[offset + 1] = color[1]
            pixels[offset + 2] = color[2]
            pixels[offset + 3] = 255

    return pixels


# (filename, size, wheel radius as a fraction of size — smaller for the
# maskable icon so the wheel stays inside Android's adaptive-icon safe zone)
ICONS = [
    ("icon-192.png", 192, 0.47),
    ("icon-512.png", 512, 0.47),
    ("icon-512-maskable.png", 512, 0.38),
    ("icon-180.png", 180, 0.47),
    ("favicon-32.png", 32, 0.47),
]


def main():
    os.makedirs(ICONS_DIR, exist_ok=True)
    for filename, size, radius_fraction in ICONS:
        pixels = render_wheel_icon(size, radius_fraction)
        write_png(os.path.join(ICONS_DIR, filename), size, pixels)
        print(f"wrote {filename} ({size}x{size})")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Run the generator**

Run: `python3 scripts/generate-pwa-icons.py`
Expected output:
```
wrote icon-192.png (192x192)
wrote icon-512.png (512x512)
wrote icon-512-maskable.png (512x512)
wrote icon-180.png (180x180)
wrote favicon-32.png (32x32)
```

- [ ] **Step 3: Verify the generated files are valid, non-empty PNGs**

Run:
```bash
python3 -c "
import os
names = ['icon-192.png', 'icon-512.png', 'icon-512-maskable.png', 'icon-180.png', 'favicon-32.png']
base = 'BookWheel/wwwroot/icons'
for name in names:
    path = os.path.join(base, name)
    with open(path, 'rb') as f:
        header = f.read(8)
    size = os.path.getsize(path)
    assert header == b'\x89PNG\r\n\x1a\n', f'{name}: bad PNG signature'
    assert size > 0, f'{name}: empty file'
    print(f'{name}: OK ({size} bytes)')
"
```
Expected: five `OK` lines, no `AssertionError`.

- [ ] **Step 4: Commit**

```bash
git add scripts/generate-pwa-icons.py BookWheel/wwwroot/icons
git commit -m "Add PWA icon set and generator script"
```

---

### Task 2: Web app manifest and `index.html` head links

**Files:**
- Create: `BookWheel/wwwroot/manifest.webmanifest`
- Modify: `BookWheel/Program.cs:220-228` (add content-type mapping for `.webmanifest`)
- Modify: `BookWheel/wwwroot/index.html:7` (add manifest/theme-color/icon links)
- Test: `BookWheel.Tests/BookWheelPwaTests.cs` (new file)

**Interfaces:**
- Consumes: icon files from Task 1 (`icons/icon-192.png`, `icons/icon-512.png`, `icons/icon-512-maskable.png`, `icons/icon-180.png`, `icons/favicon-32.png`).
- Produces: `BookWheel/wwwroot/manifest.webmanifest` (consumed by Task 3's `sw.js` precache list), `index.html` head additions.

- [ ] **Step 1: Write the failing tests**

Create `BookWheel.Tests/BookWheelPwaTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace BookWheel.Tests;

public sealed class BookWheelPwaTests
{
    [Fact]
    public async Task Manifest_Should_Be_Served_With_Correct_Content_Type_And_Fields()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/manifest+json", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Book Wheel", root.GetProperty("name").GetString());
        Assert.Equal("Book Wheel", root.GetProperty("short_name").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.True(icons.Count >= 3);
        Assert.Contains(icons, icon => icon.TryGetProperty("purpose", out var purpose) && purpose.GetString() == "maskable");
    }

    [Fact]
    public async Task Manifest_Icons_Should_All_Resolve_To_Real_Files()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var manifestResponse = await client.GetAsync("/manifest.webmanifest");
        using var document = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync());

        foreach (var icon in document.RootElement.GetProperty("icons").EnumerateArray())
        {
            var src = icon.GetProperty("src").GetString();
            Assert.NotNull(src);

            var iconResponse = await client.GetAsync("/" + src);
            Assert.Equal(HttpStatusCode.OK, iconResponse.StatusCode);
            Assert.Equal("image/png", iconResponse.Content.Headers.ContentType?.MediaType);

            var bytes = await iconResponse.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 0, $"{src} was empty");
        }
    }

    [Fact]
    public async Task Home_Page_Should_Reference_Manifest_Theme_Color_And_Icons()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("rel=\"manifest\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"theme-color\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"apple-touch-icon\"", html, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelPwaTests"`
Expected: all three tests FAIL (404 for `/manifest.webmanifest`, missing `rel="manifest"` etc. in home page HTML).

- [ ] **Step 3: Create the manifest**

Create `BookWheel/wwwroot/manifest.webmanifest`:

```json
{
  "name": "Book Wheel",
  "short_name": "Book Wheel",
  "description": "Manage a list of books and spin a wheel to pick a title at random.",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "background_color": "#0f172a",
  "theme_color": "#0f172a",
  "lang": "en",
  "icons": [
    { "src": "icons/icon-192.png", "sizes": "192x192", "type": "image/png", "purpose": "any" },
    { "src": "icons/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "any" },
    { "src": "icons/icon-512-maskable.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" }
  ]
}
```

- [ ] **Step 4: Register the `.webmanifest` content type**

In `BookWheel/Program.cs`, replace the existing static files block (currently lines 220-228):

```csharp
app.UseStaticFiles(new StaticFileOptions
{
	OnPrepareResponse = context =>
	{
		context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
		context.Context.Response.Headers.Pragma = "no-cache";
		context.Context.Response.Headers.Expires = "0";
	}
});
```

with:

```csharp
var staticFileContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticFileContentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

app.UseStaticFiles(new StaticFileOptions
{
	ContentTypeProvider = staticFileContentTypeProvider,
	OnPrepareResponse = context =>
	{
		context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
		context.Context.Response.Headers.Pragma = "no-cache";
		context.Context.Response.Headers.Expires = "0";
	}
});
```

- [ ] **Step 5: Add head links to `index.html`**

In `BookWheel/wwwroot/index.html`, replace line 7:

```html
  <link rel="stylesheet" href="css/site.css?v=__ASSET_VERSION__" />
```

with:

```html
  <link rel="stylesheet" href="css/site.css?v=__ASSET_VERSION__" />
  <link rel="manifest" href="manifest.webmanifest?v=__ASSET_VERSION__" />
  <meta name="theme-color" content="#0f172a" />
  <meta name="apple-mobile-web-app-capable" content="yes" />
  <meta name="mobile-web-app-capable" content="yes" />
  <link rel="icon" type="image/png" sizes="32x32" href="icons/favicon-32.png?v=__ASSET_VERSION__" />
  <link rel="apple-touch-icon" href="icons/icon-180.png?v=__ASSET_VERSION__" />
```

(`__ASSET_VERSION__` is replaced server-side by the existing `WriteConfiguredIndexAsync`, same as the stylesheet link above it — no new server code needed for this step.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelPwaTests"`
Expected: all three tests PASS.

- [ ] **Step 7: Commit**

```bash
git add BookWheel/wwwroot/manifest.webmanifest BookWheel/Program.cs BookWheel/wwwroot/index.html BookWheel.Tests/BookWheelPwaTests.cs
git commit -m "Add PWA manifest and wire it into index.html"
```

---

### Task 3: Offline fallback page and service worker

**Files:**
- Create: `BookWheel/wwwroot/offline.html`
- Create: `BookWheel/wwwroot/sw.js`
- Modify: `BookWheel/Program.cs:202-218` (add `WriteConfiguredServiceWorkerAsync` + `/sw.js` route)
- Test: `BookWheel.Tests/BookWheelPwaTests.cs` (append)

**Interfaces:**
- Consumes: `appVersion` variable already in scope in `Program.cs` (line 113); `manifest.webmanifest` and icon paths from Tasks 1-2 (referenced in `sw.js`'s `SHELL_ASSETS` list).
- Produces: `/sw.js` route (consumed by Task 4's `navigator.serviceWorker.register('/sw.js')` call); `offline.html` (consumed by `sw.js`'s navigation fallback).

- [ ] **Step 1: Write the failing tests**

Append to `BookWheel.Tests/BookWheelPwaTests.cs` (inside the `BookWheelPwaTests` class, after the existing tests):

```csharp

    [Fact]
    public async Task Offline_Fallback_Page_Should_Be_Served()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/offline.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Book Wheel is offline", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_Worker_Should_Be_Served_With_Current_Version_And_Lifecycle_Handlers()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var versionResponse = await client.GetAsync("/api/version");
        using var versionDocument = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync());
        var version = versionDocument.RootElement.GetProperty("version").GetString();
        Assert.NotNull(version);

        var swResponse = await client.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, swResponse.StatusCode);
        Assert.Contains("javascript", swResponse.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);

        var script = await swResponse.Content.ReadAsStringAsync();
        Assert.Contains("CACHE_NAME", script, StringComparison.Ordinal);
        Assert.Contains(version!, script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('install'", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('activate'", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('fetch'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_Worker_Should_Never_Intercept_Api_Requests()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sw.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: an explicit bypass guard for /api/ exists in the fetch handler.
        Assert.Contains("pathname.startsWith('/api/')", script, StringComparison.Ordinal);

        // Negative: the bypass must come before any cache read/write for the request,
        // otherwise API responses could still be served from or written to the cache.
        var apiGuardIndex = script.IndexOf("pathname.startsWith('/api/')", StringComparison.Ordinal);
        var firstCacheOpIndex = script.IndexOf("caches.open", StringComparison.Ordinal);
        Assert.True(apiGuardIndex >= 0 && firstCacheOpIndex >= 0 && apiGuardIndex < firstCacheOpIndex);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelPwaTests"`
Expected: the three new tests FAIL (404 for `/offline.html` and `/sw.js`).

- [ ] **Step 3: Create the offline fallback page**

Create `BookWheel/wwwroot/offline.html`:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Book Wheel — Offline</title>
  <link rel="stylesheet" href="css/site.css" />
</head>
<body>
  <main class="shell">
    <section class="card">
      <h1>Book Wheel is offline</h1>
      <p class="message">You can't reach the server right now. Check your connection, then reload the page.</p>
    </section>
  </main>
</body>
</html>
```

- [ ] **Step 4: Create the service worker**

Create `BookWheel/wwwroot/sw.js`:

```javascript
const CACHE_VERSION = '__ASSET_VERSION__';
const CACHE_NAME = `bookwheel-shell-v${CACHE_VERSION}`;

const SHELL_ASSETS = [
  '/',
  '/css/site.css',
  '/js/app.js',
  '/js/i18n.js',
  '/manifest.webmanifest',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/icon-512-maskable.png',
  '/icons/icon-180.png',
  '/icons/favicon-32.png',
  '/offline.html'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(SHELL_ASSETS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(
        keys
          .filter((key) => key.startsWith('bookwheel-shell-') && key !== CACHE_NAME)
          .map((key) => caches.delete(key))
      ))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') {
    return;
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return;
  }

  if (url.pathname.startsWith('/api/')) {
    return;
  }

  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then((response) => {
          const responseCopy = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put('/', responseCopy));
          return response;
        })
        .catch(() => caches.match('/').then((cached) => cached || caches.match('/offline.html')))
    );
    return;
  }

  event.respondWith(
    caches.match(request).then((cached) => {
      const networkFetch = fetch(request)
        .then((response) => {
          const responseCopy = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put(request, responseCopy));
          return response;
        })
        .catch(() => cached);

      return cached || networkFetch;
    })
  );
});
```

- [ ] **Step 5: Add the `/sw.js` route in `Program.cs`**

In `BookWheel/Program.cs`, insert a new function and route directly after the existing `WriteConfiguredIndexAsync` function and before its `app.MapGet` calls (currently lines 202-218):

```csharp
async Task WriteConfiguredIndexAsync(HttpContext context)
{
	var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
	var indexPath = Path.Combine(webRootPath, "index.html");
	var html = await File.ReadAllTextAsync(indexPath);
	html = html.Replace("__GOOGLE_ANALYTICS_ID__", googleAnalyticsId, StringComparison.Ordinal);
	html = html.Replace("__ASSET_VERSION__", Uri.EscapeDataString(appVersion), StringComparison.Ordinal);

	context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
	context.Response.Headers.Pragma = "no-cache";
	context.Response.Headers.Expires = "0";
	context.Response.ContentType = "text/html; charset=utf-8";
	await context.Response.WriteAsync(html);
}

async Task WriteConfiguredServiceWorkerAsync(HttpContext context)
{
	var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
	var swPath = Path.Combine(webRootPath, "sw.js");
	var script = await File.ReadAllTextAsync(swPath);
	script = script.Replace("__ASSET_VERSION__", appVersion, StringComparison.Ordinal);

	context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
	context.Response.Headers.Pragma = "no-cache";
	context.Response.Headers.Expires = "0";
	context.Response.ContentType = "application/javascript; charset=utf-8";
	await context.Response.WriteAsync(script);
}

app.MapGet("/", WriteConfiguredIndexAsync);
app.MapGet("/index.html", WriteConfiguredIndexAsync);
app.MapGet("/sw.js", WriteConfiguredServiceWorkerAsync);
```

(Note: `sw.js`'s version placeholder is replaced with the raw `appVersion`, not `Uri.EscapeDataString(appVersion)` — unlike the HTML cache-busting query string, this value is embedded directly in a JS string literal, so URL-escaping would corrupt it, e.g. turning a CI-suffixed version's `+` into `%2B`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelPwaTests"`
Expected: all six `BookWheelPwaTests` tests PASS.

- [ ] **Step 7: Commit**

```bash
git add BookWheel/wwwroot/offline.html BookWheel/wwwroot/sw.js BookWheel/Program.cs BookWheel.Tests/BookWheelPwaTests.cs
git commit -m "Add service worker with app-shell caching and offline fallback page"
```

---

### Task 4: Register the service worker and add connectivity feedback

**Files:**
- Modify: `BookWheel/wwwroot/js/app.js` (append at end of file, after line 1702)
- Modify: `BookWheel/wwwroot/js/i18n.js` (add `common.offlineToast` / `common.onlineToast` for `en`, `es`, `pl`)
- Test: `BookWheel.Tests/BookWheelPwaTests.cs` (append)

**Interfaces:**
- Consumes: `/sw.js` route from Task 3; `window.BookWheelI18n.t(key)` and `showToast(message, type)` (both already defined earlier in `app.js`).
- Produces: nothing consumed by later tasks (terminal frontend task).

- [ ] **Step 1: Write the failing tests**

Append to `BookWheel.Tests/BookWheelPwaTests.cs` (inside the `BookWheelPwaTests` class):

```csharp

    [Fact]
    public async Task Frontend_Script_Should_Register_Service_Worker_With_Feature_Detection()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        // Positive: registration happens, guarded by a feature check.
        Assert.Contains("'serviceWorker' in navigator", script, StringComparison.Ordinal);
        Assert.Contains("navigator.serviceWorker.register('/sw.js')", script, StringComparison.Ordinal);

        // Negative: the feature check must come before the registration call,
        // not after — otherwise it would throw in browsers without SW support.
        var guardIndex = script.IndexOf("'serviceWorker' in navigator", StringComparison.Ordinal);
        var registerIndex = script.IndexOf("navigator.serviceWorker.register('/sw.js')", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && registerIndex >= 0 && guardIndex < registerIndex);
    }

    [Fact]
    public async Task Frontend_Script_Should_Notify_User_On_Connectivity_Changes()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/app.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("addEventListener('offline'", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('online'", script, StringComparison.Ordinal);
        Assert.Contains("common.offlineToast", script, StringComparison.Ordinal);
        Assert.Contains("common.onlineToast", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I18n_Catalog_Should_Include_Connectivity_Toast_Strings_For_All_Locales()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("You're offline. Some features won't work until you reconnect.", script, StringComparison.Ordinal);
        Assert.Contains("Back online.", script, StringComparison.Ordinal);
        Assert.Contains("Estás sin conexión. Algunas funciones no estarán disponibles hasta que te reconectes.", script, StringComparison.Ordinal);
        Assert.Contains("De nuevo en línea.", script, StringComparison.Ordinal);
        Assert.Contains("Jesteś offline. Niektóre funkcje będą niedostępne, dopóki nie połączysz się ponownie.", script, StringComparison.Ordinal);
        Assert.Contains("Znowu online.", script, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelPwaTests"`
Expected: the three new tests FAIL (strings not yet present in `app.js`/`i18n.js`).

- [ ] **Step 3: Add the toast strings to `i18n.js`**

In `BookWheel/wwwroot/js/i18n.js`, the English `common` block currently ends with:

```javascript
        connectionError: 'Cannot connect to the server. Make sure the app is running, then try again.'
      },
```

Replace with:

```javascript
        connectionError: 'Cannot connect to the server. Make sure the app is running, then try again.',
        offlineToast: 'You\'re offline. Some features won\'t work until you reconnect.',
        onlineToast: 'Back online.'
      },
```

The Spanish `common` block currently ends with:

```javascript
        connectionError: 'No se puede conectar con el servidor. Asegúrate de que la aplicación esté en ejecución e inténtalo de nuevo.'
      },
```

Replace with:

```javascript
        connectionError: 'No se puede conectar con el servidor. Asegúrate de que la aplicación esté en ejecución e inténtalo de nuevo.',
        offlineToast: 'Estás sin conexión. Algunas funciones no estarán disponibles hasta que te reconectes.',
        onlineToast: 'De nuevo en línea.'
      },
```

The Polish `common` block currently ends with:

```javascript
        connectionError: 'Nie można połączyć się z serwerem. Upewnij się, że aplikacja działa, a następnie spróbuj ponownie.'
      },
```

Replace with:

```javascript
        connectionError: 'Nie można połączyć się z serwerem. Upewnij się, że aplikacja działa, a następnie spróbuj ponownie.',
        offlineToast: 'Jesteś offline. Niektóre funkcje będą niedostępne, dopóki nie połączysz się ponownie.',
        onlineToast: 'Znowu online.'
      },
```

- [ ] **Step 4: Register the service worker and wire connectivity toasts in `app.js`**

Append to the end of `BookWheel/wwwroot/js/app.js` (after the closing `})();` of the bootstrap IIFE):

```javascript

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Installability/offline caching is a progressive enhancement; ignore registration failures.
    });
  });
}

window.addEventListener('offline', () => {
  showToast(window.BookWheelI18n.t('common.offlineToast'), 'error');
});

window.addEventListener('online', () => {
  showToast(window.BookWheelI18n.t('common.onlineToast'), 'success');
});
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~BookWheelPwaTests"`
Expected: all nine `BookWheelPwaTests` tests PASS.

- [ ] **Step 6: Commit**

```bash
git add BookWheel/wwwroot/js/app.js BookWheel/wwwroot/js/i18n.js BookWheel.Tests/BookWheelPwaTests.cs
git commit -m "Register service worker and add connectivity toast notifications"
```

---

### Task 5: Version bump, README, and roadmap documentation

**Files:**
- Modify: `BookWheel/BookWheel.csproj:9`
- Modify: `README.md` (Features list, new "Progressive Web App" section, Testing section)
- Modify: `IMPROVEMENT_ROADMAP.md` (Priority 2 list)

**Interfaces:**
- Consumes: nothing (documentation/version-only task).
- Produces: nothing (terminal task).

- [ ] **Step 1: Bump the version**

In `BookWheel/BookWheel.csproj`, replace:

```xml
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">1.8.1</InformationalVersion>
```

with:

```xml
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">1.9.0</InformationalVersion>
```

- [ ] **Step 2: Add a Features bullet to `README.md`**

In `README.md`, after the line:

```markdown
- Settings menu (theme switcher + language selector) with saved browser preference and localized server error messages
```

add:

```markdown
- Installable Progressive Web App with an offline-capable app shell (manifest, service worker, offline fallback page)
```

- [ ] **Step 3: Add a "Progressive Web App" section to `README.md`**

In `README.md`, insert a new section directly after the end of the "## Internationalization" section (before "## Solution Structure"):

```markdown
## Progressive Web App

Book Wheel can be installed as a standalone app (desktop Chrome/Edge, Android, and — with reduced polish — iOS Safari) and its UI shell keeps working when the network drops.

- A web app manifest (`BookWheel/wwwroot/manifest.webmanifest`) drives the install prompt/icon. Icons live in `BookWheel/wwwroot/icons/` and are generated by `scripts/generate-pwa-icons.py` (Python standard library only, no image tooling required — re-run it if the wheel-slice color palette in `site.css` ever changes).
- A service worker (`BookWheel/wwwroot/sw.js`, served at `/sw.js` through `Program.cs` so its cache name automatically picks up the current app version) precaches the HTML/CSS/JS/icon app shell on install, so the UI keeps loading while offline.
- The service worker never intercepts `/api/*` requests — login, book data, and spin results always require a live connection. A toast notifies the user when the browser goes offline or comes back online.
- `BookWheel/wwwroot/offline.html` is a minimal fallback page shown only if a user's very first visit happens while offline (rare, since the shell is precached on install).
- **Not implemented:** full offline data support (queuing book edits made while offline and syncing them on reconnect). That needs a durable write queue, conflict resolution for concurrent edits, and offline-aware session handling — evaluated for #33 and scoped out as a separate future project (see `IMPROVEMENT_ROADMAP.md`).
```

- [ ] **Step 4: Extend the Testing section of `README.md`**

In `README.md`, in the "Current integration tests cover:" list, after the line:

```markdown
- CI dependency-audit gate (`scripts/check-vulnerable-packages.sh`): passes clean `dotnet list --vulnerable` output through unchanged and exits 0, and exits 1 when the report contains a vulnerable-packages finding
```

add:

```markdown
- PWA manifest, icon, and service-worker behavior, including that `/api/*` requests are never intercepted or cached by the service worker
```

- [ ] **Step 5: Update `IMPROVEMENT_ROADMAP.md`**

In `IMPROVEMENT_ROADMAP.md`, in the "Priority 2: User Experience Improvements" list, replace:

```markdown
6. [Done] Add a high-contrast theme option (dark/light/high-contrast cycle) so every theme meets A11Y contrast expectations.
7. Add optional categories, tags, or reading status to books.
```

with:

```markdown
6. [Done] Add a high-contrast theme option (dark/light/high-contrast cycle) so every theme meets A11Y contrast expectations.
7. [Done] Add PWA support: installable manifest, app-shell service worker caching, and an offline fallback page (#33).
8. Add optional categories, tags, or reading status to books.
9. Full offline data support (queuing book add/edit/delete/spin actions made while offline and syncing on reconnect) was evaluated for #33 and deferred. It needs a durable client-side write queue, conflict resolution for concurrent edits across devices, and offline-aware handling of the cookie-based auth session (which has no refresh/silent-reauth path today). Revisit as its own project rather than an extension of the PWA caching work.
```

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test BookWheel.slnx`
Expected: all tests PASS, including the new `BookWheelPwaTests` and every pre-existing test (confirms the version bump didn't break `CI`'s version-format assumptions and nothing else regressed).

- [ ] **Step 7: Commit**

```bash
git add BookWheel/BookWheel.csproj README.md IMPROVEMENT_ROADMAP.md
git commit -m "Bump version to 1.9.0 and document PWA support"
```

---

## Self-Review Notes

- **Spec coverage:** manifest (Task 2), icons (Task 1), service worker + app-shell caching + `/api/` bypass (Task 3), offline fallback (Task 3), install registration + connectivity toast (Task 4), i18n for new strings (Task 4), version bump (Task 5), README + roadmap docs including the full-offline-sync complexity note (Task 5), test coverage matching existing style throughout — all spec sections are covered.
- **Placeholder scan:** no TBD/TODO markers; every step has literal file contents or exact replace-with text.
- **Type/name consistency:** `WriteConfiguredServiceWorkerAsync` (Task 3) matches its call site in the same task; `CACHE_NAME`/`CACHE_VERSION` used consistently between `sw.js` (Task 3) and its tests; `common.offlineToast`/`common.onlineToast` keys match exactly between `i18n.js` (Task 4) and `app.js` (Task 4) and their tests; icon filenames match exactly across Task 1's generator, Task 2's manifest/`index.html`, and Task 3's `SHELL_ASSETS` list.
