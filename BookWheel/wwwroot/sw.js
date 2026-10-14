const CACHE_VERSION = '__ASSET_VERSION__';
const CACHE_NAME = `bookwheel-shell-v${CACHE_VERSION}`;

const SHELL_ASSETS = [
  '/',
  `/css/site.css?v=${CACHE_VERSION}`,
  `/js/app.js?v=${CACHE_VERSION}`,
  `/js/i18n.js?v=${CACHE_VERSION}`,
  `/i18n/locales.json?v=${CACHE_VERSION}`,
  `/i18n/en.json?v=${CACHE_VERSION}`,
  `/manifest.webmanifest?v=${CACHE_VERSION}`,
  `/icons/icon-192.png?v=${CACHE_VERSION}`,
  `/icons/icon-512.png?v=${CACHE_VERSION}`,
  `/icons/icon-512-maskable.png?v=${CACHE_VERSION}`,
  `/icons/icon-180.png?v=${CACHE_VERSION}`,
  `/icons/favicon-32.png?v=${CACHE_VERSION}`,
  '/offline.html'
];

self.addEventListener('install', (event) => {
  // Deliberately does not force activation here: a new worker installed
  // while a tab is open would otherwise swap app.js/the API contract under
  // that tab without warning. It waits until the page asks it to take over
  // (see the 'message' handler below), driven by the update-available toast
  // in app.js.
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(SHELL_ASSETS))
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

self.addEventListener('message', (event) => {
  if (event.data === 'SKIP_WAITING') {
    self.skipWaiting();
  }
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
