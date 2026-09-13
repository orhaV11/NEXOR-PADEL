// The weekly flames board (#/board): five top-tens for the week that is running (Looks, People, Rising, By intent,
// Stylist's picks) behind the feed's coins, the week and when it closes (recomputed every minute from closesIn), the
// sponsor when the week has one, the reader's own place, and each place as the medal, the fires that counted and the
// look card or the person row. The hall of flame (#/board/hall) is the closed weeks, each with its top three looks and
// its top person. Everything comes from GET /api/board (?week=yyyy-MM-dd for the archive, or a full UTC instant inside
// the week, which is what a "You finished #N" line passes) and GET /api/board/hall; the server answers 501 until the
// board is built, which reads here as the empty state. Three pieces are drawn by other views: the "This week" strip on
// Explore (boardStrip), the reset-day card on For you (boardResetCard) and the badge on a profile (profileBadge). The
// tab and the intent live in module state so a trip to a look and back keeps them; #/board?tab=people&intent=Date&week=2026-09-06
// sets them on the way in. The rules live in app.css (§ Round 10 board).
import {
  register, state, t, api, el, icon, postCard, userRow, emptyState, errorBlock, skeletonCards, setTopBar, onLeave,
  INTENTS, intentLabel, intlLocale, fmtNumber, fmtCompact, fmtDate, isMe, hasMessage
} from '../core.js';

export const BOARDS = ['looks', 'people', 'rising', 'intent', 'picks'];
/** How long a fetched board is trusted before it is fetched again: the server caches it for about as long. */
const CACHE_TTL = 60 * 1000;
const DAY = 86400 * 1000;
const RESET_KEY = 'orevosh.board.reset';

let cached = null;   // { data, at }: the current week only; an archive week (?week=) is never cached
const view = { tab: 'looks', intent: '' };

/** The count a plural key wants: the number 1 (so the _one form fires) or the compact figure. */
const countArg = (n) => (n === 1 ? 1 : fmtCompact(n));
const fires = (n) => t('board.fires', { n: countArg(n || 0) });

/** The current week's board, from the cache while it is fresh. Throws like api() (501 while the server is not built). */
async function loadBoard() {
  if (cached && Date.now() - cached.at < CACHE_TTL) return cached.data;
  const data = await api('GET', '/api/board');
  cached = { data, at: Date.now() };
  return data;
}

/** The query part of the hash (#/board?tab=people): the route parser cuts it, so the view reads it here. */
function query() {
  const i = location.hash.indexOf('?');
  return new URLSearchParams(i < 0 ? '' : location.hash.slice(i + 1));
}

/**
 * The week's label. WeekStart is the UTC instant of midnight in the board's zone, so the plain date could read as the
 * day before in a zone west of it; noon of that day names the same day in every zone within twelve hours of the board's.
 */
const weekDate = (iso) => fmtDate(new Date(Date.parse(iso) + DAY / 2).toISOString());
/** A date inside the week that starts (or ends) at the instant given: what ?week= wants for the week before or after. */
const midweek = (iso, days) => new Date(Date.parse(iso) + days * DAY).toISOString().slice(0, 10);

function unit(n, name) { return new Intl.NumberFormat(intlLocale(), { style: 'unit', unit: name, unitDisplay: 'long' }).format(n); }
/** "2 days 5 hours", "5 hours 12 minutes", "3 minutes": two units at most, in the locale's own words and order. */
function duration(ms) {
  const minutes = Math.max(1, Math.round(ms / 60000));
  const d = Math.floor(minutes / 1440);
  const h = Math.floor((minutes % 1440) / 60);
  const m = minutes % 60;
  const parts = d > 0 ? [unit(d, 'day'), h > 0 ? unit(h, 'hour') : null] : h > 0 ? [unit(h, 'hour'), m > 0 ? unit(m, 'minute') : null] : [unit(m, 'minute')];
  const list = parts.filter(Boolean);
  try { return new Intl.ListFormat(intlLocale(), { type: 'unit', style: 'narrow' }).format(list); } catch (e) { return list.join(' '); }
}

/** A board's name for people: "Looks", "By intent · Date". Unknown names show as they are rather than as a missing key. */
export function boardLabel(board) {
  if (!board) return '';
  if (board.startsWith('intent:')) {
    const intent = board.slice('intent:'.length);
    return t('badge.board_intent', { intent: INTENTS.includes(intent) ? intentLabel(intent) : intent });
  }
  return hasMessage('badge.board_' + board) ? t('badge.board_' + board) : board;
}

