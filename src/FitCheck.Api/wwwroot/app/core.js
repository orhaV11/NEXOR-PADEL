// OREVOSH client core: state, i18n, API, DOM kit, router, shell, bottom sheets, gestures, look cards.
// Views live in ./views/*.js and register their routes with register(). No build step; ES modules only.

export const AVAILABLE_LOCALES = ['en', 'he'];   // adding a locale: drop i18n/<code>.json and add the code here
export const DEFAULT_LOCALE = 'en';
export const INTENTS = ['Casual', 'Date', 'Streetwear', 'OldMoney', 'Minimal', 'Office', 'Party', 'Sport'];
export const PAGE = 10;
export const MAX_EDGE = 1280;
export const AVATAR_EDGE = 320;
export const JPEG_QUALITY = 0.85;
const PREFS_KEY = 'orevosh.prefs';

const messages = {};
let locale = DEFAULT_LOCALE;

export const state = {
  me: null,
  route: { name: 'feed', params: {} },
  returnTo: null,
  feed: { tab: 'foryou', intent: '' },
  check: { intent: null, occasion: '', photo: null, previewUrl: null, photoBusy: false, photoToken: 0, busy: false, challenge: null, error: null },
  result: null,
  resultAnimated: false,
  resultPostId: null,
  sharing: false,
  installPrompt: null,
  forceRefresh: false,
  online: typeof navigator === 'undefined' ? true : navigator.onLine
};

export const $ = (id) => document.getElementById(id);
export const view = () => $('view');
export const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
export const isTouch = () => window.matchMedia('(pointer: coarse)').matches;
export const isStandalone = () => window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
export const isIos = () => /iphone|ipad|ipod/i.test(navigator.userAgent) && !window.MSStream;

export const ICONS = {
  flame: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 22c4.4 0 7-2.9 7-6.6 0-3.4-2.2-5.3-3.6-7.2-.4 1.6-1.2 2.6-2.4 3.2C13 9 12.4 5.7 9.3 3c.2 3-1.5 4.5-2.8 6.4A7.3 7.3 0 0 0 5 15.4C5 19.1 7.6 22 12 22z"/></svg>',
  flameFill: '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 22c4.4 0 7-2.9 7-6.6 0-3.4-2.2-5.3-3.6-7.2-.4 1.6-1.2 2.6-2.4 3.2C13 9 12.4 5.7 9.3 3c.2 3-1.5 4.5-2.8 6.4A7.3 7.3 0 0 0 5 15.4C5 19.1 7.6 22 12 22z"/></svg>',
  comment: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a8 8 0 0 1-8 8H8l-5 3 1.4-4.2A8 8 0 1 1 21 12z"/></svg>',
  bookmark: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 3h12v18l-6-4-6 4z"/></svg>',
  share: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 12v8h16v-8M12 16V3M7 8l5-5 5 5"/></svg>',
  more: '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg>',
  trophy: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4z"/><path d="M7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3"/></svg>',
  bag: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 8h12l1 13H5z"/><path d="M9 8V6a3 3 0 0 1 6 0v2"/></svg>',
  back: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 5l-7 7 7 7"/></svg>',
  search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>',
  globe: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/></svg>',
  sparkle: '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 2l1.8 6.2L20 10l-6.2 1.8L12 18l-1.8-6.2L4 10l6.2-1.8z"/></svg>',
  link: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/></svg>',
  flag: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 21V4h12l-2 4 2 4H5"/></svg>',
  trash: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/></svg>',
  camera: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 8h3l2-3h6l2 3h3v12H4z"/><circle cx="12" cy="13" r="3.5"/></svg>',
  settings: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>',
  check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m5 12 5 5L20 7"/></svg>',
  x: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>'
};

// ---------- i18n ----------

export function t(key, params) {
  let text = messages[locale] && messages[locale][key];
  if (text === undefined) {
    text = messages[DEFAULT_LOCALE] && messages[DEFAULT_LOCALE][key];
    if (text === undefined) { console.warn('i18n: missing key "' + key + '" in ' + locale + ' and ' + DEFAULT_LOCALE); return key; }
    if (locale !== DEFAULT_LOCALE) console.warn('i18n: missing key "' + key + '" in ' + locale + ', using ' + DEFAULT_LOCALE);
  }
  if (params && params.n === 1) {
    const one = (messages[locale] && messages[locale][key + '_one']) || (messages[DEFAULT_LOCALE] && messages[DEFAULT_LOCALE][key + '_one']);
    if (one !== undefined) text = one;
  }
  if (params) text = text.replace(/\{(\w+)\}/g, (m, name) => (name in params ? String(params[name]) : m));
  return text;
}
export const getLocale = () => locale;
export const isRtl = () => t('meta.dir') === 'rtl';
export const localeName = (code) => (messages[code] && messages[code]['meta.name']) || code;

function matchLocale(tag) {
  if (!tag) return null;
  const language = String(tag).toLowerCase().split(/[-_]/)[0];
  return AVAILABLE_LOCALES.includes(language) ? language : null;
}
function detectLocale() {
  const tags = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
  for (const tag of tags) { const match = matchLocale(tag); if (match) return match; }
  return DEFAULT_LOCALE;
}
async function loadLocale(code) {
  if (messages[code]) return;
  const response = await fetch('/i18n/' + code + '.json', { cache: 'no-cache' });
  if (!response.ok) throw new Error('Could not load locale ' + code);
  messages[code] = await response.json();
}
export function loadPrefs() { try { return JSON.parse(localStorage.getItem(PREFS_KEY) || 'null') || {}; } catch (e) { return {}; } }
export function savePrefs(prefs) { try { localStorage.setItem(PREFS_KEY, JSON.stringify({ ...loadPrefs(), ...prefs })); } catch (e) { /* private mode */ } }

function applyLocale(code) {
  locale = code;
  document.documentElement.lang = code;
  document.documentElement.dir = isRtl() ? 'rtl' : 'ltr';
  document.title = t('app.name');
  for (const node of document.querySelectorAll('[data-i18n]')) node.textContent = t(node.dataset.i18n);
  for (const node of document.querySelectorAll('[data-i18n-aria]')) node.setAttribute('aria-label', t(node.dataset.i18nAria));
}
export async function switchLocale(code) {
  await loadLocale(code);
  applyLocale(code);
  savePrefs({ language: code });
  renderShell();
  render(false);
  if (state.me) api('PATCH', '/api/users/me', { language: code }).catch(() => {});
}

