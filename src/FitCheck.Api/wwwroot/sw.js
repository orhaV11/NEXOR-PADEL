// OREVOSH service worker: the app shell is cached so the app opens instantly and works offline for what was
// already loaded. The API and photos are never cached here; they always go to the network. Shell files are
// fetched with cache: 'no-cache', so the browser revalidates them and a deploy is picked up as one consistent set.
const VERSION = 'orevosh-shell-v2';
const SHELL = ['/', '/index.html', '/app.css', '/app/main.js', '/app/core.js', '/manifest.webmanifest', '/i18n/en.json', '/i18n/he.json'];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(SHELL.map((p) => new Request(p, { cache: 'no-cache' })))).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== location.origin) return;
  if (url.pathname.startsWith('/api/')) return;               // live data and private photos: network only
  const isNavigation = event.request.mode === 'navigate';
  const isShell = url.pathname.startsWith('/app/') || url.pathname.startsWith('/i18n/') || SHELL.includes(url.pathname);
  if (!isNavigation && !isShell) return;                      // icons and the like: the browser handles them
  const request = isNavigation
    ? new Request('/index.html', { cache: 'no-cache', credentials: 'same-origin' })
    : new Request(event.request, { cache: 'no-cache' });
  const cacheKey = isNavigation ? '/index.html' : url.pathname;
  event.respondWith(
    fetch(request).then((response) => {
      if (response.ok && (!isNavigation || (response.headers.get('content-type') || '').includes('text/html'))) {
        const copy = response.clone();
        caches.open(VERSION).then((cache) => cache.put(cacheKey, copy));
      }
      return response;
    }).catch(() => caches.match(cacheKey))
  );
});
