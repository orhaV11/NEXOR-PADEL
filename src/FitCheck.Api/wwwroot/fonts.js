// The two faces, off the critical path.
//
// index.html asks for the Google Fonts stylesheet with media="print": the browser still fetches it, at a low priority,
// but the first paint is no longer held on a third-party round trip. That wait was the one resource the service worker
// structurally cannot help with — sw.js returns early for every cross-origin request, so the stylesheet is fetched
// again on every cold start, even one where the whole shell came from the cache. On a captive portal or a saturated
// network that swallows the request instead of refusing it, the wait runs to the browser's own timeout and the person
// sits in front of a dark screen. app.css alone gates the first frame now, and --font-body's system-ui fallback draws
// it; display=swap in the URL swaps Outfit and Heebo in when they arrive.
//
// This is a file and not an inline handler because the policy is script-src 'self' with no 'unsafe-inline'
// (Services/Security/SecurityHeaders.cs) — an onload="" on the link would simply never run.
for (const link of document.querySelectorAll('link[data-font-css]')) link.media = 'all';