// ---------- formatting ----------

const rtf = () => new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
export function fmtNumber(n) { return new Intl.NumberFormat(locale).format(n); }
export function fmtCompact(n) { return new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(n); }
export function fmtPercent(fraction) { return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(fraction); }
export function fmtDate(iso) { return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(iso)); }
export function relative(iso) {
  const diff = (new Date(iso).getTime() - Date.now()) / 1000;
  const abs = Math.abs(diff);
  const sign = diff < 0 ? -1 : 1;
  if (abs < 60) return rtf().format(sign * Math.round(abs), 'second');
  if (abs < 3600) return rtf().format(sign * Math.round(abs / 60), 'minute');
  if (abs < 86400) return rtf().format(sign * Math.round(abs / 3600), 'hour');
  if (abs < 86400 * 30) return rtf().format(sign * Math.round(abs / 86400), 'day');
  return fmtDate(iso);
}
export function intentLabel(intent) { return t('intent.' + intent); }

// ---------- DOM kit ----------

export function el(tag, attrs, children) {
  const node = document.createElement(tag);
  if (attrs) {
    for (const [key, value] of Object.entries(attrs)) {
      if (value === null || value === undefined || value === false) continue;
      if (key === 'class') node.className = value;
      else if (key === 'text') node.textContent = value;
      else if (key === 'icon') node.innerHTML = ICONS[value];   // trusted constant markup only
      else if (key === 'value') node.value = value;
      else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
      else node.setAttribute(key, value === true ? '' : value);
    }
  }
  if (children) {
    for (const child of [].concat(children)) {
      if (child === null || child === undefined || child === false) continue;
      node.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
    }
  }
  return node;
}
export function icon(name, extra) { return el('span', { class: 'icon ' + (extra || ''), icon: name }); }
export function iconButton(name, label, onclick, attrs) { return el('button', { type: 'button', class: 'icon-btn', 'aria-label': label, onclick, ...(attrs || {}) }, [icon(name)]); }
export function link(href, text, cls) { return el('a', { href, text, class: cls || undefined }); }

let toastTimer = null;
export function toast(message) {
  const node = $('toast');
  node.classList.add('show');
  node.textContent = message;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { node.classList.remove('show'); node.textContent = ''; }, 2400);
}
export function announce(text) {
  const live = $('live');
  live.textContent = '';
  requestAnimationFrame(() => { live.textContent = text; });
}
export function focusHeading() {
  requestAnimationFrame(() => {
    if (view().contains(document.activeElement) && document.activeElement !== document.body) return;
    const target = view().querySelector('h1, [tabindex="-1"]');
    if (target) { target.setAttribute('tabindex', '-1'); target.focus({ preventScroll: true }); }
  });
}
export function hue(text) { let h = 0; for (const ch of text || '') h = (h * 31 + ch.charCodeAt(0)) % 360; return h; }

/** Avatar circle: the photo when there is one, initials on a stable colour otherwise. Links to the profile unless noLink. */
export function avatar(user, opts) {
  opts = typeof opts === 'string' ? { size: opts } : (opts || {});
  const name = user.name || user.handle || '?';
  const initials = name.trim().slice(0, 2);
  const cls = 'avatar' + (opts.size ? ' ' + opts.size : '');
  const inner = user.avatarUrl ? el('img', { src: user.avatarUrl, alt: '', loading: 'lazy' }) : initials;
  const attrs = { class: cls, style: user.avatarUrl ? null : 'background: hsl(' + hue(user.handle) + ' 70% 65%)', 'aria-label': t('a11y.avatar', { name }) };
  if (opts.noLink) return el('span', attrs, [inner]);
  return el('a', { ...attrs, href: '#/u/' + encodeURIComponent(user.handle) }, [inner]);
}
export function brandMark(user) { return user && user.accountType === 'Brand' ? el('span', { class: 'brand-mark', text: t('profile.brand') }) : null; }
export function handleText(handle) { return el('bdi', { dir: 'ltr', text: '@' + handle }); }