/** The medal: a numeral on a disc; the first three burn. */
function medal(rank, size) {
  return el('span', { class: 'rank-medal' + (rank <= 3 ? ' top' : '') + (size ? ' ' + size : ''), role: 'img', 'aria-label': t('board.rank_label', { rank: fmtNumber(rank) }), text: fmtNumber(rank) });
}

/** A key's text around one node: t('board.sponsor', { name: MARK }) with the name as a link where the mark was. */
const MARK = '\u0000';
function withNode(text, node) {
  const i = text.indexOf(MARK);
  if (i < 0) return [text, ' ', node];
  return [text.slice(0, i) || null, node, text.slice(i + MARK.length) || null];
}
function hostOf(url) { try { return new URL(url).host.replace(/^www\./i, ''); } catch (e) { return url; } }
/** A link the page may open in a new tab: http(s) only. The server validates the sponsor's setting; the page checks again before it becomes an href. */
const webLink = (url) => (typeof url === 'string' && /^https?:\/\//i.test(url) ? url : null);

// ---------- pieces of the page ----------

/** "Presented by {name}" (the name a link to the sponsor's profile, or to its site), the prize, and the site when both are given. */
function sponsorBlock(s) {
  const site = webLink(s.url);
  const name = s.handle
    ? el('a', { href: '#/u/' + encodeURIComponent(s.handle), text: s.name })
    : site ? el('a', { href: site, target: '_blank', rel: 'noopener', text: s.name }) : el('b', { text: s.name });
  return el('div', { class: 'board-sponsor', id: 'board-sponsor' }, [
    el('p', { class: 'board-sponsor-by' }, withNode(t('board.sponsor', { name: MARK }), name)),
    s.prizeText ? el('p', { class: 'board-prize', text: t('board.prize', { prize: s.prizeText }) }) : null,
    s.handle && site ? el('a', { class: 'board-sponsor-site', href: site, target: '_blank', rel: 'noopener' }, [icon('link'), el('bdi', { dir: 'ltr', text: hostOf(site) })]) : null
  ]);
}

const hallLink = () => el('a', { class: 'board-hall-link', id: 'board-hall-link', href: '#/board/hall' }, [icon('trophy'), el('span', { text: t('board.hall_link') })]);

/**
 * The head: the week and the hall link, "Closes in …" (recomputed every minute until the view goes away; "This week
 * is closed" once it is), the sponsor, the rules, and the way to the week before (and after, from the archive). The
 * countdown is anchored to fetchedAt, the moment closesIn was true, not to now: a board drawn from the minute's cache
 * would otherwise close up to a minute late.
 */
function head(data, fetchedAt) {
  const closes = el('p', { class: 'board-closes', id: 'board-closes' });
  const closeAt = (fetchedAt || Date.now()) + (data.closesIn || 0) * 1000;
  const paint = () => {
    const left = closeAt - Date.now();
    closes.textContent = data.closed || left <= 0 ? t('board.closed') : t('board.closes_in', { time: duration(left) });
  };
  paint();
  const timer = setInterval(paint, 60 * 1000);
  onLeave(() => clearInterval(timer));

  const nextMid = Date.parse(data.weekEnd) + 3.5 * DAY;
  return el('div', { class: 'board-head' }, [
    el('div', { class: 'between' }, [el('p', { class: 'board-week', text: t('board.week_of', { date: weekDate(data.weekStart) }) }), hallLink()]),
    closes,
    data.sponsor ? sponsorBlock(data.sponsor) : null,
    el('p', { class: 'hint', text: t('board.rules') }),
    el('div', { class: 'board-nav' }, [
      el('a', { class: 'pill', id: 'board-previous', href: '#/board?week=' + midweek(data.weekStart, -3.5), text: t('board.previous') }),
      data.closed ? el('a', { class: 'pill', id: 'board-next', href: nextMid > Date.now() ? '#/board' : '#/board?week=' + new Date(nextMid).toISOString().slice(0, 10), text: t('board.next') }) : null
    ])
  ]);
}

/** The five coins, wrapping onto a second row. Tapping one redraws the panel in place. */
function segments(current, onPick) {
  const group = el('div', { class: 'segments', role: 'group', 'aria-label': t('board.title') }, BOARDS.map((name) => el('button', {
    type: 'button', class: 'segment', 'data-tab': name, 'aria-pressed': String(name === current), text: t('board.tab_' + name),
    onclick: () => onPick(name)
  })));
  return el('div', { class: 'board-tabs', id: 'board-tabs' }, [group]);
}

/** The eight intents as chips on the By intent tab; the pressed one names the list under it. */
function intentChips(current, onPick) {
  return el('div', { class: 'chips scroll board-intents', id: 'board-intents', role: 'group', 'aria-label': t('board.tab_intent') }, INTENTS.map((intent) => el('button', {
    type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(intent === current), text: intentLabel(intent), onclick: () => onPick(intent)
  })));
}

function rowsFor(data, tab, intent) {
  if (tab === 'intent') return (data.intents && data.intents[intent]) || [];
  return data[tab] || [];
}

/** The first intent that has anybody on it, else the first intent; the remembered one wins when it is set. */
function pickIntent(data) {
  if (view.intent) return view.intent;
  const intents = data.intents || {};
  return INTENTS.find((name) => (intents[name] || []).length) || INTENTS[0];
}

/**
 * The reader's place on the board shown: the server's word (BoardMeDto, which reaches past the top ten) for the four
 * named boards; on By intent the rows of the chosen intent decide, since the DTO's one intent place does not say which
 * intent it is for. Null when signed out or not placed.
 */
function myRank(data, tab, rows) {
  if (!state.me) return null;
  const fromRows = () => { const row = rows.find((r) => (r.post && r.post.isMine) || (r.user && isMe(r.user.handle))); return row ? row.rank : null; };
  if (tab === 'intent') return fromRows();
  const mine = data.me && data.me[tab];
  return mine != null ? mine : fromRows();
}

/** One place on a looks board: the medal and the fires that counted (and the stylist's score on the picks board), then the card. */
function lookRow(row) {
  const li = el('li', { class: 'board-row', 'data-rank': String(row.rank) });
  const redraw = () => li.replaceWith(lookRow(row));
  li.appendChild(el('div', { class: 'board-row-head' }, [
    medal(row.rank),
    el('span', { class: 'board-fires' }, [icon('flame'), el('span', { text: fires(row.fires) })]),
    row.score != null ? el('span', { class: 'tag', text: t('board.score', { n: fmtNumber(row.score) + t('result.out_of') }) }) : null
  ]));
  li.appendChild(postCard(row.post, { compact: true, onDelete: () => li.remove(), onChange: redraw }));
  return li;
}

/** One place on the people board: the medal, the person, how many looks they posted and the fires that counted. */
function personRow(row) {
  const stat = [row.looks != null ? t('board.looks_count', { n: countArg(row.looks) }) : null, fires(row.fires)].filter(Boolean).join(' · ');
  return el('li', { class: 'board-row board-person', 'data-rank': String(row.rank) }, [medal(row.rank), userRow(row.user), el('span', { class: 'board-stat', text: stat })]);
}

/** The panel under the coins: the chips on By intent, the reader's own line, the rows or the empty state. */
function panel(data, tab, intent, onIntent) {
  const rows = rowsFor(data, tab, intent);
  const rank = myRank(data, tab, rows);
  return el('div', { class: 'board-panel', id: 'board-panel', role: 'group', 'aria-label': t('board.tab_' + tab) }, [
    tab === 'intent' ? intentChips(intent, onIntent) : null,
    rank ? el('p', { class: 'board-me', id: 'board-me' }, [icon('flame'), el('span', { text: t(data.closed ? 'board.finished' : 'board.me', { rank: fmtNumber(rank) }) })]) : null,
    rows.length
      ? el('ul', { class: 'board-list' }, rows.map((row) => (tab === 'people' || !row.post ? personRow(row) : lookRow(row))))
      : emptyState(t(tab === 'people' ? 'board.empty_people' : 'board.empty'))
  ]);
}

// ---------- the board ----------

register('board', async (root, params, ctx) => {
  setTopBar({ back: '#/explore', title: t('board.title') });
  root.classList.add('flush');
  const q = query();
  if (BOARDS.includes(q.get('tab'))) view.tab = q.get('tab');
  if (INTENTS.includes(q.get('intent'))) view.intent = q.get('intent');
  // A date, or a UTC instant inside the week (what the "You finished #N" line and the push carry).
  const week = /^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?Z)?$/.test(q.get('week') || '') ? q.get('week') : '';

  let data = null;
  let panelNode = null;
  const redraw = () => {
    if (!data || !panelNode) return;
    const next = panel(data, view.tab, view.tab === 'intent' ? pickIntent(data) : '', (intent) => { view.intent = intent; redraw(); });
    panelNode.replaceWith(next);
    panelNode = next;
  };
  const tabs = segments(view.tab, (name) => {
    if (name === view.tab) return;
    view.tab = name;
    for (const button of tabs.querySelectorAll('.segment')) button.setAttribute('aria-pressed', String(button.dataset.tab === name));
    redraw();
  });
  root.appendChild(el('div', { class: 'sticky-tabs' }, [el('h1', { class: 'sr-only', text: t('board.title') }), tabs]));
  const body = el('div', { class: 'board-body' }, [skeletonCards(2)]);
  root.appendChild(body);

  try { data = week ? await api('GET', '/api/board?week=' + encodeURIComponent(week)) : await loadBoard(); }
  catch (e) {
    if (ctx.stale()) return;
    // Not built yet (501) reads as an empty week; the hall stays one tap away either way.
    body.replaceChildren(el('div', { class: 'board-pad' }, [e.status === 501 ? emptyState(t('board.empty')) : errorBlock(e), hallLink()]));
    return;
  }
  if (ctx.stale()) return;
  // When closesIn was true: just now for an archive week, the cache's own fetch time for the current one.
  const fetchedAt = week || !cached ? Date.now() : cached.at;
  panelNode = panel(data, view.tab, view.tab === 'intent' ? pickIntent(data) : '', (intent) => { view.intent = intent; redraw(); });
  body.replaceChildren(head(data, fetchedAt), panelNode);
});

