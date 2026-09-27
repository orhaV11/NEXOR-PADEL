// Round 14 — the wardrobe that builds itself. The quiet line under the tip on the result screen: "Keep the camel coat in
// your wardrobe?", one tap, no form. Nothing here asks anyone to photograph a closet; the only pieces it can ever offer
// are the ones the stylist just named on this very check, and the server enforces that too.
//
// views/check.js mounts it at the documented point, right after the "did the tip land?" row:
//   import { wardrobeKeep } from '../wardrobe.js';
//   container.appendChild(wardrobeKeep(result));            // #wardrobe-keep, hidden until it has something to offer
// It never throws and never blocks the screen: signed out, offline, or with every piece already kept, the row stays
// hidden and the result screen looks exactly as it did.
//
// The list itself is views/wardrobe.js (#/wardrobe); this module also owns the one cached read of GET /api/wardrobe, so
// the result screen and the list do not ask twice.
//
// Round 20 — filling it faster: "Keep all N" beside Keep / Not this one when the check named more than one piece (one
// POST /api/wardrobe/keep-all, the server keeps its own list of names), and the Pro moment under a keep: one true line
// from the server (wardrobe.proMoment) with a Go Pro button, once per session, tallied through POST /api/wardrobe/moment.
// The moment's memory (momentUnseen / markMoment) and its sentence (momentText) are shared with views/wardrobe.js.
import { state, t, el, api, icon, toast } from './core.js';

/** The cached GET /api/wardrobe, whose account it belongs to, and the read in flight. */
let cached = null;
let cachedFor = null;
let inFlight = null;

/** Drop the cache: a keep, a rename or a delete makes what we hold stale. */
export function forgetWardrobe() {
  cached = null;
  cachedFor = null;
  inFlight = null;
}

/**
 * The wardrobe, read once and remembered. Null on any failure and signed out: the caller shows nothing rather than an
 * error. The cache is keyed by the account, so signing in as somebody else on the same phone never shows their pieces.
 */
export async function loadWardrobe(force) {
  const me = state.me ? state.me.id : null;
  if (force || me !== cachedFor) forgetWardrobe();
  if (!me) return null;
  if (cached) return cached;
  if (!inFlight) {
    inFlight = api('GET', '/api/wardrobe')
      .then((data) => { cached = data; cachedFor = me; return data; })
      .catch(() => null)
      .finally(() => { inFlight = null; });
  }
  return inFlight;
}

/** The names a check named, in the order the stylist gave them: its items, then the accessories it saw. */
export function piecesOn(result) {
  const feedback = (result && result.feedback) || {};
  if ((feedback.status || result.status) !== 'ok') return [];
  const seen = new Set();
  const names = [];
  const add = (name) => {
    const text = String(name || '').trim();
    const key = text.toLowerCase();
    if (text && !seen.has(key)) { seen.add(key); names.push(text); }
  };
  for (const item of feedback.items || []) add(item.name);
  for (const piece of (feedback.accessories && feedback.accessories.present) || []) add(piece);
  return names;
}

// ---------- Round 20 — the Pro moment ----------

/** Where this tab remembers that the moment was shown: the account id, so a different account on the same phone sees its own. */
const MOMENT_KEY = 'orevosh.wardrobe.moment';

/** True while this tab has not shown the moment to the signed-in account. sessionStorage may be absent or refused; then it is shown. */
export function momentUnseen() {
  const me = state.me ? String(state.me.id) : null;
  if (!me) return false;
  try { return sessionStorage.getItem(MOMENT_KEY) !== me; } catch (e) { return true; }
}

/** Remember that the moment was shown to this account in this tab. */
export function markMoment() {
  if (!state.me) return;
  try { sessionStorage.setItem(MOMENT_KEY, String(state.me.id)); } catch (e) { /* a private window with storage refused: shown again next time, which is honest */ }
}

/**
 * The one line the moment says. "All of them" only while the wardrobe fits what Pro's stylist sees (wardrobe.proSees,
 * Plans:WardrobeNamesToStylistPro); past that the sentence names the number, so it never promises more than the server does.
 */
export function momentText(data) {
  const n = ((data && data.items) || []).length;
  const pro = (data && data.proSees) || 0;
  return pro > 0 && n > pro ? t('wardrobe.moment_many', { n, pro }) : t('wardrobe.moment', { n });
}

/** Tell the server the moment was shown or its button tapped. A tally, never awaited and never an error on the screen. */
export const tallyMoment = (step) => api('POST', '/api/wardrobe/moment', { step }).catch(() => {});