/** #tags and @mentions become links; everything else stays text. known (optional) limits mention links to accounts that resolved. */
export function richCaption(text, known) {
  const frag = document.createDocumentFragment();
  const re = /(?<![\p{L}\p{N}_#@])(#[\p{L}\p{N}_]{2,30}|@[\p{L}\p{N}_.]{2,40})/gu;
  const allowed = known ? new Set(known.map((m) => (m.handle || m).toLowerCase())) : null;
  let last = 0;
  for (const m of (text || '').matchAll(re)) {
    if (m.index > last) frag.appendChild(document.createTextNode(text.slice(last, m.index)));
    const token = m[0];
    if (token[0] === '#') frag.appendChild(el('a', { href: '#/tag/' + encodeURIComponent(token.slice(1).toLowerCase()), text: token }));
    else {
      const handle = token.slice(1).replace(/\.+$/, '');
      if (allowed && !allowed.has(handle.toLowerCase())) frag.appendChild(document.createTextNode(token));
      else {
        frag.appendChild(el('a', { href: '#/u/' + encodeURIComponent(handle), text: '@' + handle }));
        if (handle.length < token.length - 1) frag.appendChild(document.createTextNode(token.slice(1 + handle.length)));
      }
    }
    last = m.index + token.length;
  }
  if (last < (text || '').length) frag.appendChild(document.createTextNode(text.slice(last)));
  return frag;
}

export function errorBlock(e) {
  return el('div', { class: 'notice' }, [
    el('p', { text: e && e.message ? e.message : t('error.generic') }),
    el('button', { type: 'button', class: 'btn btn-secondary', style: 'margin-block-start: 12px;', text: t('common.retry'), onclick: () => render(false) })
  ]);
}
export function signInPrompt(title, body) {
  const remember = () => { state.returnTo = location.hash; };
  return el('div', { class: 'notice' }, [
    el('h3', { text: title || t('auth.required_title') }),
    el('p', { class: 'muted', text: body || t('auth.required_body') }),
    el('div', { class: 'row', style: 'margin-block-start: 14px;' }, [
      el('a', { class: 'btn', href: '#/signup', text: t('auth.signup'), onclick: remember }),
      el('a', { class: 'btn btn-secondary', href: '#/login', text: t('auth.login'), onclick: remember })
    ])
  ]);
}
export function emptyState(title, body) {
  return el('p', { class: 'empty' }, [title ? el('b', { text: title }) : null, body || null]);
}
export function skeletonCards(n) {
  const wrap = el('div', { 'aria-hidden': 'true' });
  for (let i = 0; i < (n || 2); i++) {
    wrap.appendChild(el('div', { class: 'skel-card' }, [
      el('div', { class: 'skel-head' }, [el('div', { class: 'skel skel-avatar' }), el('div', { class: 'skel skel-line' })]),
      el('div', { class: 'skel skel-photo' })
    ]));
  }
  return wrap;
}

// ---------- API ----------

export class ApiError extends Error { constructor(status, message) { super(message); this.status = status; } }

export async function api(method, path, body) {
  const headers = { 'X-Requested-With': 'Orevosh', 'Accept-Language': locale };
  const isForm = body instanceof FormData;
  if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json';
  let response;
  try {
    response = await fetch(path, { method, headers, body: isForm ? body : body === undefined ? undefined : JSON.stringify(body), credentials: 'same-origin' });
  } catch (e) {
    throw new ApiError(0, t('error.network'));
  }
  if (response.status === 204) return null;
  let data = null;
  try { data = await response.json(); } catch (e) { data = null; }
  if (!response.ok) {
    if (response.status === 401 && state.me && !/^\/api\/auth\/(login|signup)/.test(path)) { state.me = null; resetSession(); renderShell(); render(false); }
    throw new ApiError(response.status, (data && data.error) || t('error.generic'));
  }
  if ((method === 'POST' && /^\/api\/posts\/?$/.test(path)) || (method === 'DELETE' && /^\/api\/posts\/[^/]+$/.test(path))) feedVersion.n += 1;
  return data;
}

/** Bumped whenever a look is created or deleted, so cached feeds know they are stale. */
export const feedVersion = { n: 0 };

export async function loadMe() {
  try { state.me = await api('GET', '/api/auth/me'); } catch (e) { if (e.status === 401) state.me = null; }
  renderShell();
}
export const isMe = (handle) => !!state.me && !!handle && state.me.handle.toLowerCase() === String(handle).toLowerCase();
export const isBrand = () => !!state.me && state.me.accountType === 'Brand';

/** Everything private to the person who just left: the photo, the check, the result, the pending challenge. */
export function resetSession() {
  const ck = state.check;
  if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
  Object.assign(ck, { intent: null, occasion: '', photo: null, previewUrl: null, photoBusy: false, photoToken: ck.photoToken + 1, busy: false, challenge: null, error: null });
  state.result = null; state.resultAnimated = false; state.resultPostId = null; state.returnTo = null;
}
export function navigate(hash) { if (location.hash === hash) render(true); else location.hash = hash; }
/** Like navigate, but replaces the current history entry: for guards and redirects, so Back does not loop. */
export function redirect(hash) { if (location.hash === hash) render(true); else location.replace(location.pathname + location.search + hash); }
/** Shows a message in an alert element and moves focus to it, so screen readers announce it on every platform. */
export function showAlert(node, message) { node.textContent = message; node.hidden = false; node.setAttribute('tabindex', '-1'); node.focus({ preventScroll: false }); }
export function requireSignIn(returnTo) {
  if (state.me) return true;
  state.returnTo = returnTo || location.hash;
  toast(t('auth.required_title'));
  location.hash = '#/login';
  return false;
}
export async function signOut() {
  try { await api('POST', '/api/auth/logout'); } catch (e) { /* cookie may already be gone */ }
  state.me = null; resetSession(); renderShell();
  toast(t('common.signed_out'));
  location.hash = '#/';
}

// ---------- shell: top bar, tab bar ----------

let topBarCustom = false;
export function setTopBar(opts) {
  topBarCustom = true;
  const inner = $('top-inner');
  inner.innerHTML = '';
  if (opts.back) inner.appendChild(iconButton('back', t('common.back'), () => { if (history.length > 1) history.back(); else location.hash = opts.back === true ? '#/' : opts.back; }, { class: 'icon-btn back' }));
  if (opts.title !== undefined) inner.appendChild(el('div', { class: 'top-title', text: opts.title }));
  else inner.appendChild(el('a', { class: 'wordmark', href: '#/', 'aria-label': t('app.name'), text: 'OREVOSH' }));
  inner.appendChild(el('div', { class: 'top-actions', id: 'top-actions' }, opts.actions || []));
}
function defaultTopBar() {
  topBarCustom = false;
  const inner = $('top-inner');
  inner.innerHTML = '';
  inner.appendChild(el('a', { class: 'wordmark', href: '#/', 'aria-label': t('app.name'), text: 'OREVOSH' }));
  const actions = el('div', { class: 'top-actions', id: 'top-actions' });
  actions.appendChild(iconButton('globe', t('lang.label'), openLanguageSheet, { id: 'lang' }));
  if (!state.me) actions.appendChild(el('a', { id: 'top-auth', class: 'pill accent', href: '#/signup', text: t('auth.signup'), onclick: () => { state.returnTo = location.hash; } }));
  inner.appendChild(actions);
}
export function openLanguageSheet() {
  const list = el('div', { class: 'sheet-list' }, AVAILABLE_LOCALES.map((code) => el('button', {
    type: 'button', lang: code, onclick: () => { close(); if (code !== locale) switchLocale(code).catch(() => toast(t('error.network'))); }
  }, [code === locale ? icon('check') : el('span', { class: 'icon', style: 'inline-size:22px' }), localeName(code)])));
  const { close } = sheet({ title: t('lang.label'), content: list });
}
export function renderShell() {
  if (!topBarCustom) defaultTopBar();
  const badge = $('activity-badge');
  const unread = state.me ? state.me.unreadNotifications : 0;
  badge.hidden = !unread;
  badge.textContent = unread > 99 ? '99+' : fmtNumber(unread);
  badge.setAttribute('aria-label', t('a11y.unread', { n: unread }));
  const active = tabFor(state.route);
  for (const tab of document.querySelectorAll('.tab')) {
    if (tab.dataset.tab === active) tab.setAttribute('aria-current', 'page'); else tab.removeAttribute('aria-current');
  }
}
function tabFor(route) {
  const name = route.name;
  if (['feed', 'post'].includes(name)) return 'home';
  if (['explore', 'search', 'tag', 'challenges', 'challenge', 'new-challenge'].includes(name)) return 'explore';
  if (['check', 'result'].includes(name)) return 'check';
  if (name === 'activity') return 'activity';
  if (['me', 'saved', 'settings', 'checks', 'login', 'signup', 'welcome'].includes(name)) return 'me';
  if (name === 'user') return isMe(route.params.handle) ? 'me' : '';
  return '';
}

// ---------- router ----------

const handlers = {};
/** Views call register('feed', async (root, params, ctx) => { ... }). ctx.stale() is true once a newer render started. */
export function register(name, handler) { handlers[name] = handler; }

export function parseRoute(hash) {
  const safeDecode = (part) => { try { return decodeURIComponent(part); } catch (e) { return part; } };
  const parts = (hash === undefined ? location.hash : hash).replace(/^#\/?/, '').split('/').filter(Boolean).map(safeDecode);
  const [head, a, b] = parts;
  switch (head || 'feed') {
    case 'feed': return { name: 'feed', params: { tab: a === 'following' ? 'following' : 'foryou' } };
    case 'explore': return { name: 'explore', params: {} };
    case 'search': return { name: 'search', params: { q: a || '' } };
    case 'tag': return { name: 'tag', params: { tag: (a || '').replace(/^#/, '').toLowerCase() } };
    case 'post': return { name: 'post', params: { id: a } };
    case 'u': return { name: 'user', params: { handle: a, tab: ['community', 'featured'].includes(b) ? b : 'looks' } };
    case 'challenges': return { name: 'challenges', params: { tab: a === 'ended' ? 'ended' : 'open' } };
    case 'challenge': return { name: 'challenge', params: { id: a } };
    case 'new-challenge': return { name: 'new-challenge', params: {} };
    case 'check': return { name: 'check', params: {} };
    case 'result': return { name: 'result', params: {} };
    case 'activity': return { name: 'activity', params: {} };
    case 'me': return { name: 'me', params: {} };
    case 'saved': return { name: 'saved', params: {} };
    case 'checks': return { name: 'checks', params: {} };
    case 'settings': return { name: 'settings', params: {} };
    case 'login': return { name: 'login', params: {} };
    case 'signup': return { name: 'signup', params: {} };
    case 'welcome': return { name: 'welcome', params: {} };
    default: return { name: 'feed', params: { tab: 'foryou' } };
  }
}

let renderToken = 0;
let viewCleanup = [];
/** Views register teardown work (observers, gesture handlers) that must stop when the view goes away. */
export function onLeave(fn) { viewCleanup.push(fn); }

export async function render(isNavigation) {
  state.route = parseRoute();
  document.documentElement.dataset.route = state.route.name;   // CSS hooks per route, e.g. [data-route="post"] .composer
  const token = ++renderToken;
  const stale = () => token !== renderToken;
  for (const fn of viewCleanup.splice(0)) { try { fn(); } catch (e) { /* teardown must never block a render */ } }
  closeSheet();
  topBarCustom = false;
  renderShell();
  const root = view();
  root.className = 'view';
  root.innerHTML = '';
  const handler = handlers[state.route.name] || handlers.feed;
  try {
    await handler(root, state.route.params, { stale });
  } catch (e) {
    if (stale()) return;
    root.appendChild(errorBlock(e));
  }
  if (isNavigation !== false && !stale()) { window.scrollTo({ top: 0 }); focusHeading(); }
}

// ---------- bottom sheet ----------

let openSheetNode = null;
export function sheet(opts) {
  closeSheet();
  const previouslyFocused = document.activeElement;
  const backdrop = el('div', { class: 'sheet-backdrop', onclick: () => close() });
  const panel = el('div', { class: 'sheet', role: 'dialog', 'aria-modal': 'true', 'aria-label': opts.title || '' }, [
    el('div', { class: 'handle', 'aria-hidden': 'true' }),
    opts.title ? el('h3', { text: opts.title }) : null,
    opts.content || null
  ]);
  document.body.appendChild(backdrop);
  document.body.appendChild(panel);
  document.body.classList.add('sheet-open');
  // The page behind is inert while the sheet is open, so neither taps nor Tab reach it.
  const behind = [view(), document.querySelector('header.top'), document.querySelector('nav.tabbar')].filter(Boolean);
  for (const node of behind) node.inert = true;
  let closed = false;
  // Swipe down to dismiss, but only when the drag starts on the sheet's own chrome, never inside a field or a scrolled list.
  let startY = null;
  panel.addEventListener('touchstart', (e) => {
    if (panel.scrollTop > 0 || e.target.closest('input, textarea, select, button, a, [contenteditable="true"]')) { startY = null; return; }
    startY = e.touches[0].clientY;
  }, { passive: true });
  panel.addEventListener('touchmove', (e) => { if (startY !== null) { const dy = e.touches[0].clientY - startY; if (dy > 0) panel.style.transform = 'translateY(' + dy + 'px)'; } }, { passive: true });
  panel.addEventListener('touchend', (e) => { if (startY === null) return; const dy = e.changedTouches[0].clientY - startY; startY = null; if (dy > 80) close(); else panel.style.transform = ''; });
  const focusables = () => [...panel.querySelectorAll('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')];
  const onKey = (e) => {
    if (e.key === 'Escape') { close(); return; }
    if (e.key !== 'Tab') return;
    const items = focusables();
    if (!items.length) { e.preventDefault(); return; }
    const first = items[0]; const last = items[items.length - 1];
    if (!panel.contains(document.activeElement)) { e.preventDefault(); first.focus(); }
    else if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  };
  document.addEventListener('keydown', onKey);
  const first = panel.querySelector('[data-autofocus]') || focusables()[0];
  if (first) requestAnimationFrame(() => { if (!closed) first.focus({ preventScroll: true }); });
  function close(immediate) {
    if (closed) return;
    closed = true;
    document.removeEventListener('keydown', onKey);
    for (const node of behind) node.inert = false;
    if (openSheetNode && openSheetNode.panel === panel) openSheetNode = null;
    document.body.classList.remove('sheet-open');
    const done = () => { panel.remove(); backdrop.remove(); };
    if (immediate || reducedMotion()) done();
    else { panel.classList.add('closing'); backdrop.classList.add('closing'); setTimeout(done, 170); }
    if (previouslyFocused && previouslyFocused.focus && document.contains(previouslyFocused)) previouslyFocused.focus({ preventScroll: true });
    if (opts.onClose) opts.onClose();
  }
  openSheetNode = { panel, close };
  return { close, panel };
}
export function closeSheet() {
  if (!openSheetNode) return;
  openSheetNode.close(true);
}
/** A sheet of tappable rows: [{ icon, text, onclick, danger, href }]. */
export function actionSheet(title, items) {
  const list = el('div', { class: 'sheet-list' });
  const s = sheet({ title, content: list });
  for (const item of items.filter(Boolean)) {
    const node = item.href
      ? el('a', { href: item.href, class: item.danger ? 'danger' : null, onclick: () => s.close() }, [icon(item.icon), item.text])
      : el('button', { type: 'button', class: item.danger ? 'danger' : null, onclick: () => { s.close(); item.onclick(); } }, [icon(item.icon), item.text]);
    list.appendChild(node);
  }
  return s;
}
export function confirmSheet(title, body, confirmText, danger) {
  return new Promise((resolve) => {
    let answered = false;
    const s = sheet({
      title, onClose: () => { if (!answered) resolve(false); },
      content: el('div', {}, [
        body ? el('p', { class: 'muted', text: body }) : null,
        el('div', { class: 'stack', style: 'margin-block-start:16px' }, [
          el('button', { type: 'button', class: 'btn ' + (danger ? 'btn-danger' : ''), text: confirmText, onclick: () => { answered = true; s.close(); resolve(true); } }),
          el('button', { type: 'button', class: 'btn btn-secondary', 'data-autofocus': true, text: t('common.cancel'), onclick: () => { answered = true; s.close(); resolve(false); } })
        ])
      ])
    });
  });
}

// ---------- gestures & lists ----------

/** Fires handler on two taps within 300ms on node; a single tap runs single after the window (so links can still navigate). */
export function doubleTap(node, handler, single) {
  let last = 0; let timer = null;
  node.addEventListener('click', (event) => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;   // open in a new tab etc.
    const now = performance.now();
    if (now - last < 300) { last = 0; clearTimeout(timer); event.preventDefault(); handler(event); return; }
    last = now;
    if (single) {
      event.preventDefault();
      const hashAtTap = location.hash;
      timer = setTimeout(() => { if (location.hash === hashAtTap) single(event); }, 280);
    }
  });
}

/** Pull down at the top of the page to refresh. Touch only; the indicator node is placed by the view. */
export function pullToRefresh(indicator, onRefresh) {
  if (!isTouch()) return () => {};
  let startY = null; let pulling = false; let armed = false;
  const label = el('span', { text: t('common.pull') });
  const mark = el('div', { class: 'loading-mark', hidden: true });
  indicator.appendChild(label); indicator.appendChild(mark);
  const onStart = (e) => { if (window.scrollY <= 0 && !document.body.classList.contains('sheet-open')) { startY = e.touches[0].clientY; pulling = true; } };
  const onMove = (e) => {
    if (!pulling) return;
    const dy = e.touches[0].clientY - startY;
    if (dy <= 0 || window.scrollY > 0) { indicator.style.blockSize = '0px'; armed = false; return; }
    const size = Math.min(dy * 0.5, 72);
    indicator.style.blockSize = size + 'px';
    armed = dy > 110;
    indicator.classList.toggle('armed', armed);
    label.textContent = t(armed ? 'common.release' : 'common.pull');
  };
  const onEnd = async () => {
    if (!pulling) return;
    pulling = false;
    if (!armed) { indicator.style.blockSize = '0px'; return; }
    armed = false; indicator.classList.remove('armed');
    label.hidden = true; mark.hidden = false; indicator.style.blockSize = '56px';
    try { await onRefresh(); } finally { indicator.style.blockSize = '0px'; label.hidden = false; mark.hidden = true; }
  };
  document.addEventListener('touchstart', onStart, { passive: true });
  document.addEventListener('touchmove', onMove, { passive: true });
  document.addEventListener('touchend', onEnd);
  const cleanup = () => { document.removeEventListener('touchstart', onStart); document.removeEventListener('touchmove', onMove); document.removeEventListener('touchend', onEnd); };
  onLeave(cleanup);
  return cleanup;
}

/**
 * A paged list that loads more as you scroll. opts: load(offset) -> { items, nextOffset }, render(item) -> node,
 * empty() -> node, key(item) -> id (drops repeats across pages), initial { items, nextOffset } (restores a snapshot
 * without a request), skeleton (bool), stale() from the view. Returns { refresh, snapshot, list }.
 */
export function infiniteList(container, opts) {
  const list = el('div', { class: opts.className || '' });
  const sentinel = el('div', { class: 'sentinel', 'aria-hidden': 'true' });
  const more = el('div', { class: 'empty', hidden: true });
  container.appendChild(list); container.appendChild(more); container.appendChild(sentinel);
  let seq = 0; let next = 0; let loading = false; let items = []; let seen = new Set(); let errorNode = null;
  function append(page) {
    for (const item of page) {
      const id = opts.key ? opts.key(item) : null;
      if (id !== null && seen.has(id)) continue;       // the ranking moved under us between pages
      if (id !== null) seen.add(id);
      items.push(item);
      list.appendChild(opts.render(item));
    }
  }
  function finish() {
    if (items.length === 0) list.appendChild(opts.empty ? opts.empty() : emptyState(t('feed.empty')));
    else if (next === null && opts.endText !== false) { more.textContent = opts.endText || t('feed.end'); more.hidden = items.length < 3; }
  }
  async function loadPage(reset) {
    if (reset) {
      seq += 1; next = 0; items = []; seen = new Set(); errorNode = null; loading = false;   // an in-flight page is now stale and will be dropped
      list.innerHTML = ''; more.hidden = true;
      if (opts.skeleton !== false) list.appendChild(skeletonCards(2));
    }
    if (next === null || loading) return;
    const mine = seq; const offset = next;
    loading = true;
    try {
      const page = await opts.load(offset);
      if (mine !== seq || (opts.stale && opts.stale())) return;
      if (reset) list.innerHTML = '';
      if (errorNode) { errorNode.remove(); errorNode = null; }
      append(page.items);
      next = page.nextOffset === null || page.nextOffset === undefined ? null : page.nextOffset;
      finish();
    } catch (e) {
      if (mine !== seq) return;
      if (reset) list.innerHTML = '';
      if (errorNode) errorNode.remove();
      errorNode = errorBlock(e);
      list.appendChild(errorNode);
      next = offset;
    } finally { if (mine === seq) loading = false; }
  }
  const observer = new IntersectionObserver((entries) => { if (entries.some((x) => x.isIntersecting) && next !== null && !loading) loadPage(false); }, { rootMargin: '600px 0px' });
  observer.observe(sentinel);
  onLeave(() => observer.disconnect());
  if (opts.initial && Array.isArray(opts.initial.items)) {
    append(opts.initial.items);
    next = opts.initial.nextOffset === null || opts.initial.nextOffset === undefined ? null : opts.initial.nextOffset;
    finish();
  } else {
    loadPage(true);
  }
  return { refresh: () => loadPage(true), snapshot: () => ({ items: items.slice(), nextOffset: next }), list };
}

// ---------- images ----------

const pendingPicks = new Map();
export function pickFile(inputId) {
  const input = $(inputId);
  const previous = pendingPicks.get(inputId);
  if (previous) previous(null);                       // a pick that was cancelled without a cancel event resolves now
  return new Promise((resolve) => {
    const done = (file) => {
      if (pendingPicks.get(inputId) !== done) return;
      pendingPicks.delete(inputId);
      input.removeEventListener('change', onChange);
      input.removeEventListener('cancel', onCancel);
      resolve(file);
    };
    const onChange = () => { const file = input.files && input.files[0]; input.value = ''; done(file || null); };
    const onCancel = () => done(null);
    pendingPicks.set(inputId, done);
    input.addEventListener('change', onChange);
    input.addEventListener('cancel', onCancel);
    input.click();
  });
}
async function decodeImage(file) {
  if (typeof createImageBitmap === 'function') {
    try { return await createImageBitmap(file, { imageOrientation: 'from-image' }); }
    catch (e) { if (!(e instanceof TypeError)) throw e; }
    return await createImageBitmap(file);
  }
  const url = URL.createObjectURL(file);
  try {
    const img = new Image();
    img.src = url;
    if (img.decode) await img.decode(); else await new Promise((resolve, reject) => { img.onload = resolve; img.onerror = reject; });
    return img;
  } finally { URL.revokeObjectURL(url); }
}
/** Downscale before upload: less data on mobile networks, fewer input tokens, EXIF orientation baked in. square=true crops to the centre. */
export async function prepareImage(file, maxEdge, square) {
  try {
    const source = await decodeImage(file);
    const sw = source.width || source.naturalWidth; const sh = source.height || source.naturalHeight;
    let sx = 0, sy = 0, cw = sw, ch = sh;
    if (square) { const side = Math.min(sw, sh); sx = Math.floor((sw - side) / 2); sy = Math.floor((sh - side) / 2); cw = side; ch = side; }
    const scale = Math.min(1, (maxEdge || MAX_EDGE) / Math.max(cw, ch));
    const width = Math.max(1, Math.round(cw * scale)); const height = Math.max(1, Math.round(ch * scale));
    const canvas = document.createElement('canvas');
    canvas.width = width; canvas.height = height;
    canvas.getContext('2d').drawImage(source, sx, sy, cw, ch, 0, 0, width, height);
    if (source.close) source.close();
    return await new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('toBlob failed'))), 'image/jpeg', JPEG_QUALITY));
  } catch (e) {
    console.warn('Client-side downscale failed, sending the original file', e);
    return file;
  }
}

// ---------- looks ----------

export async function toggleFire(post, button) {
  if (!requireSignIn()) return;
  const was = post.fired;
  post.fired = !was;
  post.fireCount += was ? -1 : 1;
  paintFire(button, post);
  if (post.fired && !reducedMotion()) { button.classList.remove('burst'); void button.offsetWidth; button.classList.add('burst'); }
  try {
    const result = await api(was ? 'DELETE' : 'POST', '/api/posts/' + post.id + '/fire');
    post.fireCount = result.fireCount; post.fired = result.fired;
  } catch (e) {
    post.fired = was; post.fireCount += was ? 1 : -1;
    toast(e.message);
  }
  paintFire(button, post);
}
function paintFire(button, post) {
  if (!button) return;
  button.setAttribute('aria-pressed', String(post.fired));
  const name = button.querySelector('.sr-only');
  if (name) name.textContent = post.fired ? t('post.fired') : t('post.fire');
  const count = button.querySelector('.count');
  if (count) count.textContent = fmtCompact(post.fireCount);
}
export async function toggleSave(post, button) {
  if (!requireSignIn()) return;
  const was = post.saved;
  post.saved = !was;
  if (button) button.setAttribute('aria-pressed', String(post.saved));
  try {
    const result = await api(was ? 'DELETE' : 'POST', '/api/posts/' + post.id + '/save');
    post.saved = result.saved;
    toast(post.saved ? t('post.saved') : t('post.unsaved'));
  } catch (e) { post.saved = was; toast(e.message); }
  if (button) { button.setAttribute('aria-pressed', String(post.saved)); button.setAttribute('aria-label', post.saved ? t('post.saved') : t('post.save')); }
}
export function postUrl(post) { return location.origin + '/#/post/' + post.id; }
export async function sharePost(post) {
  if (state.sharing) return;
  state.sharing = true;
  try {
    const url = postUrl(post);
    const text = t('post.share_text', { name: post.user.name, intent: intentLabel(post.intent), score: fmtNumber(post.score) });
    if (navigator.share) {
      try { await navigator.share({ title: t('app.name'), text, url }); return; }
      catch (e) { if (e && (e.name === 'AbortError' || e.name === 'InvalidStateError')) return; }
    }
    await copyText(url, t('post.copied'));
  } finally { state.sharing = false; }
}
export async function copyText(text, doneMessage) {
  try { await navigator.clipboard.writeText(text); toast(doneMessage || t('common.copied')); }
  catch (e) { window.prompt(t('common.copy_link'), text); }
}
export async function reportPost(post) {
  if (!requireSignIn()) return;
  if (!await confirmSheet(t('post.report'), t('post.report_confirm'), t('post.report'), true)) return;
  try { await api('POST', '/api/posts/' + post.id + '/report', { reason: 'reported from app' }); toast(t('post.reported')); }
  catch (e) { toast(e.message); }
}
export async function deletePost(post) {
  if (!await confirmSheet(t('post.delete'), t('post.delete_confirm'), t('post.delete'), true)) return false;
  try { await api('DELETE', '/api/posts/' + post.id); toast(t('post.deleted')); return true; }
  catch (e) { toast(e.message); return false; }
}
export async function featurePost(post, on) {
  if (!requireSignIn()) return false;
  try {
    const result = await api(on ? 'POST' : 'DELETE', '/api/posts/' + post.id + '/feature');
    post.featuredBy = result.featuredBy || null;
    toast(t(on ? 'post.featured_toast' : 'post.unfeatured_toast'));
    return true;
  } catch (e) { toast(e.message); return false; }
}
export async function toggleFollow(handle, following) {
  return await api(following ? 'DELETE' : 'POST', '/api/users/' + encodeURIComponent(handle) + '/follow');
}
/** The "…" sheet for a look: share, copy link, save, feature (brands), report, delete (author). */
export function openPostMenu(post, opts) {
  opts = opts || {};
  const mentionsMe = state.me && (post.mentions || []).some((m) => m.handle.toLowerCase() === state.me.handle.toLowerCase());
  const featuredByMe = state.me && post.featuredBy && post.featuredBy.handle.toLowerCase() === state.me.handle.toLowerCase();
  const canFeature = isBrand() && !post.isMine && (mentionsMe || post.challengeId) && !post.featuredBy;
  actionSheet(t('post.menu_title'), [
    { icon: 'share', text: t('post.share'), onclick: () => sharePost(post) },
    { icon: 'link', text: t('common.copy_link'), onclick: () => copyText(postUrl(post), t('post.copied')) },
    { icon: 'bookmark', text: post.saved ? t('post.unsave') : t('post.save'), onclick: () => toggleSave(post, opts.saveButton) },
    canFeature ? { icon: 'sparkle', text: t('post.feature'), onclick: async () => { if (await featurePost(post, true) && opts.onChange) opts.onChange(); } } : null,
    featuredByMe ? { icon: 'sparkle', text: t('post.unfeature'), onclick: async () => { if (await featurePost(post, false) && opts.onChange) opts.onChange(); } } : null,
    !post.isMine ? { icon: 'flag', text: t('post.report'), onclick: () => reportPost(post), danger: true } : null,
    post.isMine ? { icon: 'trash', text: t('post.delete'), onclick: async () => { if (await deletePost(post) && opts.onDelete) opts.onDelete(); }, danger: true } : null
  ]);
}

/** A look card. opts: inChallenge, votes, onDelete, onChange, compact (no caption/match). */
export function postCard(post, opts) {
  opts = opts || {};
  const user = post.user;
  const saveBtn = el('button', { type: 'button', class: 'action save', 'aria-pressed': String(post.saved), 'aria-label': post.saved ? t('post.saved') : t('post.save'), onclick: () => toggleSave(post, saveBtn) }, [icon('bookmark')]);
  // The visible caps labels (.lbl) are aria-hidden: the sr-only spans already say "On fire" / "Comments".
  const fireBtn = el('button', { type: 'button', class: 'action fire', 'aria-pressed': String(post.fired), onclick: () => toggleFire(post, fireBtn) },
    [icon('flame'), el('span', { class: 'sr-only', text: post.fired ? t('post.fired') : t('post.fire') }), el('span', { class: 'count', text: fmtCompact(post.fireCount) }), el('span', { class: 'lbl', 'aria-hidden': 'true', text: t('post.fire') })]);
  const head = el('div', { class: 'card-head' }, [
    avatar(user),
    el('div', { class: 'who' }, [
      el('a', { class: 'name', href: '#/u/' + encodeURIComponent(user.handle), style: 'text-decoration:none' }, [user.name, brandMark(user)]),
      el('div', { class: 'sub' }, [handleText(user.handle), ' · ' + relative(post.createdAt)])
    ]),
    el('span', { class: 'tag', text: intentLabel(post.intent) }),
    el('button', { type: 'button', class: 'icon-btn menu-open', 'aria-label': t('common.more'), onclick: () => openPostMenu(post, { saveButton: saveBtn, onDelete: opts.onDelete, onChange: opts.onChange }) }, [icon('more')])
  ]);
  const photo = el('a', { class: 'card-photo', href: '#/post/' + post.id, 'aria-label': t('a11y.look_by', { intent: intentLabel(post.intent), name: user.name }) }, [
    el('img', { src: post.imageUrl, alt: '', loading: opts.eager ? 'eager' : 'lazy', decoding: 'async' }),
    el('span', { class: 'score-badge', 'aria-hidden': 'true' }, [fmtNumber(post.score), el('small', { text: t('result.out_of') })])
  ]);
  doubleTap(photo, () => {
    if (!post.fired) toggleFire(post, fireBtn);
    if (!reducedMotion()) { const burst = el('span', { class: 'burst-flame', icon: 'flameFill' }); photo.appendChild(burst); setTimeout(() => burst.remove(), 750); }
  }, opts.noOpen ? null : () => { location.hash = '#/post/' + post.id; });
  const body = el('div', { class: 'card-body' }, [
    post.hidden ? el('p', { class: 'alert danger', text: t('post.hidden') + ' · ' + t('post.hidden_hint') }) : null,
    el('p', { class: 'headline', text: post.headline }),
    post.caption ? el('p', { class: 'caption' }, [richCaption(post.caption, post.mentions)]) : null,
    post.featuredBy ? el('a', { class: 'featured', href: '#/u/' + encodeURIComponent(post.featuredBy.handle) + '/featured' }, [icon('sparkle'), t('post.featured_by', { name: post.featuredBy.name })]) : null,
    opts.compact ? null : el('div', { class: 'match' }, [
      el('span', { text: t('post.reads_as', { intent: intentLabel(post.intent), pct: fmtPercent(post.intentMatch / 100) }) }),
      el('div', { class: 'bar' }, [el('div', { class: 'bar-fill', style: 'inline-size:' + post.intentMatch + '%' })])
    ]),
    post.challengeId && post.challengeTitle && !opts.inChallenge ? el('a', { class: 'challenge-link', href: '#/challenge/' + post.challengeId, text: t('post.in_challenge', { title: post.challengeTitle }) }) : null,
    post.products && post.products.length ? el('div', { class: 'shop' }, post.products.map((p) => el('a', { href: p.url, target: '_blank', rel: 'noopener' }, [icon('bag'), p.label, p.price ? el('b', { text: p.price }) : null]))) : null
  ]);
  const actions = el('div', { class: 'actions' }, [
    fireBtn,
    el('a', { class: 'action comments', href: '#/post/' + post.id }, [icon('comment'), el('span', { class: 'sr-only', text: t('post.comments') }), el('span', { class: 'count', text: fmtCompact(post.commentCount) }), el('span', { class: 'lbl', 'aria-hidden': 'true', text: t('post.comments_label', { n: post.commentCount }) })]),
    saveBtn,
    el('button', { type: 'button', class: 'action', 'aria-label': t('post.share'), onclick: () => sharePost(post) }, [icon('share')]),
    opts.votes !== undefined ? el('span', { class: 'tag accent end', text: t('post.votes', { n: fmtNumber(opts.votes) }) }) : null
  ]);
  return el('article', { class: 'card', 'data-post': post.id }, [head, photo, body, actions]);
}

/** A person or brand row with a follow button. card is UserCardDto ({ user, followers, posts, following }) or a bare UserRefDto. */
export function userRow(card, opts) {
  opts = opts || {};
  const user = card.user || card;
  const mine = isMe(user.handle);
  const sub = card.user
    ? el('div', { class: 'sub' }, [handleText(user.handle), ' · ' + t('profile.followers_n', { n: card.followers === 1 ? 1 : fmtCompact(card.followers) })])
    : el('div', { class: 'sub' }, [handleText(user.handle)]);
  const row = el('div', { class: 'person' }, [
    avatar(user),
    el('div', { class: 'who' }, [el('a', { class: 'name', href: '#/u/' + encodeURIComponent(user.handle), style: 'text-decoration:none' }, [user.name, brandMark(user)]), sub])
  ]);
  if (!mine && card.user && opts.follow !== false) row.appendChild(followButton(user.handle, card.following, (r) => { card.following = r.following; card.followers = r.followers; }));
  return row;
}
/** The follow label: brass FOLLOW when it invites, outlined "✓ FOLLOWING" (the kit's check icon, in brass) when pressed. aria-pressed drives the state. */
export function followButton(handle, following, onChange, opts) {
  opts = opts || {};
  const btn = el('button', { type: 'button', class: 'btn btn-sm' + (following ? ' btn-secondary' : ''), 'aria-pressed': String(following) });
  const paint = () => {
    btn.innerHTML = '';
    if (following) btn.appendChild(icon('check'));
    btn.appendChild(document.createTextNode(t(following ? 'profile.unfollow' : 'profile.follow')));
    btn.setAttribute('aria-pressed', String(following));
    btn.classList.toggle('btn-secondary', following);
  };
  paint();
  btn.addEventListener('click', async () => {
    if (!requireSignIn()) return;
    btn.disabled = true;
    try {
      const result = await toggleFollow(handle, following);
      following = result.following;
      paint();
      if (onChange) onChange(result);
    } catch (e) { toast(e.message); }
    btn.disabled = false;
  });
  return btn;
}
/**
 * A grid of framed prints. opts.wall → the two-column staggered atelier wall (class "grid wall"); opts.captions → each
 * cell is a > figure(img, stamp, private?) + figcaption(rank, name, intent), the rank drawn by a CSS counter.
 */
export function postGrid(posts, opts) {
  opts = opts || {};
  const stamp = (p) => el('span', { class: 'score-badge', 'aria-hidden': 'true' }, [fmtNumber(p.score), el('small', { text: t('result.out_of') })]);
  const print = (p) => [
    el('img', { src: p.imageUrl, alt: '', loading: 'lazy', decoding: 'async' }),
    stamp(p),
    p.hidden ? el('span', { class: 'tag private', text: t('post.hidden') }) : null
  ];
  return el('div', { class: 'grid' + (opts.wall ? ' wall' : '') }, posts.map((p) => el('a', { href: '#/post/' + p.id, 'aria-label': t('a11y.look_by', { intent: intentLabel(p.intent), name: p.user.name }) },
    opts.captions
      ? [el('figure', {}, print(p)), el('figcaption', {}, [el('span', { class: 'rank', 'aria-hidden': 'true' }), el('b', { text: p.user.name }), el('span', { text: intentLabel(p.intent) })])]
      : print(p))));
}

// ---------- install banner ----------

export function installBanner() {
  const prefs = loadPrefs();
  if (isStandalone() || prefs.installDismissed) return null;
  const ios = isIos() && !state.installPrompt;
  if (!state.installPrompt && !ios) return null;
  const node = el('div', { class: 'install' }, [
    el('div', { class: 'mark', text: 'O', 'aria-hidden': 'true' }),
    el('div', { class: 'text' }, [el('b', { text: t('pwa.install_title') }), el('span', { text: ios ? t('pwa.install_ios') : t('pwa.install_body') })]),
    ios ? null : el('button', { type: 'button', class: 'btn btn-sm', text: t('pwa.install'), onclick: async () => { const p = state.installPrompt; if (!p) return; p.prompt(); try { await p.userChoice; } catch (e) { /* dismissed */ } state.installPrompt = null; node.remove(); } }),
    iconButton('x', t('pwa.later'), () => { savePrefs({ installDismissed: true }); node.remove(); })
  ]);
  return node;
}

// ---------- boot ----------

export async function boot() {
  const prefs = loadPrefs();
  const initial = matchLocale(prefs.language) || detectLocale();
  await Promise.all(AVAILABLE_LOCALES.map((code) => loadLocale(code).catch((e) => console.warn(e))));
  if (!messages[DEFAULT_LOCALE]) messages[DEFAULT_LOCALE] = {};
  applyLocale(messages[initial] ? initial : DEFAULT_LOCALE);
  window.addEventListener('hashchange', () => render(true));
  for (const tab of document.querySelectorAll('.tab')) {
    tab.addEventListener('click', (event) => {
      if (tab.getAttribute('href') === location.hash || (tab.dataset.tab === 'home' && (location.hash === '' || location.hash === '#/'))) { event.preventDefault(); state.forceRefresh = true; window.scrollTo({ top: 0, behavior: reducedMotion() ? 'auto' : 'smooth' }); render(true); }
    });
  }
  window.addEventListener('beforeinstallprompt', (event) => { event.preventDefault(); state.installPrompt = event; });
  const offline = () => { state.online = navigator.onLine; const bar = $('offline'); bar.hidden = state.online; bar.textContent = t('pwa.offline'); };
  window.addEventListener('online', offline); window.addEventListener('offline', offline); offline();
  if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost' || location.hostname === '127.0.0.1')) {
    navigator.serviceWorker.register('/sw.js').catch((e) => console.warn('service worker', e));
  }
  await loadMe();
  if (state.me && matchLocale(state.me.language) && state.me.language !== locale && !prefs.language) await switchLocale(state.me.language);
  await render(true);
}