// ---------- the hall of flame ----------

/** A closed week's look: the print with its medal, the name and the fires; a tile with a note when the look is gone. */
function hallTile(w) {
  const label = t('hall.winner', { rank: fmtNumber(w.rank), board: boardLabel(w.board) }) + ' · ' + w.user.name;
  const inner = [
    el('figure', {}, [
      w.imageUrl ? el('img', { src: w.imageUrl, alt: '', loading: 'lazy', decoding: 'async' }) : el('span', { class: 'hall-gone', text: t('hall.look_gone') }),
      medal(w.rank, 'sm')
    ]),
    el('span', { class: 'hall-by' }, [el('b', { text: w.user.name }), fires(w.fires)])
  ];
  return w.postId && w.imageUrl
    ? el('a', { class: 'hall-tile', href: '#/post/' + w.postId, 'aria-label': label }, inner)
    : el('div', { class: 'hall-tile', role: 'group', 'aria-label': label }, inner);
}

/** One closed week: its top three looks and its top person. */
function hallWeek(week) {
  const winners = week.winners || [];
  const looks = winners.filter((w) => w.board === 'looks' && w.rank <= 3).sort((a, b) => a.rank - b.rank);
  const person = winners.find((w) => w.board === 'people' && w.rank === 1);
  return el('section', { class: 'hall-week' }, [
    el('h2', { text: t('hall.week', { date: weekDate(week.weekStart) }) }),
    looks.length ? el('div', { class: 'hall-top' }, looks.map(hallTile)) : null,
    person ? el('div', { class: 'hall-person', 'aria-label': t('hall.winner', { rank: fmtNumber(1), board: boardLabel('people') }) }, [medal(1), userRow(person.user), el('span', { class: 'board-stat', text: fires(person.fires) })]) : null,
    !looks.length && !person ? el('p', { class: 'hint', text: t('board.empty') }) : null
  ]);
}

