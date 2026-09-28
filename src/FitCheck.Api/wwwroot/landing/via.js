// OREVOSH landing — the invite that arrives with the page (Round 13, the growth loop).
//
// An invite link is /?via=<handle>. Most of them land on the app, which reads it in app/invite.js; one that lands here
// would otherwise be lost on the way in, so this does the same two things and nothing else: keeps the handle in
// localStorage under the same key the app reads (try/catch — private mode simply has no memory), and carries it on the
// links that lead into the app, so a tap works even where storage does not. No cookie, no request, nothing counted
// here: the server's own middleware already tallies the day's landing views and invite arrivals.
//
// The landing page is otherwise script-free and stays that way: this file is 30 lines, same-origin (the CSP allows no
// other kind) and deferred by being a module, so it never holds the first paint.
const KEY = 'orevosh.invite';
const HANDLE = /^[\p{L}\p{N}_.]{2,40}$/u;
/** ?via=share is the share loop's own marker, never a person. */
const SHARE = 'share';

let via = '';
try {
  via = (new URLSearchParams(location.search).get('via') || '').trim();
} catch (e) {
  via = '';
}

if (via && via.toLowerCase() !== SHARE && HANDLE.test(via)) {
  try { localStorage.setItem(KEY, via); } catch (e) { /* private mode */ }
  const query = '?via=' + encodeURIComponent(via);
  for (const link of document.querySelectorAll('a[href="/"], a[href^="/#"]')) {
    const href = link.getAttribute('href');
    const hash = href.indexOf('#');
    link.setAttribute('href', hash < 0 ? '/' + query : '/' + query + href.slice(hash));
  }
}
