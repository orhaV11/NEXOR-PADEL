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

let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '.keep-row { display: grid; grid-template-columns: 34px 1fr; gap: 6px 12px; align-items: center; padding: 14px 16px; background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); }',
    '.keep-row p { grid-column: 2; margin: 0; font-size: 15px; line-height: 1.4; color: var(--ink); unicode-bidi: plaintext; }',
    '.keep-row .keep-icon { grid-row: 1; inline-size: 34px; block-size: 34px; border-radius: 50%; display: grid; place-items: center; background: var(--accent-tint); color: var(--accent); }',
    '.keep-row .keep-icon svg { inline-size: 18px; block-size: 18px; }',
    '.keep-row .keep-answers { grid-column: 2; display: flex; align-items: center; gap: 14px; }',
    '.keep-row .keep-skip { min-inline-size: 44px; min-block-size: 44px; }',
    '.keep-done { grid-column: 2; display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }',
    '.keep-done p { grid-column: auto; }',
    '.keep-done a { font-weight: 600; }'
  ].join('\n') }));
}

/**
 * wardrobeKeep(result) → the row, hidden until it finds a piece worth offering.
 *
 * One piece at a time, in the stylist's own order, skipping what this account already keeps. A keep is one POST and the
 * row then offers the next piece, so a wardrobe fills itself over a few checks instead of over an hour of forms. "Not
 * this one" moves on without storing anything; when the pieces run out the row goes quiet for good.
 */
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
    // A moment to read it, then the next piece if there is one. Never more than one question on the screen at a time.
    if (queue.length > 0) setTimeout(() => { if (row.isConnected && queue.length > 0) ask(); }, 2200);
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
    row.replaceChildren(
      el('span', { class: 'keep-icon', 'aria-hidden': 'true' }, [icon('bag')]),
      el('p', { dir: 'auto', text: t('wardrobe.keep_ask', { piece: name }) }),
      el('div', { class: 'keep-answers' }, [button, skip])
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
