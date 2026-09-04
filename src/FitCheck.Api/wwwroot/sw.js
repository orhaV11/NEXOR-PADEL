// OREVOSH service worker: the app shell is cached so the app opens instantly and works offline for
// what was already loaded. The API and photos are never cached here; they always go to the network.
const VERSION = 'orevosh-shell-v1';
const SHELL = ['/', '/index.html', '/app.css', '/app/main.js', '/app/core.js', '/manifest.webmanifest', '/i18n/en.json', '/i18n/he.json'];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== location.origin) return;
  if (url.pathname.startsWith('/api/')) return;               // live data and private photos: network only
  // Navigations and shell files: network first (so a deploy shows up), cache as the fallback.
  event.respondWith(
    fetch(event.request).then((response) => {
      if (response.ok && (url.pathname.startsWith('/app/') || url.pathname.startsWith('/i18n/') || SHELL.includes(url.pathname) || event.request.mode === 'navigate')) {
        const copy = response.clone();
        caches.open(VERSION).then((cache) => cache.put(event.request.mode === 'navigate' ? '/index.html' : event.request, copy));
      }
      return response;
    }).catch(() => caches.match(event.request.mode === 'navigate' ? '/index.html' : event.request))
  );
});
