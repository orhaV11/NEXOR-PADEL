// OREVOSH service worker: the app shell is cached so the app opens instantly and works offline for what was
// already loaded. The API and photos are never cached here; they always go to the network. Shell files are
// fetched with cache: 'no-cache', so the browser revalidates them and a deploy is picked up as one consistent set.
// It also shows Web Push notifications and opens the app on the right screen when one is tapped.
const VERSION = 'orevosh-shell-v4';
const SHELL = ['/', '/index.html', '/app.css', '/app/main.js', '/app/core.js', '/app/push.js', '/app/sharecard.js', '/brand/wordmark.svg', '/manifest.webmanifest', '/i18n/en.json', '/i18n/he.json', '/i18n/ar.json', '/i18n/ru.json'];

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
  if (url.pathname.startsWith('/landing/')) return;           // the static landing pages are their own documents, not the shell
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

// ---------- push ----------

// The server sends { title, body, url, tag, type }. A push must show something (userVisibleOnly), so a payload that
// cannot be read still becomes a plain OREVOSH notification that opens the activity list.
self.addEventListener('push', (event) => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch (e) { data = { body: event.data ? event.data.text() : '' }; }
  // Only a path on this origin ("//host" would be another site).
  const url = typeof data.url === 'string' && data.url.startsWith('/') && !data.url.startsWith('//') ? data.url : '/#/activity';
  const tag = data.tag || ((data.type || 'orevosh') + ':' + url);   // repeats about the same thing replace each other
  event.waitUntil(self.registration.showNotification(data.title || 'OREVOSH', {
    body: data.body || '',
    icon: '/icons/icon-192.png',
    badge: '/icons/icon-192.png',
    tag,
    data: { url }
  }));
});

// Tap: bring the app that is already open to the front and send it to the screen, else open one there.
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const path = (event.notification.data && event.notification.data.url) || '/#/activity';
  const parsed = new URL(path, self.location.origin);
  const target = parsed.origin === self.location.origin ? parsed.href : new URL('/#/activity', self.location.origin).href;
  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
    const open = windows.find((w) => new URL(w.url).origin === self.location.origin);
    if (!open) return self.clients.openWindow(target);
    return Promise.resolve(open.focus()).then((focused) => {
      const client = focused || open;
      return 'navigate' in client ? client.navigate(target).catch(() => self.clients.openWindow(target)) : self.clients.openWindow(target);
    });
  }));
});