/**
 * The moment as a notice: the sentence and the Go Pro button, which says where it came from (the Pro page tallies that
 * for the funnel) and posts "go" before it navigates. The caller decides whether to draw it (data.proMoment && momentUnseen()).
 */
export function momentNotice(data, ids) {
  markMoment();
  tallyMoment('shown');
  return el('div', { class: 'notice wr-moment', id: ids.box }, [
    el('p', { text: momentText(data) }),
    el('a', { class: 'btn btn-sm', id: ids.go, href: '#/pro?from=wardrobe', text: t('pro.go'), onclick: () => tallyMoment('go') })
  ]);
}

let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    // Round 21: the one green surface on the screen, because "keep" is the works colour — a mint wash over glass with a
    // mint edge, the bag on a mint disc with a soft glow; the KEEP button stays the gradient.
    '.keep-row { display: grid; grid-template-columns: 34px 1fr; gap: 6px 12px; align-items: center; padding: 14px 16px; background: linear-gradient(var(--ok-tint), var(--ok-tint)), var(--glass); border: 1px solid rgba(111, 240, 173, 0.22); border-radius: var(--radius); box-shadow: var(--shadow-card); }',
    '.keep-row p { grid-column: 2; margin: 0; font-size: 15px; line-height: 1.4; color: var(--ink); unicode-bidi: plaintext; }',
    '.keep-row .keep-icon { grid-row: 1; inline-size: 34px; block-size: 34px; border-radius: 50%; display: grid; place-items: center; background: rgba(111, 240, 173, 0.18); color: var(--ok); box-shadow: 0 0 16px rgba(111, 240, 173, 0.25); }',
    '.keep-row .keep-icon svg { inline-size: 18px; block-size: 18px; }',
    // Round 20: three controls (Keep, Not this one, Keep all N) have to wrap on a narrow phone rather than overflow.
    '.keep-row .keep-answers { grid-column: 2; display: flex; align-items: center; gap: 14px; flex-wrap: wrap; row-gap: 4px; }',
    '.keep-row .keep-skip, .keep-row .keep-all { min-inline-size: 44px; min-block-size: 44px; }',
    '.keep-done { grid-column: 2; display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }',
    '.keep-done p { grid-column: auto; }',
    '.keep-done a { font-weight: 600; }',
    '.keep-done .hint { flex-basis: 100%; margin: 0; }',
    '.keep-done .wr-moment { flex-basis: 100%; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; padding: 12px 14px; }',
    '.keep-done .wr-moment p { flex: 1; min-inline-size: 12ch; margin: 0; font-size: 14px; color: var(--ink-2); unicode-bidi: plaintext; }',
    '.keep-done .wr-moment a { font-weight: 600; }'
  ].join('\n') }));
}

/**
 * wardrobeKeep(result) → the row, hidden until it finds a piece worth offering.
 *
 * One piece at a time, in the stylist's own order, skipping what this account already keeps. A keep is one POST and the
 * row then offers the next piece, so a wardrobe fills itself over a few checks instead of over an hour of forms. "Not
 * this one" moves on without storing anything; when the pieces run out the row goes quiet for good.
 */
/**
 * What a keep pays back, read from the wardrobe itself so every number is true rather than counted here. Round 19: how
 * far the closet is from its first planned outfit ("1 of 2 kinds"), or the door to it once two kinds are in. Round 20:
 * the Pro moment, when the server says the wardrobe has passed what a free account's stylist sees and this tab has not
 * shown it yet. Nothing where the row is gone from the screen.
 */
async function payoff(row) {
  const plans = (state.config && state.config.plans) || {};
  const data = await loadWardrobe(true);
  if (!data || !row.isConnected) return;
  const done = row.querySelector('.keep-done');
  if (!done) return;
  if (plans.tomorrow) {
    const kinds = new Set((data.items || []).map((item) => item.category).filter((c) => c && c !== 'other')).size;
    const needKinds = plans.suggestionMinCategories || 2;
    done.appendChild(kinds >= needKinds
      ? el('a', { class: 'btn-text', id: 'wardrobe-kept-tomorrow', href: '#/tomorrow', text: t('tomorrow.from_wardrobe') })
      : el('p', { class: 'hint', id: 'wardrobe-kept-progress', text: t('tomorrow.progress', { n: kinds, of: needKinds }) }));
  }
  if (data.proMoment && momentUnseen()) done.appendChild(momentNotice(data, { box: 'wardrobe-keep-moment', go: 'wardrobe-keep-moment-go' }));
}

