(function () {
  'use strict';

  // Adding a locale: drop i18n/<code>.json (with meta.name and meta.dir) and add the code here.
  const AVAILABLE_LOCALES = ['en', 'he'];
  const DEFAULT_LOCALE = 'en';
  const INTENTS = ['Casual', 'Date', 'Streetwear', 'OldMoney', 'Minimal', 'Office', 'Party', 'Sport'];
  const PREFS_KEY = 'fitcheck.prefs';
  const MAX_EDGE = 1280;
  const JPEG_QUALITY = 0.85;
  const PAGE = 10;

  const messages = {};
  let locale = DEFAULT_LOCALE;

  const state = {
    me: null,
    route: { name: 'feed', params: {} },
    feed: { tab: 'fresh', intent: '', items: [], next: null, seq: 0 },
    challenges: { tab: 'open' },
    check: { intent: null, occasion: '', photo: null, previewUrl: null, photoBusy: false, photoToken: 0, busy: false, challenge: null },
    result: null,
    resultAnimated: false,
    resultPostId: null,
    sharing: false
  };

  const $ = (id) => document.getElementById(id);
  const view = () => $('view');
  const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const ICONS = {
    flame: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 22c4.4 0 7-2.9 7-6.6 0-3.4-2.2-5.3-3.6-7.2-.4 1.6-1.2 2.6-2.4 3.2C13 9 12.4 5.7 9.3 3c.2 3-1.5 4.5-2.8 6.4A7.3 7.3 0 0 0 5 15.4C5 19.1 7.6 22 12 22z"/></svg>',
    comment: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a8 8 0 0 1-8 8H8l-5 3 1.4-4.2A8 8 0 1 1 21 12z"/></svg>',
    bookmark: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 3h12v18l-6-4-6 4z"/></svg>',
    share: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 12v8h16v-8M12 16V3M7 8l5-5 5 5"/></svg>',
    more: '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg>',
    trophy: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4z"/><path d="M7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3"/></svg>',
    bag: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 8h12l1 13H5z"/><path d="M9 8V6a3 3 0 0 1 6 0v2"/></svg>'
  };

  // ---------- i18n ----------

  function t(key, params) {
    let text = messages[locale] && messages[locale][key];
    if (text === undefined) {
      text = messages[DEFAULT_LOCALE] && messages[DEFAULT_LOCALE][key];
      if (text === undefined) {
        console.warn('i18n: missing key "' + key + '" in ' + locale + ' and ' + DEFAULT_LOCALE);
        return key;
      }
      if (locale !== DEFAULT_LOCALE) console.warn('i18n: missing key "' + key + '" in ' + locale + ', using ' + DEFAULT_LOCALE);
    }
    if (params) text = text.replace(/\{(\w+)\}/g, (m, name) => (name in params ? String(params[name]) : m));
    return text;
  }

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

  function loadPrefs() { try { return JSON.parse(localStorage.getItem(PREFS_KEY) || 'null') || {}; } catch (e) { return {}; } }
  function savePrefs(prefs) { try { localStorage.setItem(PREFS_KEY, JSON.stringify({ ...loadPrefs(), ...prefs })); } catch (e) { /* private mode */ } }

  function applyLocale(code) {
    locale = code;
    document.documentElement.lang = code;
    document.documentElement.dir = t('meta.dir') === 'rtl' ? 'rtl' : 'ltr';
    document.title = t('app.name');
    for (const node of document.querySelectorAll('[data-i18n]')) node.textContent = t(node.dataset.i18n);
    $('lang').value = code;
    renderShell();
    render(false);
  }

  async function switchLocale(code) {
    await loadLocale(code);
    applyLocale(code);
    savePrefs({ language: code });
    if (state.me) api('PATCH', '/api/users/me', { language: code }).catch(() => {});
  }

  function renderLanguageSelect() {
    const select = $('lang');
    select.innerHTML = '';
    for (const code of AVAILABLE_LOCALES) {
      const option = document.createElement('option');
      option.value = code;
      option.textContent = (messages[code] && messages[code]['meta.name']) || code;
      option.lang = code;
      select.appendChild(option);
    }
    select.value = locale;
  }

  // ---------- formatting ----------

  const rtf = () => new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  function fmtNumber(n) { return new Intl.NumberFormat(locale).format(n); }
  function fmtPercent(fraction) { return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(fraction); }
  function fmtDate(iso) { return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(iso)); }
  function relative(iso) {
    const diff = (new Date(iso).getTime() - Date.now()) / 1000;
    const abs = Math.abs(diff);
    const sign = diff < 0 ? -1 : 1;
    if (abs < 60) return rtf().format(sign * Math.round(abs), 'second');
    if (abs < 3600) return rtf().format(sign * Math.round(abs / 60), 'minute');
    if (abs < 86400) return rtf().format(sign * Math.round(abs / 3600), 'hour');
    if (abs < 86400 * 30) return rtf().format(sign * Math.round(abs / 86400), 'day');
    return fmtDate(iso);
  }

  // ---------- DOM helpers ----------

  function el(tag, attrs, children) {
    const node = document.createElement(tag);
    if (attrs) {
      for (const [key, value] of Object.entries(attrs)) {
        if (value === null || value === undefined || value === false) continue;
        if (key === 'class') node.className = value;
        else if (key === 'text') node.textContent = value;
        else if (key === 'icon') node.innerHTML = ICONS[value];   // trusted constant markup only
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

  function icon(name, extra) { return el('span', { class: 'icon ' + (extra || ''), icon: name }); }

  let toastTimer = null;
  function toast(message) {
    const node = $('toast');
    node.classList.add('show');
    node.textContent = message;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { node.classList.remove('show'); node.textContent = ''; }, 2400);
  }

  function announce(text) {
    const live = $('live');
    live.textContent = '';
    requestAnimationFrame(() => { live.textContent = text; });
  }

  function focusHeading() {
    requestAnimationFrame(() => {
      const target = view().querySelector('h1, [tabindex="-1"]');
      if (target) { target.setAttribute('tabindex', '-1'); target.focus({ preventScroll: true }); }
    });
  }

  function hue(text) { let h = 0; for (const ch of text) h = (h * 31 + ch.charCodeAt(0)) % 360; return h; }
  function avatar(user, large) {
    const initials = (user.name || user.handle || '?').trim().slice(0, 2);
    return el('a', {
      class: 'avatar' + (large ? ' lg' : ''), href: '#/u/' + encodeURIComponent(user.handle),
      style: 'background: hsl(' + hue(user.handle) + ' 70% 65%)', 'aria-label': t('a11y.avatar', { name: user.name }), text: initials
    });
  }
  function brandMark(user) { return user.accountType === 'Brand' ? el('span', { class: 'brand-mark', text: t('profile.brand') }) : null; }
  function intentLabel(intent) { return t('intent.' + intent); }

  // ---------- API ----------

  class ApiError extends Error { constructor(status, message) { super(message); this.status = status; } }

  async function api(method, path, body) {
    const headers = { 'X-Requested-With': 'FitCheck', 'Accept-Language': locale };
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
      if (response.status === 401 && state.me) { state.me = null; renderShell(); }
      throw new ApiError(response.status, (data && data.error) || t('error.generic'));
    }
    return data;
  }

  async function loadMe() {
    try { state.me = await api('GET', '/api/auth/me'); } catch (e) { state.me = null; }
    renderShell();
  }

  function requireSignIn() {
    if (state.me) return true;
    toast(t('auth.required_title'));
    location.hash = '#/login';
    return false;
  }

  // ---------- shell ----------

  function renderShell() {
    const auth = $('top-auth');
    if (state.me) {
      auth.textContent = state.me.name;
      auth.href = '#/me';
      auth.classList.remove('accent');
    } else {
      auth.textContent = t('auth.signup');
      auth.href = '#/signup';
      auth.classList.add('accent');
    }
    const badge = $('activity-badge');
    const unread = state.me ? state.me.unreadNotifications : 0;
    badge.hidden = !unread;
    badge.textContent = fmtNumber(unread);
    badge.setAttribute('aria-label', t('a11y.unread', { n: unread }));
    for (const tab of document.querySelectorAll('.tab')) {
      const active = tab.dataset.tab === tabFor(state.route.name);
      if (active) tab.setAttribute('aria-current', 'page'); else tab.removeAttribute('aria-current');
    }
  }

  function tabFor(name) {
    if (['feed', 'post'].includes(name)) return 'feed';
    if (['challenges', 'challenge', 'new-challenge'].includes(name)) return 'challenges';
    if (['check', 'result'].includes(name)) return 'check';
    if (name === 'activity') return 'activity';
    if (['me', 'user', 'saved', 'settings', 'checks', 'login', 'signup'].includes(name)) return 'me';
    return '';
  }

  // ---------- routing ----------

  function parseRoute() {
    const parts = location.hash.replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent);
    const [head, a] = parts;
    switch (head || 'feed') {
      case 'feed': return { name: 'feed', params: { tab: ['fresh', 'top', 'following'].includes(a) ? a : 'fresh' } };
      case 'challenges': return { name: 'challenges', params: { tab: a === 'ended' ? 'ended' : 'open' } };
      case 'challenge': return { name: 'challenge', params: { id: a } };
      case 'check': return { name: 'check', params: {} };
      case 'result': return { name: 'result', params: {} };
      case 'activity': return { name: 'activity', params: {} };
      case 'me': return { name: 'me', params: {} };
      case 'u': return { name: 'user', params: { handle: a } };
      case 'post': return { name: 'post', params: { id: a } };
      case 'login': return { name: 'login', params: {} };
      case 'signup': return { name: 'signup', params: {} };
      case 'new-challenge': return { name: 'new-challenge', params: {} };
      case 'saved': return { name: 'saved', params: {} };
      case 'settings': return { name: 'settings', params: {} };
      case 'checks': return { name: 'checks', params: {} };
      default: return { name: 'feed', params: { tab: 'fresh' } };
    }
  }

  let renderToken = 0;
  async function render(isNavigation) {
    state.route = parseRoute();
    renderShell();
    const token = ++renderToken;
    const root = view();
    root.innerHTML = '';
    const stale = () => token !== renderToken;
    const r = state.route;
    try {
      switch (r.name) {
        case 'feed': await feedView(root, r.params.tab, stale); break;
        case 'challenges': await challengesView(root, r.params.tab, stale); break;
        case 'challenge': await challengeView(root, r.params.id, stale); break;
        case 'check': checkView(root); break;
        case 'result': resultView(root); break;
        case 'activity': await activityView(root, stale); break;
        case 'me': await profileView(root, state.me ? state.me.handle : null, stale); break;
        case 'user': await profileView(root, r.params.handle, stale); break;
        case 'post': await postView(root, r.params.id, stale); break;
        case 'login': authView(root, 'login'); break;
        case 'signup': authView(root, 'signup'); break;
        case 'new-challenge': newChallengeView(root); break;
        case 'saved': await savedView(root, stale); break;
        case 'settings': settingsView(root); break;
        case 'checks': await checksView(root, stale); break;
      }
    } catch (e) {
      if (stale()) return;
      root.appendChild(errorBlock(e));
    }
    if (isNavigation !== false) { window.scrollTo({ top: 0 }); focusHeading(); }
  }

  function errorBlock(e) {
    return el('div', { class: 'notice' }, [
      el('p', { text: e && e.message ? e.message : t('error.generic') }),
      el('button', { type: 'button', class: 'btn btn-secondary', style: 'margin-block-start: 12px;', text: t('common.retry'), onclick: () => render(false) })
    ]);
  }

  function signInPrompt() {
    return el('div', { class: 'notice' }, [
      el('h3', { text: t('auth.required_title') }),
      el('p', { class: 'muted', text: t('auth.required_body') }),
      el('div', { class: 'row', style: 'margin-block-start: 14px;' }, [
        el('a', { class: 'btn', href: '#/signup', text: t('auth.signup'), style: 'display:grid;place-items:center;text-decoration:none;' }),
        el('a', { class: 'btn btn-secondary', href: '#/login', text: t('auth.login'), style: 'display:grid;place-items:center;text-decoration:none;' })
      ])
    ]);
  }

  // ---------- post card ----------

  function postCard(post, opts) {
    opts = opts || {};
    const user = post.user;
    const head = el('div', { class: 'card-head' }, [
      avatar(user),
      el('div', { class: 'who' }, [
        el('a', { class: 'name', href: '#/u/' + encodeURIComponent(user.handle), style: 'text-decoration:none' }, [user.name, brandMark(user)]),
        el('div', { class: 'sub', text: '@' + user.handle + ' · ' + relative(post.createdAt) })
      ]),
      el('span', { class: 'tag', text: intentLabel(post.intent) })
    ]);

    const photo = el('a', { class: 'card-photo', href: '#/post/' + post.id, 'aria-label': t('a11y.look_by', { intent: intentLabel(post.intent), name: user.name }) }, [
      el('img', { src: post.imageUrl, alt: '', loading: 'lazy' }),
      el('span', { class: 'score-badge', 'aria-hidden': 'true' }, [fmtNumber(post.score), el('small', { text: t('result.out_of') })])
    ]);

    const body = el('div', { class: 'card-body' }, [
      post.hidden ? el('p', { class: 'alert danger', text: t('post.hidden') + ' · ' + t('post.hidden_hint') }) : null,
      el('p', { class: 'headline', text: post.headline }),
      post.caption ? el('p', { class: 'caption', text: post.caption }) : null,
      el('div', { class: 'match' }, [
        el('span', { text: t('post.reads_as', { intent: intentLabel(post.intent), pct: fmtPercent(post.intentMatch / 100) }) }),
        el('div', { class: 'bar' }, [el('div', { class: 'bar-fill', style: 'inline-size:' + post.intentMatch + '%' })])
      ]),
      post.challengeId && post.challengeTitle && !opts.inChallenge
        ? el('a', { class: 'challenge-link', href: '#/challenge/' + post.challengeId, text: t('post.in_challenge', { title: post.challengeTitle }) })
        : null,
      post.products && post.products.length ? el('div', { class: 'shop' }, post.products.map((p) => el('a', { href: p.url, target: '_blank', rel: 'noopener' }, [
        icon('bag'), p.label, p.price ? el('b', { text: p.price }) : null
      ]))) : null
    ]);

    const fireBtn = el('button', {
      type: 'button', class: 'action fire', 'aria-pressed': String(post.fired), 'aria-label': t('post.fire'),
      onclick: () => toggleFire(post, fireBtn)
    }, [icon('flame'), el('span', { class: 'count', text: fmtNumber(post.fireCount) })]);
    const saveBtn = el('button', {
      type: 'button', class: 'action save', 'aria-pressed': String(post.saved), 'aria-label': post.saved ? t('post.saved') : t('post.save'),
      onclick: () => toggleSave(post, saveBtn)
    }, [icon('bookmark')]);
    const actions = el('div', { class: 'actions' }, [
      fireBtn,
      el('a', { class: 'action', href: '#/post/' + post.id, 'aria-label': t('post.comments') }, [icon('comment'), el('span', { class: 'count', text: fmtNumber(post.commentCount) })]),
      saveBtn,
      el('button', { type: 'button', class: 'action', 'aria-label': t('post.share'), onclick: () => sharePost(post) }, [icon('share')]),
      opts.votes !== undefined ? el('span', { class: 'tag accent end', text: t('post.votes', { n: fmtNumber(opts.votes) }) }) : null,
      opts.menu ? el('button', { type: 'button', class: 'action end menu-open', 'aria-label': t('common.more'), onclick: () => opts.menu(post) }, [icon('more')]) : null
    ]);

    return el('article', { class: 'card', 'data-post': post.id }, [head, photo, body, actions]);
  }

  async function toggleFire(post, button) {
    if (!requireSignIn()) return;
    const was = post.fired;
    post.fired = !was;
    post.fireCount += was ? -1 : 1;
    button.setAttribute('aria-pressed', String(post.fired));
    button.querySelector('.count').textContent = fmtNumber(post.fireCount);
    if (post.fired && !reducedMotion()) { button.classList.remove('burst'); void button.offsetWidth; button.classList.add('burst'); }
    try {
      const result = await api(was ? 'DELETE' : 'POST', '/api/posts/' + post.id + '/fire');
      post.fireCount = result.fireCount; post.fired = result.fired;
    } catch (e) {
      post.fired = was; post.fireCount += was ? 1 : -1;
      toast(e.message);
    }
    button.setAttribute('aria-pressed', String(post.fired));
    button.querySelector('.count').textContent = fmtNumber(post.fireCount);
  }

  async function toggleSave(post, button) {
    if (!requireSignIn()) return;
    const was = post.saved;
    post.saved = !was;
    button.setAttribute('aria-pressed', String(post.saved));
    try {
      const result = await api(was ? 'DELETE' : 'POST', '/api/posts/' + post.id + '/save');
      post.saved = result.saved;
      toast(post.saved ? t('post.saved') : t('post.save'));
    } catch (e) {
      post.saved = was;
      toast(e.message);
    }
    button.setAttribute('aria-pressed', String(post.saved));
    button.setAttribute('aria-label', post.saved ? t('post.saved') : t('post.save'));
  }

  async function sharePost(post) {
    if (state.sharing) return;
    state.sharing = true;
    try {
      const url = location.origin + '/#/post/' + post.id;
      const text = t('post.share_text', { name: post.user.name, intent: intentLabel(post.intent), score: fmtNumber(post.score) });
      if (navigator.share) {
        try { await navigator.share({ title: t('app.name'), text, url }); return; }
        catch (e) { if (e && (e.name === 'AbortError' || e.name === 'InvalidStateError')) return; }
      }
      try { await navigator.clipboard.writeText(url); toast(t('post.copied')); }
      catch (e) { window.prompt(t('post.share'), url); }
    } finally {
      state.sharing = false;
    }
  }

  async function reportPost(post) {
    if (!requireSignIn()) return;
    if (!window.confirm(t('post.report_confirm'))) return;
    try { await api('POST', '/api/posts/' + post.id + '/report', { reason: 'reported from app' }); toast(t('post.reported')); }
    catch (e) { toast(e.message); }
  }

  async function deletePost(post) {
    if (!window.confirm(t('post.delete_confirm'))) return;
    try { await api('DELETE', '/api/posts/' + post.id); location.hash = '#/me'; }
    catch (e) { toast(e.message); }
  }

  // ---------- feed ----------

  async function feedView(root, tab, stale) {
    const f = state.feed;
    f.tab = tab;
    root.appendChild(el('h1', { text: t('feed.title') }));
    root.appendChild(el('div', { class: 'segments' }, ['fresh', 'top', 'following'].map((name) => el('button', {
      type: 'button', class: 'segment', 'aria-pressed': String(name === tab), text: t('feed.' + name),
      onclick: () => { location.hash = '#/feed/' + name; }
    }))));
    if (tab === 'following' && !state.me) {
      root.appendChild(signInPrompt());
      return;
    }
    const list = el('div', { class: 'stack', id: 'feed-list' });
    const more = el('button', { type: 'button', class: 'btn btn-secondary', text: t('common.load_more'), hidden: true, onclick: () => loadFeed(list, more, false, stale) });
    const chips = el('div', { class: 'chips scroll', role: 'group', 'aria-label': t('a11y.intent_group') });
    const setIntentFilter = (intent) => {
      f.intent = intent;
      for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === intent));
      loadFeed(list, more, true, stale);
    };
    chips.appendChild(el('button', { type: 'button', class: 'chip', 'data-intent': '', 'aria-pressed': String(f.intent === ''), text: t('feed.all_intents'), onclick: () => setIntentFilter('') }));
    for (const intent of INTENTS) {
      chips.appendChild(el('button', { type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(f.intent === intent), text: intentLabel(intent), onclick: () => setIntentFilter(intent) }));
    }
    root.appendChild(chips);
    root.appendChild(list);
    root.appendChild(more);
    await loadFeed(list, more, true, stale);
  }

  // Every reset bumps a sequence number, so a page that comes back for an earlier tab, filter or render is dropped
  // instead of landing in the wrong list (tap the feed tab twice quickly and both loads used to fight).
  async function loadFeed(list, more, reset, stale) {
    const f = state.feed;
    if (reset) { f.items = []; f.next = 0; f.seq += 1; list.innerHTML = ''; }
    if (f.next === null) return;
    const seq = f.seq;
    const offset = f.next;
    f.next = null;               // no second request for the same page while this one is in flight
    more.hidden = true;
    try {
      const query = '?tab=' + f.tab + (f.intent ? '&intent=' + f.intent : '') + '&offset=' + offset + '&limit=' + PAGE;
      const page = await api('GET', '/api/feed' + query);
      if (stale() || f.seq !== seq) return;
      f.items.push(...page.items);
      f.next = page.nextOffset === null || page.nextOffset === undefined ? null : page.nextOffset;
      for (const post of page.items) list.appendChild(postCard(post));
      if (f.items.length === 0) list.appendChild(el('p', { class: 'empty', text: t(f.tab === 'following' ? 'feed.empty_following' : 'feed.empty') }));
      more.hidden = f.next === null;
    } catch (e) {
      if (stale() || f.seq !== seq) return;
      f.next = offset;
      list.appendChild(errorBlock(e));
    }
  }

  // ---------- post detail ----------

  async function postView(root, id, stale) {
    const post = await api('GET', '/api/posts/' + id);
    if (stale()) return;
    root.appendChild(el('a', { class: 'btn-text', href: '#/feed', text: t('common.back') }));
    root.appendChild(postCard(post, { menu: (p) => {
      const menu = root.querySelector('.menu');
      if (menu) { menu.remove(); return; }
      root.querySelector('.card').after(el('div', { class: 'menu' }, [
        p.isMine ? el('button', { type: 'button', class: 'pill', id: 'post-delete', text: t('post.delete'), onclick: () => deletePost(p) }) : null,
        !p.isMine ? el('button', { type: 'button', class: 'pill', id: 'post-report', text: t('post.report'), onclick: () => reportPost(p) }) : null
      ]));
    } }));
    if (post.challengeId && post.votes !== undefined) {
      root.appendChild(el('p', { class: 'muted', text: t('post.votes', { n: fmtNumber(post.votes) }) }));
    }

    root.appendChild(el('h2', { text: t('comments.title') }));
    const list = el('ul', { class: 'comments' });
    root.appendChild(list);
    async function loadComments() {
      const comments = await api('GET', '/api/posts/' + id + '/comments');
      if (stale()) return;
      list.innerHTML = '';
      if (comments.length === 0) list.appendChild(el('li', { class: 'muted', text: t('comments.empty') }));
      for (const c of comments) {
        list.appendChild(el('li', {}, [
          avatar(c.user),
          el('div', { class: 'text' }, [el('b', { text: c.user.name }), c.text, el('div', { class: 'when muted', style: 'font-size:12px', text: relative(c.createdAt) })]),
          c.canDelete
            ? el('button', { type: 'button', class: 'btn-text', text: t('comments.delete'), onclick: async () => { try { await api('DELETE', '/api/comments/' + c.id); post.commentCount = Math.max(0, post.commentCount - 1); await loadComments(); } catch (e) { toast(e.message); } } })
            : state.me ? el('button', { type: 'button', class: 'btn-text', text: t('comments.report'), onclick: async () => { if (!window.confirm(t('comments.report_confirm'))) return; try { await api('POST', '/api/comments/' + c.id + '/report', { reason: 'reported from app' }); toast(t('post.reported')); } catch (e) { toast(e.message); } } }) : null
        ]));
      }
    }
    await loadComments();

    if (state.me) {
      const input = el('input', { type: 'text', id: 'comment-input', maxlength: '200', placeholder: t('comments.placeholder'), 'aria-label': t('comments.title') });
      const send = el('button', { type: 'button', class: 'btn', id: 'comment-send', text: t('comments.send') });
      async function submit() {
        const text = input.value.trim();
        if (!text) return;
        send.disabled = true;
        try { await api('POST', '/api/posts/' + id + '/comments', { text }); input.value = ''; await loadComments(); }
        catch (e) { toast(e.message); }
        finally { send.disabled = false; }
      }
      send.addEventListener('click', submit);
      input.addEventListener('keydown', (event) => { if (event.key === 'Enter') { event.preventDefault(); submit(); } });
      root.appendChild(el('div', { class: 'composer field' }, [input, send]));
    } else {
      root.appendChild(el('a', { class: 'btn-text', href: '#/login', text: t('comments.signin') }));
    }
  }

  // ---------- challenges ----------

  function countdown(challenge) {
    return challenge.isOpen
      ? t('challenges.ends', { when: relative(challenge.endsAt) })
      : t('challenges.ended_at', { when: relative(challenge.endsAt) });
  }

  function challengeCard(c) {
    return el('article', { class: 'card challenge-card' }, [
      el('div', { class: 'challenge-meta' }, [
        el('span', { class: 'tag', text: intentLabel(c.intent) }),
        el('span', { text: t('challenges.by', { name: c.brand.name }) }),
        el('span', { text: t('challenges.entries', { n: fmtNumber(c.entries) }) })
      ]),
      el('a', { href: '#/challenge/' + c.id, style: 'text-decoration:none' }, [el('h3', { class: 'challenge-title', text: c.title })]),
      el('div', { class: 'countdown', text: countdown(c) }),
      el('div', { class: 'prize' }, [icon('trophy'), el('div', {}, [el('b', { text: t('challenges.prize') + ': ' }), c.prize, c.prizeUrl ? el('span', {}, [' · ', el('a', { href: c.prizeUrl, target: '_blank', rel: 'noopener', text: t('challenges.see_prize') })]) : null])]),
      c.top.length ? el('div', { class: 'thumbs' }, c.top.map((p) => el('a', { href: '#/post/' + p.id, 'aria-label': t('a11y.look_by', { intent: intentLabel(p.intent), name: p.user.name }) }, [el('img', { src: p.imageUrl, alt: '', loading: 'lazy' })]))) : null,
      c.winnerPostId ? el('span', { class: 'tag accent', text: t('challenges.winner') }) : null,
      enterButton(c)
    ]);
  }

  function enterButton(c) {
    if (!c.isOpen) return null;
    if (c.viewer.hasEntered) return el('span', { class: 'tag accent', text: t('challenges.entered') });
    if (c.viewer.isBrand) return null;
    return el('button', { type: 'button', class: 'btn btn-secondary', text: t('challenges.enter'), onclick: () => {
      if (!requireSignIn()) return;
      state.check.challenge = { id: c.id, title: c.title, intent: c.intent };
      state.check.intent = c.intent;
      location.hash = '#/check';
    } });
  }

  async function challengesView(root, tab, stale) {
    state.challenges.tab = tab;
    root.appendChild(el('h1', { text: t('challenges.title') }));
    root.appendChild(el('div', { class: 'segments' }, ['open', 'ended'].map((name) => el('button', {
      type: 'button', class: 'segment', 'aria-pressed': String(name === tab), text: t('challenges.' + name),
      onclick: () => { location.hash = '#/challenges' + (name === 'ended' ? '/ended' : ''); }
    }))));
    if (state.me && state.me.accountType === 'Brand') {
      root.appendChild(el('a', { class: 'btn', href: '#/new-challenge', text: t('challenges.new'), style: 'display:grid;place-items:center;text-decoration:none;' }));
    }
    const challenges = await api('GET', '/api/challenges?state=' + tab);
    if (stale()) return;
    if (challenges.length === 0) root.appendChild(el('p', { class: 'empty', text: t(tab === 'ended' ? 'challenges.empty_ended' : 'challenges.empty') }));
    for (const c of challenges) root.appendChild(challengeCard(c));
  }

  async function challengeView(root, id, stale) {
    const detail = await api('GET', '/api/challenges/' + id);
    if (stale()) return;
    const c = detail.challenge;
    root.appendChild(el('a', { class: 'btn-text', href: '#/challenges', text: t('common.back') }));
    root.appendChild(el('div', { class: 'card challenge-card' }, [
      el('div', { class: 'challenge-meta' }, [el('span', { class: 'tag', text: intentLabel(c.intent) }), el('a', { href: '#/u/' + encodeURIComponent(c.brand.handle), text: t('challenges.by', { name: c.brand.name }) })]),
      el('h1', { class: 'challenge-title', text: c.title }),
      el('div', { class: 'countdown', text: countdown(c) }),
      el('div', {}, [el('h2', { text: t('challenges.brief') }), el('p', { class: 'lede', style: 'margin-block-start:4px', text: c.brief })]),
      el('div', { class: 'prize' }, [icon('trophy'), el('div', {}, [el('b', { text: t('challenges.prize') + ': ' }), c.prize, c.prizeUrl ? el('span', {}, [' · ', el('a', { href: c.prizeUrl, target: '_blank', rel: 'noopener', text: t('challenges.see_prize') })]) : null])]),
      el('div', { class: 'challenge-meta' }, [el('span', { text: t('challenges.entries', { n: fmtNumber(c.entries) }) }), el('span', { text: t('challenges.votes', { n: fmtNumber(c.votes) }) })]),
      enterButton(c)
    ]));

    if (!c.isOpen) {
      root.appendChild(el('h2', { text: t('challenges.winner') }));
      if (detail.winner) root.appendChild(postCard(detail.winner, { inChallenge: true, votes: detail.winner.votes }));
      else root.appendChild(el('p', { class: 'muted', text: t('challenges.no_winner') }));
    }

    root.appendChild(el('h2', { text: t('challenges.leaderboard') }));
    if (detail.entriesByVotes.length === 0) { root.appendChild(el('p', { class: 'empty', text: t('challenges.no_entries') })); return; }
    const board = el('div', { class: 'lb' });
    detail.entriesByVotes.forEach((entry, index) => {
      const mine = c.viewer.votedPostId === entry.id;
      const voteBtn = el('button', {
        type: 'button', class: 'vote', 'aria-pressed': String(mine),
        disabled: !c.isOpen || entry.isMine,
        text: mine ? t('post.voted') : t('post.vote'),
        onclick: async () => {
          if (!requireSignIn()) return;
          try {
            const result = mine ? await api('DELETE', '/api/challenges/' + id + '/vote') : await api('POST', '/api/challenges/' + id + '/vote', { postId: entry.id });
            c.viewer.votedPostId = result.votedPostId;
            await render(false);
          } catch (e) { toast(e.message); }
        }
      });
      board.appendChild(el('div', { class: 'lb-row' + (c.winnerPostId === entry.id ? ' winner' : '') }, [
        el('span', { class: 'lb-rank', text: fmtNumber(index + 1) }),
        el('a', { href: '#/post/' + entry.id, 'aria-label': t('a11y.look_by', { intent: intentLabel(entry.intent), name: entry.user.name }) }, [el('img', { src: entry.imageUrl, alt: '', loading: 'lazy' })]),
        el('div', { class: 'lb-who' }, [
          el('a', { class: 'name', href: '#/u/' + encodeURIComponent(entry.user.handle), style: 'text-decoration:none', text: entry.user.name }),
          el('div', { class: 'votes', text: t('post.votes', { n: fmtNumber(entry.votes) }) + ' · ' + fmtNumber(entry.score) + t('result.out_of') })
        ]),
        state.me ? voteBtn : el('a', { class: 'vote', href: '#/login', text: t('challenges.vote_signin'), style: 'text-decoration:none' })
      ]));
    });
    root.appendChild(board);
  }

  function newChallengeView(root) {
    root.appendChild(el('h1', { text: t('newchallenge.title') }));
    if (!state.me || state.me.accountType !== 'Brand') { root.appendChild(signInPrompt()); return; }
    let intent = 'Casual';
    const chips = el('div', { class: 'chips' }, INTENTS.map((i) => el('button', { type: 'button', class: 'chip', 'aria-pressed': String(i === intent), text: intentLabel(i), onclick: (event) => { intent = i; for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip === event.currentTarget)); } })));
    const title = el('input', { type: 'text', maxlength: '80', id: 'nc-title' });
    const brief = el('textarea', { maxlength: '500', id: 'nc-brief', placeholder: t('newchallenge.brief_placeholder') });
    const prize = el('input', { type: 'text', maxlength: '200', id: 'nc-prize', placeholder: t('newchallenge.prize_placeholder') });
    const prizeUrl = el('input', { type: 'url', maxlength: '500', id: 'nc-url', placeholder: 'https://' });
    const ends = el('input', { type: 'datetime-local', id: 'nc-ends' });
    const inThreeDays = new Date(Date.now() + 3 * 86400000);
    inThreeDays.setMinutes(0, 0, 0);
    ends.value = new Date(inThreeDays.getTime() - inThreeDays.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    const submit = el('button', { type: 'button', class: 'btn', id: 'nc-submit', text: t('newchallenge.submit'), onclick: async () => {
      submit.disabled = true; error.hidden = true;
      try {
        const created = await api('POST', '/api/challenges', { title: title.value, brief: brief.value, intent, prize: prize.value, prizeUrl: prizeUrl.value || null, endsAt: new Date(ends.value).toISOString() });
        location.hash = '#/challenge/' + created.id;
      } catch (e) { error.textContent = e.message; error.hidden = false; submit.disabled = false; }
    } });
    root.appendChild(el('div', { class: 'sheet' }, [
      el('div', { class: 'field' }, [el('label', { for: 'nc-title', text: t('newchallenge.name') }), title]),
      el('div', { class: 'field' }, [el('span', { class: 'label', text: t('newchallenge.intent') }), chips]),
      el('div', { class: 'field' }, [el('label', { for: 'nc-brief', text: t('newchallenge.brief') }), brief]),
      el('div', { class: 'field' }, [el('label', { for: 'nc-prize', text: t('newchallenge.prize') }), prize]),
      el('div', { class: 'field' }, [el('label', { for: 'nc-url', text: t('newchallenge.prize_url') }), prizeUrl]),
      el('div', { class: 'field' }, [el('label', { for: 'nc-ends', text: t('newchallenge.ends') }), ends, el('p', { class: 'hint', text: t('newchallenge.hint') })]),
      error,
      submit
    ]));
  }

  // ---------- check flow ----------

  function checkView(root) {
    const ck = state.check;
    root.appendChild(el('h1', { text: t('check.title') }));
    if (!state.me) { root.appendChild(signInPrompt()); return; }
    if (ck.challenge) {
      root.appendChild(el('div', { class: 'alert' }, [
        el('b', { text: t('check.entering', { title: ck.challenge.title }) }), ' ',
        el('span', { class: 'muted', text: t('check.intent_locked', { intent: intentLabel(ck.challenge.intent) }) }), ' ',
        el('button', { type: 'button', class: 'btn-text', text: t('common.cancel'), onclick: () => { ck.challenge = null; render(false); } })
      ]));
    }
    const chips = el('div', { class: 'chips', role: 'group', 'aria-label': t('a11y.intent_group') });
    for (const intent of INTENTS) {
      chips.appendChild(el('button', {
        type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(ck.intent === intent), text: intentLabel(intent),
        disabled: ck.challenge && ck.challenge.intent !== intent,
        onclick: () => { ck.intent = intent; for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === intent)); updateSubmit(); }
      }));
    }
    root.appendChild(el('div', {}, [el('h2', { text: t('check.intent_label') }), el('div', { style: 'margin-block-start:10px' }, [chips])]));

    const occasion = el('input', { type: 'text', id: 'occasion', maxlength: '120', autocomplete: 'off', placeholder: t('check.occasion_placeholder'), value: ck.occasion, oninput: (e) => { ck.occasion = e.target.value; } });
    root.appendChild(el('div', { class: 'field' }, [el('label', { for: 'occasion', text: t('check.occasion_label') }), occasion]));

    const photo = el('button', { id: 'photo', class: 'photo', type: 'button', onclick: () => $('file').click() });
    root.appendChild(photo);
    const error = el('p', { id: 'check-error', class: 'alert danger', role: 'alert', hidden: true });
    root.appendChild(error);
    const submit = el('button', { id: 'submit', class: 'btn', type: 'button', text: t('check.submit'), onclick: submitCheck });
    root.appendChild(submit);
    root.appendChild(el('p', { class: 'hint', text: t('check.private_note') }));
    renderPhoto();
    updateSubmit();
  }

  function renderPhoto() {
    const photo = $('photo');
    if (!photo) return;
    const ck = state.check;
    const label = ck.photoBusy ? t('check.photo_processing') : t(ck.previewUrl ? 'check.photo_replace' : 'check.photo_add');
    photo.classList.toggle('has-image', !!ck.previewUrl);
    photo.setAttribute('aria-label', label);
    photo.setAttribute('aria-busy', String(ck.photoBusy));
    photo.innerHTML = '';
    if (ck.previewUrl) {
      photo.appendChild(el('img', { src: ck.previewUrl, alt: t('a11y.photo_preview') }));
      photo.appendChild(el('span', { class: 'photo-replace', text: label }));
    } else {
      photo.appendChild(el('span', { class: 'photo-empty' }, [el('strong', { text: label }), ck.photoBusy ? null : el('span', { class: 'hint', text: t('check.photo_hint') })]));
    }
  }

  function updateSubmit() {
    const submit = $('submit');
    const ck = state.check;
    if (submit) submit.disabled = !(ck.intent && ck.photo) || ck.busy || ck.photoBusy;
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

  // Downscale before upload: less data over mobile networks, fewer input tokens, and EXIF orientation baked in.
  async function prepareImage(file) {
    try {
      const source = await decodeImage(file);
      const sourceWidth = source.width || source.naturalWidth;
      const sourceHeight = source.height || source.naturalHeight;
      const scale = Math.min(1, MAX_EDGE / Math.max(sourceWidth, sourceHeight));
      const width = Math.max(1, Math.round(sourceWidth * scale));
      const height = Math.max(1, Math.round(sourceHeight * scale));
      const canvas = document.createElement('canvas');
      canvas.width = width; canvas.height = height;
      canvas.getContext('2d').drawImage(source, 0, 0, width, height);
      if (source.close) source.close();
      return await new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('toBlob failed'))), 'image/jpeg', JPEG_QUALITY));
    } catch (e) {
      console.warn('Client-side downscale failed, sending the original file', e);
      return file;
    }
  }

  async function onFileChosen(event) {
    const file = event.target.files && event.target.files[0];
    event.target.value = '';
    if (!file) return;
    const ck = state.check;
    const errorNode = $('check-error');
    if (errorNode) errorNode.hidden = true;
    const token = ++ck.photoToken;
    ck.photo = null; ck.photoBusy = true;
    renderPhoto(); updateSubmit();
    let blob = null;
    try { blob = await prepareImage(file); } catch (e) { blob = null; }
    if (token !== ck.photoToken) return;
    ck.photoBusy = false;
    if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
    if (blob) { ck.photo = blob; ck.previewUrl = URL.createObjectURL(blob); }
    else { ck.photo = null; ck.previewUrl = null; if (errorNode) { errorNode.textContent = t('error.image_read'); errorNode.hidden = false; } }
    renderPhoto(); updateSubmit();
  }

  async function submitCheck() {
    const ck = state.check;
    if (!ck.intent || !ck.photo || ck.busy) return;
    ck.busy = true;
    updateSubmit();
    const root = view();
    root.innerHTML = '';
    root.appendChild(el('div', { class: 'loading', role: 'status' }, [el('div', {}, [el('div', { class: 'loading-mark', 'aria-hidden': 'true' }), el('p', { text: t('loading.line'), tabindex: '-1' })])]));
    announce(t('loading.line'));
    focusHeading();
    const form = new FormData();
    form.append('intent', ck.intent);
    form.append('occasion', ck.occasion.trim());
    form.append('language', locale);
    form.append('image', ck.photo, 'outfit.jpg');
    try {
      state.result = await api('POST', '/api/checks', form);
      state.resultAnimated = false;
      state.resultPostId = null;
      loadMe();
      location.hash = '#/result';
    } catch (e) {
      ck.busy = false;
      if (location.hash !== '#/check') location.hash = '#/check';
      await render(true);
      const errorNode = $('check-error');
      if (errorNode) { errorNode.textContent = e.message; errorNode.hidden = false; }
      return;
    }
    ck.busy = false;
  }

  function checkAnother() {
    const ck = state.check;
    state.result = null; state.resultPostId = null;
    ck.photo = null; ck.challenge = null;
    if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
    ck.previewUrl = null;
    location.hash = '#/check';
  }

  // ---------- result ----------

  function resultView(root) {
    const result = state.result;
    if (!result) { location.hash = '#/check'; return; }
    const feedback = result.feedback || {};
    const intent = intentLabel(result.intent);
    const container = el('div', { id: 'result' });
    root.appendChild(container);

    if (feedback.status !== 'ok') {
      const rejected = feedback.status === 'rejected';
      if (!state.resultAnimated) announce(t(rejected ? 'result.rejected_title' : 'result.not_outfit_title'));
      state.resultAnimated = true;
      container.appendChild(el('div', { class: 'state' }, [
        el('h1', { text: t(rejected ? 'result.rejected_title' : 'result.not_outfit_title') }),
        el('p', { class: 'lede', style: 'margin-block-start: 12px;', text: (!rejected && feedback.message) || t(rejected ? 'result.rejected_body' : 'result.not_outfit_body') }),
        el('button', { type: 'button', class: 'btn', style: 'margin-block-start: 24px;', text: t('result.try_again'), onclick: checkAnother })
      ]));
      return;
    }

    const animate = !state.resultAnimated && !reducedMotion();
    if (!state.resultAnimated) announce(t('a11y.score', { score: feedback.score }) + '. ' + feedback.headline);
    state.resultAnimated = true;
    const scoreNode = el('span', { class: 'score', text: animate ? fmtNumber(0) : fmtNumber(feedback.score) });
    const fill = el('div', { class: 'bar-fill', style: animate ? null : 'transition: none; inline-size: ' + feedback.intentMatch + '%;' });

    container.appendChild(el('div', {}, [
      el('div', { class: 'hero', role: 'img', 'aria-label': t('a11y.score', { score: feedback.score }) }, [scoreNode, el('span', { class: 'score-out', 'aria-hidden': 'true', text: t('result.out_of') })]),
      el('h1', { class: 'result-headline', text: feedback.headline, style: 'margin-block-start: 16px;' }),
      el('p', { class: 'vibe', text: feedback.vibe })
    ]));
    container.appendChild(el('div', {}, [
      el('div', { class: 'match-label' }, [el('span', { text: t('result.intent_match', { intent }) }), el('span', { text: fmtPercent(feedback.intentMatch / 100) })]),
      el('div', { class: 'bar', role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(feedback.intentMatch), 'aria-label': t('result.intent_match', { intent }) }, [fill])
    ]));
    if (feedback.items && feedback.items.length) {
      container.appendChild(el('div', {}, [el('h2', { text: t('result.items') }), el('ul', { class: 'items', style: 'margin-block-start: 4px;' }, feedback.items.map((item) => el('li', {}, [
        el('span', { class: 'dot ' + item.verdict, 'aria-hidden': 'true' }),
        el('div', {}, [el('div', { class: 'item-name' }, [item.name, el('span', { class: 'item-verdict', text: t('verdict.' + item.verdict) })]), el('div', { class: 'item-note', text: item.note })])
      ])))]));
    }
    if (feedback.working && feedback.working.length) {
      container.appendChild(el('div', {}, [el('h2', { text: t('result.working') }), el('ul', { class: 'working', style: 'margin-block-start: 6px;' }, feedback.working.map((line) => el('li', { text: line })))]));
    }
    if (feedback.oneTip) {
      container.appendChild(el('div', {}, [el('h2', { text: t('result.tip') }), el('div', { class: 'tip', style: 'margin-block-start: 8px;' }, [el('p', { text: feedback.oneTip })])]));
    }

    const postArea = el('div');
    container.appendChild(postArea);
    renderPostArea(postArea, result);
    container.appendChild(el('div', { class: 'row' }, [
      el('button', { type: 'button', class: 'btn btn-secondary', text: t('result.share'), onclick: () => shareResult(result) }),
      el('button', { type: 'button', class: 'btn btn-ghost', text: t('result.again'), onclick: checkAnother })
    ]));

    if (animate) {
      requestAnimationFrame(() => { fill.style.inlineSize = feedback.intentMatch + '%'; });
      animateScore(scoreNode, feedback.score);
    }
  }

  function renderPostArea(area, result) {
    area.innerHTML = '';
    const postId = state.resultPostId || result.postId;
    if (postId) {
      area.appendChild(el('div', { class: 'row' }, [el('a', { class: 'btn', id: 'post-link', href: '#/post/' + postId, text: t('result.posted') + ' · ' + t('result.view_post'), style: 'display:grid;place-items:center;text-decoration:none;' })]));
      return;
    }
    area.appendChild(el('button', { type: 'button', class: 'btn', id: 'post-open', text: t('result.post'), onclick: () => openPostSheet(area, result) }));
  }

  async function openPostSheet(area, result) {
    if (!requireSignIn()) return;
    area.innerHTML = '';
    const sheet = el('div', { class: 'sheet' });
    area.appendChild(sheet);
    sheet.appendChild(el('h3', { text: t('result.post_title') }));
    sheet.appendChild(el('p', { class: 'muted', text: t('result.post_intro') }));
    const caption = el('input', { type: 'text', id: 'caption', maxlength: '140', placeholder: t('result.caption_placeholder') });
    sheet.appendChild(el('div', { class: 'field' }, [el('label', { for: 'caption', text: t('result.caption') }), caption]));

    const select = el('select', { id: 'challenge-pick' });
    select.appendChild(el('option', { value: '', text: t('result.no_challenge') }));
    try {
      const open = await api('GET', '/api/challenges?state=open');
      for (const c of open.filter((c) => c.intent === result.intent && !c.viewer.hasEntered && !c.viewer.isBrand)) {
        select.appendChild(el('option', { value: c.id, text: c.title + ' · ' + t('challenges.by', { name: c.brand.name }) }));
      }
    } catch (e) { /* the feed still works without challenges */ }
    if (state.check.challenge && state.check.challenge.intent === result.intent) select.value = state.check.challenge.id;
    sheet.appendChild(el('div', { class: 'field' }, [el('label', { for: 'challenge-pick', text: t('result.challenge') }), select]));

    const productRows = [];
    if (state.me && state.me.accountType === 'Brand') {
      const grid = el('div', { class: 'products-grid' });
      for (let i = 0; i < 3; i++) {
        const row = { label: el('input', { type: 'text', maxlength: '60', placeholder: t('result.product_label'), 'aria-label': t('result.product_label') }), url: el('input', { type: 'url', maxlength: '500', placeholder: t('result.product_url'), 'aria-label': t('result.product_url') }), price: el('input', { type: 'text', maxlength: '20', placeholder: t('result.product_price'), 'aria-label': t('result.product_price') }) };
        productRows.push(row);
        grid.appendChild(row.label); grid.appendChild(row.url); grid.appendChild(row.price);
      }
      sheet.appendChild(el('div', { class: 'field' }, [el('span', { class: 'label', text: t('result.products') }), grid]));
    }

    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    sheet.appendChild(error);
    const confirm = el('button', { type: 'button', class: 'btn', id: 'post-confirm', text: t('result.confirm_post'), onclick: async () => {
      confirm.disabled = true; error.hidden = true;
      const products = productRows.filter((r) => r.label.value.trim() || r.url.value.trim()).map((r) => ({ label: r.label.value.trim(), url: r.url.value.trim(), price: r.price.value.trim() || null }));
      try {
        const post = await api('POST', '/api/posts', { checkId: result.id, caption: caption.value, challengeId: select.value || null, products });
        state.resultPostId = post.id;
        state.check.challenge = null;
        toast(t('result.posted'));
        renderPostArea(area, result);
      } catch (e) { error.textContent = e.message; error.hidden = false; confirm.disabled = false; }
    } });
    sheet.appendChild(el('div', { class: 'row' }, [confirm, el('button', { type: 'button', class: 'btn btn-ghost', text: t('common.cancel'), onclick: () => renderPostArea(area, result) })]));
    caption.focus();
  }

  function animateScore(node, target) {
    const duration = 900;
    const start = performance.now();
    function frame(now) {
      const progress = Math.min(1, (now - start) / duration);
      const eased = 1 - Math.pow(1 - progress, 3);
      node.textContent = fmtNumber(Math.round(eased * target));
      if (progress < 1) requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }

  async function shareResult(result) {
    if (state.sharing) return;
    state.sharing = true;
    try {
      const feedback = result.feedback;
      const text = t('result.share_text', { score: fmtNumber(feedback.score), intent: intentLabel(result.intent), tip: feedback.oneTip });
      if (navigator.share) {
        try { await navigator.share({ text }); return; }
        catch (e) { if (e && (e.name === 'AbortError' || e.name === 'InvalidStateError')) return; }
      }
      try { await navigator.clipboard.writeText(text); toast(t('result.copied')); }
      catch (e) { window.prompt(t('result.share'), text); }
    } finally { state.sharing = false; }
  }

  // ---------- activity ----------

  async function activityView(root, stale) {
    root.appendChild(el('h1', { text: t('activity.title') }));
    if (!state.me) { root.appendChild(signInPrompt()); return; }
    const data = await api('GET', '/api/notifications');
    if (stale()) return;
    if (data.items.length === 0) { root.appendChild(el('p', { class: 'empty', text: t('activity.empty') })); return; }
    const list = el('ul', { class: 'activity' });
    for (const n of data.items) {
      const key = n.type === 'ended' && n.actorHandle === state.me.handle ? 'activity.ended_empty' : 'activity.' + n.type;
      const href = n.postId ? '#/post/' + n.postId : n.challengeId ? '#/challenge/' + n.challengeId : '#/u/' + encodeURIComponent(n.actorHandle);
      const actor = n.actorName || n.actorHandle;
      list.appendChild(el('li', { class: n.read ? '' : 'unread' }, [
        avatar({ handle: n.actorHandle, name: actor }),
        el('a', { href }, [el('div', { text: t(key, { actor }) }), el('div', { class: 'when', text: relative(n.createdAt) })])
      ]));
    }
    root.appendChild(list);
    if (data.unread > 0) {
      api('POST', '/api/notifications/read').then(() => { if (state.me) { state.me.unreadNotifications = 0; renderShell(); } }).catch(() => {});
    }
  }

  // ---------- profile ----------

  async function profileView(root, handle, stale) {
    if (!handle) { root.appendChild(el('h1', { text: t('nav.profile') })); root.appendChild(signInPrompt()); return; }
    let profile;
    try { profile = await api('GET', '/api/users/' + encodeURIComponent(handle)); }
    catch (e) { if (e.status === 404) { root.appendChild(el('p', { class: 'empty', text: t('profile.not_found') })); return; } throw e; }
    if (stale()) return;
    const isMe = profile.viewer.isMe;
    const head = el('div', { class: 'profile-head' }, [
      avatar(profile, true),
      el('div', { class: 'who' }, [
        el('h1', { class: 'name' }, [profile.name, brandMark(profile)]),
        el('div', { class: 'sub', text: '@' + profile.handle }),
        profile.bio ? el('p', { class: 'caption', style: 'margin-block-start:6px', text: profile.bio }) : null,
        profile.website ? el('a', { href: profile.website, target: '_blank', rel: 'noopener', class: 'challenge-link', text: profile.website.replace(/^https:\/\//, '') }) : null
      ])
    ]);
    root.appendChild(head);
    root.appendChild(el('div', { class: 'stats' }, [
      stat(profile.posts, 'profile.posts'), stat(profile.followers, 'profile.followers'), stat(profile.following, 'profile.following'),
      stat(profile.fireReceived, 'profile.fire', true), stat(profile.bestScore === null || profile.bestScore === undefined ? '–' : profile.bestScore, 'profile.best'), stat(profile.streak, 'profile.streak', profile.streak > 1)
    ]));

    if (isMe) {
      const links = el('div', { class: 'links' }, [
        el('a', { href: '#/settings', text: t('profile.edit') }),
        el('a', { href: '#/saved', text: t('profile.saved') }),
        el('a', { href: '#/checks', text: t('profile.checks') }),
        state.me.accountType === 'Brand' ? el('a', { href: '#/new-challenge', text: t('challenges.new') }) : null,
        el('button', { type: 'button', id: 'logout', text: t('auth.logout'), onclick: async () => { try { await api('POST', '/api/auth/logout'); } catch (e) { /* cookie may already be gone */ } state.me = null; toast(t('common.signed_out')); location.hash = '#/feed'; } })
      ]);
      root.appendChild(links);
    } else if (state.me) {
      const follow = el('button', { type: 'button', id: 'follow', class: 'btn' + (profile.viewer.following ? ' btn-secondary' : ''), 'aria-pressed': String(profile.viewer.following), text: t(profile.viewer.following ? 'profile.unfollow' : 'profile.follow'), onclick: async () => {
        follow.disabled = true;
        try {
          const result = await api(profile.viewer.following ? 'DELETE' : 'POST', '/api/users/' + encodeURIComponent(profile.handle) + '/follow');
          profile.viewer.following = result.following;
          profile.followers = result.followers;
          await render(false);
        } catch (e) { toast(e.message); follow.disabled = false; }
      } });
      root.appendChild(follow);
    } else {
      root.appendChild(el('a', { class: 'btn btn-secondary', href: '#/login', text: t('profile.follow'), style: 'display:grid;place-items:center;text-decoration:none;' }));
    }

    const posts = await api('GET', '/api/users/' + encodeURIComponent(handle) + '/posts?limit=30');
    if (stale()) return;
    root.appendChild(el('h2', { text: t('profile.posts') }));
    if (posts.items.length === 0) { root.appendChild(el('p', { class: 'empty', text: t('profile.no_posts') })); return; }
    root.appendChild(postGrid(posts.items));
  }

  function stat(value, key, hot) {
    return el('div', { class: 'stat' + (hot ? ' hot' : '') }, [el('b', { text: typeof value === 'number' ? fmtNumber(value) : value }), el('span', { text: t(key) })]);
  }

  function postGrid(posts) {
    return el('div', { class: 'grid' }, posts.map((p) => el('a', { href: '#/post/' + p.id, 'aria-label': t('a11y.look_by', { intent: intentLabel(p.intent), name: p.user.name }) }, [
      el('img', { src: p.imageUrl, alt: '', loading: 'lazy' }),
      el('span', { class: 'score-badge', 'aria-hidden': 'true' }, [fmtNumber(p.score), el('small', { text: t('result.out_of') })]),
      p.hidden ? el('span', { class: 'tag private', text: t('post.hidden') }) : null
    ])));
  }

  async function savedView(root, stale) {
    root.appendChild(el('h1', { text: t('saved.title') }));
    if (!state.me) { root.appendChild(signInPrompt()); return; }
    const saved = await api('GET', '/api/users/me/saved?limit=30');
    if (stale()) return;
    if (saved.items.length === 0) { root.appendChild(el('p', { class: 'empty', text: t('saved.empty') })); return; }
    root.appendChild(postGrid(saved.items));
  }

  async function checksView(root, stale) {
    root.appendChild(el('h1', { text: t('checks.title') }));
    if (!state.me) { root.appendChild(signInPrompt()); return; }
    const checks = await api('GET', '/api/users/me/checks');
    if (stale()) return;
    const ok = checks.filter((c) => c.status === 'ok');
    if (ok.length === 0) { root.appendChild(el('p', { class: 'empty', text: t('checks.empty') })); return; }
    root.appendChild(el('ul', { class: 'activity' }, ok.map((c) => el('li', {}, [
      el('div', { style: 'flex:1' }, [
        el('div', {}, [el('b', { text: fmtNumber(c.score) + t('result.out_of') + ' · ' + intentLabel(c.intent) }), ' ', el('span', { class: 'tag', text: c.postId ? t('result.posted') : t('checks.private') })]),
        el('div', { class: 'when', text: fmtDate(c.createdAt) + (c.feedback && c.feedback.headline ? ' · ' + c.feedback.headline : '') })
      ]),
      c.postId
        ? el('a', { class: 'pill', href: '#/post/' + c.postId, text: t('result.view_post') })
        : el('button', { type: 'button', class: 'pill accent', text: t('result.post'), onclick: () => { state.result = c; state.resultAnimated = true; state.resultPostId = null; location.hash = '#/result'; } })
    ]))));
  }

  function settingsView(root) {
    root.appendChild(el('h1', { text: t('settings.title') }));
    if (!state.me) { root.appendChild(signInPrompt()); return; }
    const me = state.me;
    const name = el('input', { type: 'text', id: 's-name', maxlength: '40', value: me.name === me.handle ? '' : me.name });
    const bio = el('textarea', { id: 's-bio', maxlength: '160' }); bio.value = me.bio || '';
    const website = el('input', { type: 'url', id: 's-web', maxlength: '200', value: me.website || '', placeholder: 'https://' });
    const language = el('select', { id: 's-lang' });
    for (const code of AVAILABLE_LOCALES) language.appendChild(el('option', { value: code, text: (messages[code] && messages[code]['meta.name']) || code }));
    language.value = locale;
    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    const save = el('button', { type: 'button', class: 'btn', id: 's-save', text: t('settings.save'), onclick: async () => {
      save.disabled = true; error.hidden = true;
      try {
        state.me = await api('PATCH', '/api/users/me', { displayName: name.value, bio: bio.value, website: website.value, language: language.value });
        if (language.value !== locale) await switchLocale(language.value);
        renderShell();
        toast(t('settings.saved'));
      } catch (e) { error.textContent = e.message; error.hidden = false; }
      finally { save.disabled = false; }
    } });
    root.appendChild(el('div', { class: 'sheet' }, [
      el('div', { class: 'field' }, [el('label', { for: 's-name', text: t('settings.display_name') }), name]),
      el('div', { class: 'field' }, [el('label', { for: 's-bio', text: t('settings.bio') }), bio]),
      el('div', { class: 'field' }, [el('label', { for: 's-web', text: t('settings.website') }), website]),
      el('div', { class: 'field' }, [el('label', { for: 's-lang', text: t('settings.language') }), language]),
      error,
      save
    ]));
    root.appendChild(el('button', { type: 'button', class: 'btn btn-danger', id: 'delete-account', text: t('settings.delete'), onclick: async () => {
      if (!window.confirm(t('settings.delete_confirm'))) return;
      try { await api('DELETE', '/api/users/me'); } catch (e) { if (e.status !== 401) { toast(e.message); return; } }
      state.me = null;
      location.hash = '#/feed';
    } }));
  }

  // ---------- auth ----------

  function authView(root, mode) {
    const signup = mode === 'signup';
    root.appendChild(el('h1', { text: t(signup ? 'auth.signup_title' : 'auth.login_title') }));
    root.appendChild(el('p', { class: 'lede', text: t(signup ? 'auth.signup_intro' : 'auth.login_intro') }));
    const handle = el('input', { type: 'text', id: 'a-handle', maxlength: '40', autocomplete: 'username', autocapitalize: 'none' });
    const password = el('input', { type: 'password', id: 'a-password', maxlength: '200', autocomplete: signup ? 'new-password' : 'current-password' });
    const displayName = el('input', { type: 'text', id: 'a-name', maxlength: '40', autocomplete: 'nickname' });
    const brand = el('input', { type: 'checkbox', id: 'a-brand' });
    const age = el('input', { type: 'checkbox', id: 'a-age' });
    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    const submit = el('button', { type: 'submit', class: 'btn', id: 'a-submit', text: t(signup ? 'auth.submit_signup' : 'auth.submit_login') });
    const form = el('form', { novalidate: true, onsubmit: async (event) => {
      event.preventDefault();
      submit.disabled = true; error.hidden = true;
      try {
        state.me = signup
          ? await api('POST', '/api/auth/signup', { handle: handle.value, password: password.value, confirmed16Plus: age.checked, language: locale, accountType: brand.checked ? 'Brand' : 'Person', displayName: displayName.value })
          : await api('POST', '/api/auth/login', { handle: handle.value, password: password.value });
        renderShell();
        location.hash = state.check.challenge ? '#/check' : '#/feed';
      } catch (e) { error.textContent = e.message; error.hidden = false; submit.disabled = false; }
    } }, [
      el('div', { class: 'field' }, [el('label', { for: 'a-handle', text: t('auth.handle') }), handle, signup ? el('p', { class: 'hint', text: t('auth.handle_hint') }) : null]),
      el('div', { class: 'field' }, [el('label', { for: 'a-password', text: t('auth.password') }), password, signup ? el('p', { class: 'hint', text: t('auth.password_hint') }) : null]),
      signup ? el('div', { class: 'field' }, [el('label', { for: 'a-name', text: t('auth.display_name') }), displayName]) : null,
      signup ? el('div', { class: 'field' }, [el('label', { class: 'checkline', for: 'a-brand' }, [brand, el('span', {}, [el('span', { text: t('auth.brand_toggle') }), el('span', { class: 'hint', text: t('auth.brand_hint') })])])]) : null,
      signup ? el('div', { class: 'field' }, [el('label', { class: 'checkline', for: 'a-age' }, [age, el('span', {}, [el('span', { text: t('auth.age') }), el('span', { class: 'hint', text: t('auth.privacy') })])])]) : null,
      error,
      el('div', { style: 'margin-block-start: 18px' }, [submit]),
      el('p', { class: 'hint', style: 'margin-block-start: 14px' }, [
        t(signup ? 'auth.have_account' : 'auth.no_account') + ' ',
        el('a', { href: signup ? '#/login' : '#/signup', text: t(signup ? 'auth.login' : 'auth.signup') })
      ])
    ]);
    root.appendChild(form);
    setTimeout(() => handle.focus(), 0);
  }

  // ---------- boot ----------

  async function boot() {
    const prefs = loadPrefs();
    const initial = matchLocale(prefs.language) || detectLocale();
    await Promise.all(AVAILABLE_LOCALES.map((code) => loadLocale(code).catch((e) => console.warn(e))));
    if (!messages[DEFAULT_LOCALE]) messages[DEFAULT_LOCALE] = {};
    renderLanguageSelect();
    locale = messages[initial] ? initial : DEFAULT_LOCALE;
    document.documentElement.lang = locale;
    document.documentElement.dir = t('meta.dir') === 'rtl' ? 'rtl' : 'ltr';
    for (const node of document.querySelectorAll('[data-i18n]')) node.textContent = t(node.dataset.i18n);
    $('lang').value = locale;
    $('lang').addEventListener('change', (event) => switchLocale(event.target.value).catch((e) => { console.warn(e); $('lang').value = locale; toast(t('error.network')); }));
    $('file').addEventListener('change', onFileChosen);
    window.addEventListener('hashchange', () => render(true));
    // Tapping the tab you are already on refreshes it, the way feeds on the phone do.
    for (const tab of document.querySelectorAll('.tab')) {
      tab.addEventListener('click', (event) => {
        if (tab.getAttribute('href') === location.hash) { event.preventDefault(); render(true); }
      });
    }
    await loadMe();
    if (state.me && matchLocale(state.me.language) && state.me.language !== locale && !prefs.language) await switchLocale(state.me.language);
    await render(true);
  }

  boot().catch((e) => {
    console.error(e);
    document.body.appendChild(el('p', { class: 'alert', style: 'margin: 20px;', text: 'FitCheck could not start. Reload the page.' }));
  });
})();
