// The offline page's behaviour: the retry button, and the three lines in the reader's own language.
//
// It lives in a file and not in offline.html because the policy is script-src 'self' with no 'unsafe-inline'
// (Services/Security/SecurityHeaders.cs, sent on every response including this static file). The onclick= and the
// <script> block the page used to carry were both refused by the browser: the big button did nothing at all, and the
// page stood in English for someone who had been reading the app in Hebrew. A cached response carries the policy it
// was fetched with, so that was as true when the service worker served the page from the cache as on the network.
//
// The file is in sw.js's SHELL right beside offline.html, and cache.addAll is atomic: wherever the page is, this is.
//
// What it can honestly do with no network at all: read the saved language (core.js writes prefs.language and the list
// of live languages, prefs.languages, at boot — the page cannot ask /api/config which ones are on), then read the
// locale file, which the service worker precaches for all four languages and answers from the cache when the network
// is gone. Nothing else is fetched: no fonts, no app JS. If any of it fails, English stands, which is what the page
// was written in.

(function () {
  'use strict';

  // Try again: the address the browser was asked for is still this page's address, so a reload is the retry.
  // (The button is also a form submit, which is what happens if this file is somehow not there: a scriptless
  // same-origin GET of the same address, without its #route. The handler below is the better of the two.)
  var retry = document.getElementById('retry');
  if (retry) {
    retry.addEventListener('click', function (event) {
      event.preventDefault();
      location.reload();
    });
  }

  var rtl = { he: 1, ar: 1 };
  var code = 'en';
  var live = ['en', 'he'];   // the languages the app last saw as live; English and Hebrew until it has said otherwise
  try {
    var prefs = JSON.parse(localStorage.getItem('orevosh.prefs') || 'null') || {};
    if (Array.isArray(prefs.languages) && prefs.languages.length) live = prefs.languages;
    code = prefs.language || (navigator.language || 'en').split('-')[0];
  } catch (e) { code = 'en'; }
  if (!/^(en|he|ar|ru)$/.test(code) || live.indexOf(code) < 0 || code === 'en') return;

  fetch('/i18n/' + code + '.json').then(function (r) { return r.ok ? r.json() : null; }).then(function (m) {
    if (!m || !m['offline.title']) return;
    document.documentElement.lang = code;
    document.documentElement.dir = rtl[code] ? 'rtl' : 'ltr';
    document.getElementById('title').textContent = m['offline.title'];
    document.getElementById('body').textContent = m['offline.body'];
    document.getElementById('retry').textContent = m['offline.retry'];
  }).catch(function () { /* English stands */ });
})();
