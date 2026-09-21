// OREVOSH service worker: the app shell is cached so the app opens instantly and works offline for what was
// already loaded. The API and photos are never cached here; they always go to the network. Shell files are
// fetched with cache: 'no-cache', so the browser revalidates them and a deploy is picked up as one consistent set.
// It also shows Web Push notifications and opens the app on the right screen when one is tapped.
// Round 13: /offline.html is precached and answers a navigation that has no network and no cached shell to fall back on
// (a /landing/ page, or the app before its shell was ever cached): the brand, one line, retry. The four locale files stay
// precached whether or not a language is live (Languages:Enabled): they are small, and enabling one needs no new shell.
// Round 15, the three things a phone on a real network taught us: the offline page's own script (/offline.js) and the
// font flip (/fonts.js) are precached beside the files they belong to; a 5xx from the edge gets our offline page
// instead of Cloudflare's; and a cached shell stops waiting on a network that has gone quiet after SLOW_MS.
const VERSION = 'orevosh-shell-v6';
const OFFLINE = '/offline.html';
const SHELL = ['/', '/index.html', '/app.css', '/app/main.js', '/app/core.js', '/app/push.js', '/app/sharecard.js', '/app/sharevideo.js', '/vendor/mp4-muxer/mp4-muxer.mjs', '/vendor/webm-muxer/webm-muxer.mjs', '/brand/wordmark.svg', '/brand/mark.svg', '/manifest.webmanifest', '/fonts.js', '/i18n/en.json', '/i18n/he.json', '/i18n/ar.json', '/i18n/ru.json', OFFLINE, '/offline.js'];
// How long a cached shell waits for the network before it answers anyway. Cafe wifi that is associated but passing no
// traffic does not fail: navigator.onLine stays true, the offline bar stays down and fetch simply hangs — iOS Safari
// for about a minute — while a complete working app sits in Cache Storage. Above a cold tunnel's first byte, well
// under any phone's own timeout. The fetch is never cancelled, so the revalidation it was doing still lands.
const SLOW_MS = 9000;

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(SHELL.map((p) => new Request(p, { cache: 'no-cache' })))).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

// A document the edge itself could not produce — a 502 or 503 from Cloudflare, from Fly, or from a quick tunnel whose
// `dotnet run` is restarting — is somebody else's error page, and inside an installed app it fills a screen with no
// address bar on it. Ours instead, with the mark and a Try again. Navigations only, and 5xx only: a 404 or a 410 is
// this server's own answer and it says something true, so it is passed through as it is.
const edgeFailed = (response) => (response.status >= 500 ? caches.match(OFFLINE).then((page) => page || response) : response);
/** A server-rendered document (a landing page, a look, a profile, the digest): the network, or our own page. */
const document_ = (request) => fetch(request).then(edgeFailed).catch(() => caches.match(OFFLINE));

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== location.origin) return;
  if (url.pathname.startsWith('/api/')) return;               // live data and private photos: network only
  const isNavigation = event.request.mode === 'navigate';
  if (url.pathname.startsWith('/landing/')) {                 // the static landing pages are their own documents, not the shell:
    if (isNavigation) event.respondWith(document_(event.request));
    return;
  }
  // Round 13 — the growth loop: /look/<id>, /u/<handle> and the weekly mail's /digest/off/<token> are server-rendered
  // pages, each one a document of its own. Never the shell: a phone with the app installed must see the page a share
  // led it to, not the app's own index.html with an empty hash route.
  if (url.pathname.startsWith('/look/') || url.pathname.startsWith('/u/') || url.pathname.startsWith('/digest/')) {
    if (isNavigation) event.respondWith(document_(event.request));
    return;
  }
  const isShell = url.pathname.startsWith('/app/') || url.pathname.startsWith('/i18n/') || SHELL.includes(url.pathname);
  if (!isNavigation && !isShell) return;                      // icons and the like: the browser handles them
  const request = isNavigation
    ? new Request('/index.html', { cache: 'no-cache', credentials: 'same-origin' })
    : new Request(event.request, { cache: 'no-cache' });
  const cacheKey = isNavigation ? '/index.html' : url.pathname;
  const cached = caches.match(cacheKey);
  // Network first, exactly as before: the fetch starts on this line, revalidates, and a good answer replaces the entry,
  // so a deploy is picked up as one consistent set. What is new is only what happens while it neither answers nor
  // fails. waitUntil keeps this worker alive for the cache.put even when the answer below came from the cache.
  const fromNetwork = fetch(request).then((response) => {
    if (response.ok && (!isNavigation || (response.headers.get('content-type') || '').includes('text/html'))) {
      const copy = response.clone();
      caches.open(VERSION).then((cache) => cache.put(cacheKey, copy));
    }
    return isNavigation ? edgeFailed(response) : response;
  });
  event.waitUntil(fromNetwork.catch(() => { /* answered from the cache below */ }));
  const answer = fromNetwork.catch(() => cached.then((hit) => hit || (isNavigation ? caches.match(OFFLINE) : undefined)));
  // The shell is already here, so a network that has stopped passing traffic costs a wait and nothing else. The timer
  // resolves only where there is something to resolve with — an undefined would turn a navigation into a network
  // error — and it never cancels the fetch, which keeps revalidating behind the answer.
  const slow = cached.then((hit) => (hit ? new Promise((resolve) => setTimeout(() => resolve(hit.clone()), SLOW_MS)) : new Promise(() => { })));
  event.respondWith(Promise.race([answer, slow]));
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
  // icon is the picture beside the text and wants the colour one. badge is the small mark Android draws in the status
  // bar and in the shade's header: it is rendered from the ALPHA channel alone and tinted, so a full-colour opaque
  // square becomes a solid white blob. /icons/badge-96.png is the ring and the flame as a white silhouette on nothing,
  // which is what that channel needs. It is deliberately not in SHELL — the OS fetches it itself when a notification
  // arrives, and putting it there would cost every phone a shell re-download for a file the app never reads.
  event.waitUntil(self.registration.showNotification(data.title || 'OREVOSH', {
    body: data.body || '',
    icon: '/icons/icon-192.png',
    badge: '/icons/badge-96.png',
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