register('board-hall', async (root, params, ctx) => {
  setTopBar({ back: '#/board', title: t('hall.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('hall.title') }));
  const holder = el('div', {}, [skeletonCards(1)]);
  root.appendChild(holder);

  let data;
  try { data = await api('GET', '/api/board/hall'); }
  catch (e) { if (ctx.stale()) return; holder.replaceWith(e.status === 501 ? emptyState(t('hall.empty')) : errorBlock(e)); return; }
  if (ctx.stale()) return;
  const weeks = (data && data.weeks) || [];
  if (!weeks.length) { holder.replaceWith(emptyState(t('hall.empty'))); return; }
  holder.replaceWith(el('div', { class: 'stack hall', id: 'hall' }, weeks.map(hallWeek)));
});

// ---------- pieces other views draw ----------

function stripTile(row) {
  const p = row.post;
  return el('a', { href: '#/post/' + p.id, 'aria-label': t('board.rank_label', { rank: fmtNumber(row.rank) }) + ' · ' + t(p.videoUrl ? 'a11y.clip_by' : 'a11y.look_by', { intent: intentLabel(p.intent), name: p.user.name }) }, [
    el('figure', {}, [el('img', { src: p.imageUrl, alt: '', loading: 'lazy', decoding: 'async' }), medal(row.rank, 'sm')]),
    el('span', { class: 'board-fires' }, [icon('flame'), el('span', { text: fires(row.fires) })])
  ]);
}

/**
 * The "This week" strip for the top of Explore: the top three of the looks board and the way to the board. Returns a
 * holder that stays hidden until the board has looks (drawn at once from the cache, fetched when that is stale); an
 * empty or unbuilt board leaves it hidden. Never throws.
 */
export function boardStrip(ctx) {
  const holder = el('div', { hidden: true });
  const paint = (data) => {
    const rows = (data.looks || []).filter((row) => row.post).slice(0, 3);
    holder.hidden = !rows.length || !!data.closed;
    if (holder.hidden) { holder.replaceChildren(); return; }
    holder.replaceChildren(el('section', { class: 'x-section board-strip', id: 'board-strip', 'aria-labelledby': 'board-strip-title' }, [
      el('div', { class: 'section-head' }, [el('h2', { id: 'board-strip-title', text: t('board.strip_title') }), el('a', { href: '#/board', text: t('board.strip_all') })]),
      el('div', { class: 'board-strip-row' }, rows.map(stripTile))
    ]));
  };
  if (cached) paint(cached.data);
  if (!cached || Date.now() - cached.at >= CACHE_TTL) loadBoard().then((data) => { if (!ctx || !ctx.stale || !ctx.stale()) paint(data); }).catch(() => { /* nothing on the board, then */ });
  return holder;
}

function dismissedReset() { try { return localStorage.getItem(RESET_KEY); } catch (e) { return null; } }
function rememberReset(weekStart) { try { localStorage.setItem(RESET_KEY, weekStart); } catch (e) { /* private mode */ } }

/**
 * "The board reset" for the top of For you on the first day of the week: a card with the way to the board and to last
 * week's winners, dismissible for the day (the week's start is remembered in localStorage). place(node) puts it where
 * the feed wants it; it is called at most once, and not at all on any other day, when the reader dismissed it, when
 * the board is not built yet, or while the week shown is closed. Never throws.
 */
export function boardResetCard(ctx, place) {
  const paint = (data) => {
    if (!data || data.closed || !data.weekStart) return;
    const start = Date.parse(data.weekStart);
    const age = Date.now() - start;
    if (!(age >= 0 && age < DAY) || dismissedReset() === data.weekStart) return;
    const card = el('div', { class: 'board-reset', id: 'board-reset', role: 'region', 'aria-label': t('board.reset') }, [
      el('div', { class: 'board-reset-text' }, [
        el('p', { class: 'board-reset-title' }, [icon('flame'), el('span', { text: t('board.reset') })]),
        el('div', { class: 'board-reset-links' }, [
          el('a', { class: 'btn-text', href: '#/board', text: t('board.strip_all') }),
          el('a', { class: 'btn-text', href: '#/board/hall', text: t('board.reset_cta') })
        ])
      ]),
      el('button', { type: 'button', class: 'icon-btn', id: 'board-reset-dismiss', 'aria-label': t('common.close'), onclick: () => { rememberReset(data.weekStart); card.remove(); } }, [icon('x')])
    ]);
    place(card);
  };
  if (cached && Date.now() - cached.at < CACHE_TTL) { paint(cached.data); return; }
  loadBoard().then((data) => { if (!ctx || !ctx.stale || !ctx.stale()) paint(data); }).catch(() => { /* no board, no card */ });
}

/**
 * The weekly badge for a profile head: last week's place on one board (ProfileDto.badge / MeDto.badge), worn for this
 * week, next to the brand mark; it opens the hall. Null when there is none, so callers append it unconditionally.
 */
export function profileBadge(badge) {
  if (!badge || !badge.rank) return null;
  const board = boardLabel(badge.board);
  const desc = t('badge.desc', { rank: fmtNumber(badge.rank), board });
  return el('a', { class: 'board-badge', id: 'profile-badge', href: '#/board/hall', title: desc, 'aria-label': desc }, [
    icon('flame'),
    el('span', { 'aria-hidden': 'true', text: t('badge.label', { rank: fmtNumber(badge.rank), board }) })
  ]);
}