export function wardrobeKeep(result) {
  const row = el('div', { class: 'keep-row', id: 'wardrobe-keep', hidden: true });
  if (!state.me || !result || !result.id) return row;
  const names = piecesOn(result);
  if (names.length === 0) return row;
  ensureStyle();

  let queue = [];
  let busy = false;

  const done = (name) => {
    row.replaceChildren(
      el('span', { class: 'keep-icon', 'aria-hidden': 'true' }, [icon('check')]),
      el('div', { class: 'keep-done' }, [
        el('p', { role: 'status', dir: 'auto', text: t('wardrobe.kept', { piece: name }) }),
        el('a', { class: 'btn-text', id: 'wardrobe-kept-link', href: '#/wardrobe', text: t('wardrobe.open') })
      ])
    );
    // Round 19 — each keep has a visible payoff: how far the closet is from its first planned outfit, and once it is
    // there, the door to it. Read from the wardrobe itself, so the number is true rather than counted here.
    payoff(row);
    // A moment to read it, then the next piece if there is one. Never more than one question on the screen at a time.
    // A Pro moment drawn under a single keep goes with the row when the next piece is asked; it lives on #/wardrobe
    // too, which the "See it" link opens, and the once-per-session mark is already set.
    if (queue.length > 0) setTimeout(() => { if (row.isConnected && queue.length > 0) ask(); }, 2200);
  };

  // Round 20 — after "Keep all": the count the server kept, the link, and (when the cap kept some out) why the list is
  // shorter than the check. The queue is empty by then, so nothing asks again; the payoff reads the wardrobe as after
  // any keep, which is where the Pro moment appears when it is true.
  const doneAll = (res) => {
    const kept = (res && res.kept) || 0;
    const block = el('div', { class: 'keep-done' }, [
      el('p', { role: 'status', dir: 'auto', text: t('wardrobe.kept_all', { n: kept }) }),
      el('a', { class: 'btn-text', id: 'wardrobe-kept-link', href: '#/wardrobe', text: t('wardrobe.open') })
    ]);
    if (res && res.full) block.appendChild(el('p', { class: 'hint', id: 'wardrobe-keep-all-full', text: t('wardrobe.kept_all_full', { max: res.max }) }));
    row.replaceChildren(el('span', { class: 'keep-icon', 'aria-hidden': 'true' }, [icon('check')]), block);
    payoff(row);
  };

  const keepAll = async () => {
    if (busy) return;
    busy = true;
    try {
      const res = await api('POST', '/api/wardrobe/keep-all', { checkId: result.id });
      forgetWardrobe();
      queue = [];
      doneAll(res);
    } catch (e) {
      // 409 when nothing fitted: the server's own sentence, and the row stays with its question.
      toast((e && e.message) || t('error.generic'));
    } finally {
      busy = false;
    }
  };

  const keep = async (name) => {
    if (busy) return;
    busy = true;
    try {
      await api('POST', '/api/wardrobe', { checkId: result.id, name });
      forgetWardrobe();
      done(name);
    } catch (e) {
      toast((e && e.message) || t('error.generic'));
    } finally {
      busy = false;
    }
  };

  const ask = () => {
    const name = queue.shift();
    if (!name) { row.hidden = true; return; }
    const button = el('button', { type: 'button', class: 'btn btn-sm', id: 'wardrobe-keep-yes', text: t('wardrobe.keep_yes'), onclick: () => keep(name) });
    const skip = el('button', { type: 'button', class: 'btn-text keep-skip', id: 'wardrobe-keep-skip', text: t('wardrobe.keep_skip'), onclick: () => ask() });
    const answers = [button, skip];
    // Round 20: with more than one piece left to offer, the third answer keeps them all in one request.
    const left = 1 + queue.length;
    if (left >= 2) answers.push(el('button', { type: 'button', class: 'btn-text keep-all', id: 'wardrobe-keep-all', text: t('wardrobe.keep_all', { n: left }), onclick: () => keepAll() }));
    row.replaceChildren(
      el('span', { class: 'keep-icon', 'aria-hidden': 'true' }, [icon('bag')]),
      el('p', { dir: 'auto', text: t('wardrobe.keep_ask', { piece: name }) }),
      el('div', { class: 'keep-answers' }, answers)
    );
    row.hidden = false;
  };

  // The read is quiet: until it answers there is no row, and a failure leaves none.
  loadWardrobe().then((wardrobe) => {
    const kept = new Set(((wardrobe && wardrobe.items) || []).map((item) => String(item.name || '').toLowerCase()));
    queue = names.filter((name) => !kept.has(name.toLowerCase()));
    if (queue.length > 0) ask();
  });

  return row;
}
