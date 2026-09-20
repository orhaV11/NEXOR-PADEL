// Round 14 — the loop that makes the tenth check better than the first. Three pieces, all of them the person's own rows:
//   1. The typed reason under the tip: "tried it, it worked" / "tried it, it didn't" / "not my style" / "I don't own that",
//      plus a skip and the optional free note. Four different facts, stored as four different answers
//      (POST /api/checks/{id}/useful with { reason }), because only the typed one can teach anything.
//   2. "I tried it": one tap remembers this check, the person photographs the look again through the ordinary check flow,
//      and the two results are shown together — both scores, both tips, and what changed in the combination. The second
//      check is a real check: the stylist is never told it is an attempt, and the pair is written only once both verdicts
//      exist (POST /api/checks/{id}/tried with { beforeId }). Then "which do you prefer?".
//   3. The taste card: what OREVOSH has learned, in a handful of lines the person would recognise, with the literal text
//      the stylist is sent, a switch to stop the learning and a button to clear it.
//
// MOUNTING. The result screen is views/check.js, which is not this module's file. It calls mountResult(container, check)
// once, after the tip, and this module fills whichever of these ids it finds inside the container, or appends its own
// nodes in that order when it finds none:
//   #taste-win      the one line about the last tip that worked ("last time you … and said it worked")
//   #taste-reasons  the row of typed reasons
//   #tried-action   the "I tried it" button, or the pair once the two checks are linked
// Nothing here needs check.js to exist: views/profile.js mounts the same pieces on #/checks, and views/settings.js mounts
// the card, so the loop is whole without the result screen.
import {
  el, icon, t, api, state, toast, sheet, confirmSheet, fmtNumber, fmtDate, intentLabel, navigate, requireSignIn
} from './core.js';

/** The four typed answers, in the order the row shows them. Mirrors Domain.TipReason on the server. */
export const REASONS = ['worked', 'didnt_work', 'not_my_style', 'dont_own'];

/** The item categories a "what changed" line can name; they borrow the item editor's labels. */
const CATEGORIES = ['top', 'bottom', 'dress', 'outerwear', 'shoes', 'accessory', 'other'];

const PENDING_KEY = 'orevosh.tried';

const CSS = `
.loop { display: grid; gap: 10px; padding: 14px 16px; background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); }
.loop h2 { font-family: var(--font-display); font-size: 17px; font-weight: 700; letter-spacing: 0; text-transform: none; color: var(--ink); margin: 0; }
.loop-choices { display: flex; flex-wrap: wrap; gap: 8px; }
.loop-choices .chip { flex: 1 1 46%; justify-content: center; min-block-size: 44px; font-size: 15px; }
.loop-note { display: grid; gap: 8px; }
.loop-note .row > .btn-text { flex: none; padding-block: 0; }
.loop-note .row > .btn-sm { min-block-size: 44px; }
.loop-skip { justify-self: start; min-block-size: 44px; padding-block: 0; }
.loop-said { display: flex; align-items: center; justify-content: space-between; gap: 10px; flex-wrap: wrap; }
.loop-said .btn-text { flex: none; padding-block: 0; }
.loop-win { margin: 0; color: var(--ink-2); font-style: italic; }
.loop-start { min-block-size: 44px; }
.loop-pending { display: flex; align-items: center; justify-content: space-between; gap: 10px; flex-wrap: wrap; }
.loop-pending .btn-text { flex: none; padding-block: 0; }
.pair-sides { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.pair-side { display: grid; gap: 6px; min-inline-size: 0; }
.pair-side .lbl { color: var(--ink-3); font-size: 12px; text-transform: uppercase; letter-spacing: .08em; }
.pair-side .num { font-family: var(--font-display); font-size: 30px; line-height: 1; font-weight: 800; color: var(--accent); direction: ltr; }
.pair-side .num small { font-size: 11px; color: var(--ink-3); font-weight: 600; margin-inline-start: 1px; }
.pair-side .headline { font-size: 15px; font-weight: 600; }
.pair-side .tip { font-size: 13px; color: var(--ink-2); }
.pair-side.won .num { color: var(--ok, var(--accent)); }
.pair-changed { margin: 0; padding-inline-start: 18px; }
.pair-changed li { font-size: 14px; }
.pair-honest { margin: 0; color: var(--ink-3); font-size: 12px; }
.taste-card { display: grid; gap: 12px; }
.taste-card .taste-facts { display: grid; gap: 10px; }
.taste-line { display: grid; gap: 4px; }
.taste-line .lbl { color: var(--ink-3); font-size: 12px; text-transform: uppercase; letter-spacing: .08em; }
.taste-line .chips { display: flex; flex-wrap: wrap; gap: 6px; }
.taste-sent { border-block-start: 1px solid var(--line); padding-block-start: 12px; display: grid; gap: 6px; }
.taste-sent pre { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 12px; line-height: 1.5; color: var(--ink-2); background: var(--surface-2, transparent); padding: 10px; border-radius: var(--radius-sm, 8px); }
.taste-actions { display: flex; flex-wrap: wrap; gap: 10px; }
.taste-actions .btn { min-block-size: 44px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

// ---------- the pending attempt ----------

/**
 * "I tried it" remembers one check id in sessionStorage, so the person can walk through the camera, the check screen and
 * the result and still be offered the link when they come back. Nothing here is sent anywhere: the second check goes out
 * as an ordinary check, and the pair is made afterwards.
 */
export function pendingAttempt() {
  try {
    const raw = sessionStorage.getItem(PENDING_KEY);
    const value = raw ? JSON.parse(raw) : null;
    return value && value.beforeId ? value : null;
  } catch (e) { return null; }
}
export function setPendingAttempt(beforeId) {
  try {
    if (beforeId) sessionStorage.setItem(PENDING_KEY, JSON.stringify({ beforeId, startedAt: new Date().toISOString() }));
    else sessionStorage.removeItem(PENDING_KEY);
  } catch (e) { /* private mode: the loop still works, it just is not remembered across a reload */ }
}

// ---------- the taste card's data ----------

let tasteCache = null;
/** GET /api/users/me/taste, kept for the render; pass true after a change. Null when signed out or the call failed. */
export async function loadTaste(force) {
  if (!state.me) return null;
  if (tasteCache && !force) return tasteCache;
  try { tasteCache = await api('GET', '/api/users/me/taste'); } catch (e) { tasteCache = null; }
  return tasteCache;
}
export function forgetTaste() { tasteCache = null; }

/** GET /api/users/me/tried: the person's own pairs, newest first. [] when signed out or the call failed. */
export async function loadPairs() {
  if (!state.me) return [];
  try { const answer = await api('GET', '/api/users/me/tried'); return (answer && answer.items) || []; }
  catch (e) { return []; }
}

// ---------- 1. the typed reason ----------

/**
 * The row of taps under the tip. opts: { ids } to use the documented result-screen ids (one row per page only),
 * { onSaved(check) } after every stored answer. A check that was answered before shows what was said, with a way back.
 */
export function reasonRow(check, opts) {
  opts = opts || {};
  ensureStyle();
  const withIds = !!opts.ids;
  const section = el('section', { class: 'loop loop-reasons', id: withIds ? 'taste-reasons' : null, 'aria-label': t('tried.question') });
  section.appendChild(el('h2', { text: t('tried.question') }));
  const body = el('div');
  section.appendChild(body);
  let busy = false;

  const save = async (reason, note) => {
    if (busy) return false;
    busy = true;
    try {
      const payload = note ? { reason, note } : { reason };
      const saved = await api('POST', '/api/checks/' + encodeURIComponent(check.id) + '/useful', payload);
      check.useful = saved.useful;
      check.usefulAt = saved.usefulAt;
      check.usefulNote = saved.note || null;
      check.usefulReason = saved.reason || null;
      if (opts.onSaved) opts.onSaved(check);
      forgetTaste();
      return true;
    } catch (e) {
      toast(e && e.message ? e.message : t('error.generic'));
      return false;
    } finally { busy = false; }
  };

  const thanks = () => {
    body.replaceChildren(el('p', { class: 'muted', role: 'status', text: t('tried.thanks') }));
  };

  const askNote = (reason) => {
    const input = el('input', {
      type: 'text', maxlength: '120', autocomplete: 'off', enterkeyhint: 'send',
      placeholder: t('tried.note_placeholder'), value: check.usefulNote || ''
    });
    const send = el('button', { type: 'submit', class: 'btn btn-sm', text: t('tried.send') });
    const skip = el('button', { type: 'button', class: 'btn-text', text: t('tried.skip'), onclick: thanks });
    const label = el('label', { text: t('tried.note_label') });
    const form = el('form', { class: 'loop-note', novalidate: true, onsubmit: async (event) => {
      event.preventDefault();
      const note = input.value.trim();
      if (!note) { thanks(); return; }
      send.disabled = true;
      if (await save(reason, note)) thanks(); else send.disabled = false;
    } }, [label, input, el('div', { class: 'row' }, [send, skip])]);
    body.replaceChildren(form);
    requestAnimationFrame(() => { if (document.contains(input)) input.focus({ preventScroll: true }); });
  };

  const choices = () => {
    const group = el('div', { class: 'loop-choices', role: 'group', 'aria-label': t('tried.group') });
    const pick = async (reason, chip) => {
      for (const other of group.children) other.setAttribute('aria-pressed', String(other === chip));
      chip.setAttribute('aria-busy', 'true');
      const ok = await save(reason, null);
      chip.removeAttribute('aria-busy');
      if (ok) askNote(reason);
      else for (const other of group.children) other.setAttribute('aria-pressed', 'false');
    };
    for (const reason of REASONS) {
      const chip = el('button', {
        type: 'button', class: 'chip', 'data-reason': reason, 'aria-pressed': 'false', text: t('tried.' + reason),
        id: withIds ? 'reason-' + reason : null
      });
      chip.addEventListener('click', () => pick(reason, chip));
      group.appendChild(chip);
    }
    const skip = el('button', { type: 'button', class: 'btn-text loop-skip', id: withIds ? 'reason-skip' : null, text: t('tried.skip'), onclick: thanks });
    body.replaceChildren(group, skip);
  };

  const said = () => {
    body.replaceChildren(el('div', { class: 'loop-said' }, [
      el('span', { class: 'muted', text: t('tried.saved', { reason: t('tried.' + check.usefulReason) }) }),
      el('button', { type: 'button', class: 'btn-text', text: t('tried.change'), onclick: choices })
    ]));
  };

  if (REASONS.includes(check.usefulReason)) said(); else choices();
  return section;
}

// ---------- 2. "I tried it" ----------

/**
 * The button that starts the second check, the "waiting" state once it has been tapped, and the pair once the two checks
 * are linked. opts: { ids } for the documented result-screen id, { pair } when the pair is already known,
 * { onLinked(pair) }, { onNavigate } instead of going to the check screen.
 */
export function triedBlock(check, opts) {
  opts = opts || {};
  ensureStyle();
  const host = el('div', { id: opts.ids ? 'tried-action' : null });
  paintTried(host, check, opts);
  return host;
}

function paintTried(host, check, opts) {
  host.replaceChildren();
  if (opts.pair) { host.appendChild(pairBlock(opts.pair, opts)); return; }
  const pending = pendingAttempt();
  if (pending && pending.beforeId === check.id) {
    host.appendChild(el('div', { class: 'loop loop-pending' }, [
      el('span', { class: 'muted', text: t('tried.pending') }),
      el('button', { type: 'button', class: 'btn-text', text: t('tried.pending_cancel'), onclick: () => { setPendingAttempt(null); paintTried(host, check, opts); } })
    ]));
    return;
  }
  const start = el('button', { type: 'button', class: 'btn btn-secondary loop-start', id: opts.ids ? 'tried-start' : null }, [
    icon('camera'), t('tried.start')
  ]);
  start.addEventListener('click', () => {
    if (!requireSignIn()) return;
    setPendingAttempt(check.id);
    paintTried(host, check, opts);
    if (opts.onNavigate) opts.onNavigate(); else navigate('#/check');
  });
  host.appendChild(el('div', { class: 'loop' }, [start, el('p', { class: 'hint', text: t('tried.start_hint') })]));
}

/**
 * Offers to link a candidate check to the attempt the person started, and links it when they say yes. Resolves with the
 * pair, or null when there was nothing to offer or they said no. The pair is made only here, with both verdicts already
 * stored: the second check went out as an ordinary check and the stylist was told nothing about the first.
 */
export async function offerLink(candidate) {
  const pending = pendingAttempt();
  if (!pending || !candidate || candidate.id === pending.beforeId || candidate.status !== 'ok') return null;
  const ok = await confirmSheet(t('tried.confirm_title'), candidate.feedback && candidate.feedback.headline, t('tried.confirm_yes'));
  if (!ok) return null;
  try {
    const pair = await api('POST', '/api/checks/' + encodeURIComponent(candidate.id) + '/tried', { beforeId: pending.beforeId });
    setPendingAttempt(null);
    toast(t('tried.linked'));
    return pair;
  } catch (e) {
    // A refusal that can never succeed (the pair is taken, the order is wrong) clears the attempt; a network hiccup does not.
    if (e && (e.status === 400 || e.status === 404 || e.status === 409)) setPendingAttempt(null);
    toast(e && e.message ? e.message : t('error.generic'));
    return null;
  }
}

/** One line of what changed in the combination, from the server's per-category list. */
function changeLine(change) {
  const category = t('items.cat_' + (CATEGORIES.includes(change.category) ? change.category : 'other'));
  if (change.from && change.to) return t('tried.change_swap', { category, from: change.from, to: change.to });
  if (change.to) return t('tried.change_added', { category, to: change.to });
  return t('tried.change_removed', { category, from: change.from });
}

function pairSide(side, which, pair) {
  const won = pair.preferred === which;
  return el('div', { class: 'pair-side' + (won ? ' won' : ''), 'data-side': which }, [
    el('span', { class: 'lbl', text: t('tried.' + which) }),
    el('div', { class: 'num', role: 'img', 'aria-label': t('a11y.score', { score: fmtNumber(side.score) }) }, [
      fmtNumber(side.score), el('small', { text: t('result.out_of') })
    ]),
    el('div', { class: 'hint', text: intentLabel(side.intent) + ' · ' + fmtDate(side.createdAt) }),
    side.headline ? el('div', { class: 'headline', dir: 'auto', text: side.headline }) : null,
    side.oneTip ? el('div', { class: 'tip', dir: 'auto', text: side.oneTip }) : null
  ]);
}

/** The two results together: both scores, both tips, what changed, and "which do you prefer?". */
export function pairBlock(pair, opts) {
  opts = opts || {};
  ensureStyle();
  const section = el('section', { class: 'loop pair', id: opts.ids ? 'tried-pair' : null, 'aria-label': t('tried.pair_title') });
  const paint = () => {
    const changes = (pair.changed || []).map(changeLine);
    const prefer = el('div', { class: 'loop-choices', role: 'group', 'aria-label': t('tried.prefer') });
    const choose = async (which, chip) => {
      for (const other of prefer.children) other.setAttribute('aria-pressed', String(other === chip));
      try {
        const saved = await api('POST', '/api/checks/' + encodeURIComponent(pair.after.id) + '/tried/prefer', { prefer: which });
        Object.assign(pair, saved);
        if (opts.onLinked) opts.onLinked(pair);
        paint();
      } catch (e) {
        for (const other of prefer.children) other.setAttribute('aria-pressed', 'false');
        toast(e && e.message ? e.message : t('error.generic'));
      }
    };
    for (const which of ['before', 'after']) {
      const chip = el('button', { type: 'button', class: 'chip', 'data-prefer': which, 'aria-pressed': String(pair.preferred === which), text: t('tried.prefer_' + which) });
      chip.addEventListener('click', () => choose(which, chip));
      prefer.appendChild(chip);
    }
    section.replaceChildren(
      el('h2', { text: t('tried.pair_title') }),
      el('div', { class: 'pair-sides' }, [pairSide(pair.before, 'before', pair), pairSide(pair.after, 'after', pair)]),
      el('div', { class: 'taste-line' }, [
        el('span', { class: 'lbl', text: t('tried.changed') }),
        changes.length
          ? el('ul', { class: 'pair-changed' }, changes.map((line) => el('li', { dir: 'auto', text: line })))
          : el('p', { class: 'muted', text: t('tried.change_none') })
      ]),
      el('p', { class: 'pair-honest', text: t('tried.honest') }),
      el('div', { class: 'taste-line' }, [el('span', { class: 'lbl', text: t('tried.prefer') }), prefer]),
      pair.preferred ? el('p', { class: 'muted', text: t('tried.preferred_' + pair.preferred) }) : null
    );
  };
  paint();
  return section;
}

// ---------- 3. the taste card ----------

/** "Last time you … and said it worked", from the person's own row. Null when there is none. */
export function winLine(win, opts) {
  if (!win || !win.tip) return null;
  ensureStyle();
  return el('p', { class: 'loop-win', id: (opts && opts.ids) ? 'taste-win' : null, dir: 'auto', text: t('taste.win', { tip: win.tip }) });
}

function chipLine(label, values) {
  if (!values || !values.length) return null;
  return el('div', { class: 'taste-line' }, [
    el('span', { class: 'lbl', text: label }),
    el('div', { class: 'chips' }, values.map((value) => el('span', { class: 'chip', dir: 'auto', text: value })))
  ]);
}

/**
 * "What OREVOSH has learned about your taste": the facts, and the literal text the stylist is sent, so nothing in it is a
 * secret from its subject. opts: { controls } adds the switch and the clear button (Settings), { onChange(card) }.
 * Renders into a node that is returned at once and fills itself when the card arrives.
 */
export function tasteCard(opts) {
  opts = opts || {};
  ensureStyle();
  const host = el('section', { class: 'loop taste-card', id: 'taste-card', 'aria-label': t('taste.title') });
  host.appendChild(el('h2', { text: t('taste.title') }));
  const body = el('div');
  host.appendChild(body);

  const paint = (card) => {
    if (!card) { body.replaceChildren(el('p', { class: 'muted', text: t('taste.empty') })); return; }
    const facts = card.facts || {};
    const reasons = (facts.reasons || []).map((row) => t('taste.count', { label: t('tried.' + row.name), n: fmtNumber(row.n) }));
    const nodes = [el('p', { class: 'hint', text: t('taste.lede') })];
    if (!card.learning) nodes.push(el('p', { class: 'muted', id: 'taste-off', text: t('taste.off') }));
    else if (card.empty) nodes.push(el('p', { class: 'muted', id: 'taste-empty', text: t('taste.empty') }));
    else {
      nodes.push(el('div', { class: 'taste-facts' }, [
        el('p', { class: 'hint' }, [
          t('taste.checks_n', { n: facts.checks === 1 ? 1 : fmtNumber(facts.checks || 0) }),
          ' · ',
          t('taste.posted_n', { n: facts.posted === 1 ? 1 : fmtNumber(facts.posted || 0) })
        ]),
        chipLine(t('taste.occasions'), (facts.intents || []).map((row) => intentLabel(row.name))),
        chipLine(t('taste.pieces'), facts.pieces),
        chipLine(t('taste.colours'), facts.colours),
        chipLine(t('taste.reasons'), reasons),
        chipLine(t('taste.avoid'), facts.avoid),
        chipLine(t('taste.notes'), facts.notes)
      ]));
    }
    nodes.push(el('div', { class: 'taste-sent' }, [
      el('span', { class: 'lbl', text: t('taste.sent_title') }),
      card.advisory
        ? el('pre', { id: 'taste-advisory', dir: 'ltr', text: card.advisory })
        : el('p', { class: 'muted', id: 'taste-advisory-none', text: t('taste.sent_none') }),
      el('p', { class: 'hint', text: t('taste.sent_hint') })
    ]));
    if (card.clearedAt) nodes.push(el('p', { class: 'hint', id: 'taste-cleared', text: t('taste.cleared_on', { date: fmtDate(card.clearedAt) }) }));
    if (opts.controls) nodes.push(controls(card, paint, opts));
    body.replaceChildren(...nodes.filter(Boolean));
  };

  paint(null);
  loadTaste(true).then((card) => { paint(card); if (opts.onChange) opts.onChange(card); });
  return host;
}

/** The switch that stops the learning and the button that clears it. Both are honoured at once. */
function controls(card, paint, opts) {
  const input = el('input', { type: 'checkbox', id: 'taste-learning', name: 'taste-learning', checked: card.learning });
  input.addEventListener('change', async () => {
    input.disabled = true;
    try {
      const saved = await api('PATCH', '/api/users/me/taste', { learning: input.checked });
      forgetTaste();
      toast(t('taste.saved'));
      paint(saved);
      if (opts.onChange) opts.onChange(saved);
    } catch (e) {
      input.checked = card.learning;
      toast(e && e.message ? e.message : t('error.generic'));
    } finally { input.disabled = false; }
  });
  const clear = el('button', { type: 'button', class: 'btn btn-secondary', id: 'taste-clear', text: t('taste.clear') });
  clear.addEventListener('click', async () => {
    if (clear.disabled) return;
    if (!await confirmSheet(t('taste.clear_title'), t('taste.clear_body'), t('taste.clear'), true)) return;
    clear.disabled = true;
    try {
      const saved = await api('DELETE', '/api/users/me/taste');
      forgetTaste();
      toast(t('taste.cleared'));
      paint(saved);
      if (opts.onChange) opts.onChange(saved);
    } catch (e) {
      toast(e && e.message ? e.message : t('error.generic'));
    } finally { clear.disabled = false; }
  });
  return el('div', { class: 'taste-actions' }, [
    el('label', { class: 'switch', for: 'taste-learning' }, [
      el('span', { class: 's-switch-text' }, [el('b', { text: t('taste.learning') }), el('span', { class: 'hint', text: t('taste.learning_hint') })]),
      input
    ]),
    clear
  ]);
}

// ---------- mounting on the result screen ----------

/**
 * Everything the result screen shows for an ok check, in one call: the "last time it worked" line, the row of typed
 * reasons and the "I tried it" block (or the pair, when this check is already half of one). views/check.js calls this
 * once, after the tip, with the container it drew the result into; the three documented ids (#taste-win, #taste-reasons,
 * #tried-action) are filled when they are there and the nodes are appended in that order when they are not.
 */
export function mountResult(container, check) {
  if (!container || !check || check.status !== 'ok') return;
  ensureStyle();
  const place = (id, node) => {
    if (!node) return;
    const slot = container.querySelector('#' + id);
    if (slot) slot.replaceChildren(node); else container.appendChild(node);
  };

  place('taste-reasons', reasonRow(check, { ids: !container.querySelector('#taste-reasons') }));
  const tried = triedBlock(check, { ids: !container.querySelector('#tried-action') });
  place('tried-action', tried);

  // The two reads that need the network come after the screen is whole, so nothing waits on them.
  if (!state.me) return;
  loadTaste().then((card) => {
    const line = winLine(card && card.lastWin, { ids: true });
    if (line && card.lastWin.checkId !== check.id) {
      const slot = container.querySelector('#taste-win');
      if (slot) slot.replaceChildren(line); else container.insertBefore(line, container.firstChild);
    }
  });
  loadPairs().then(async (pairs) => {
    const mine = pairs.find((pair) => pair.before.id === check.id || pair.after.id === check.id);
    if (mine) { tried.replaceChildren(pairBlock(mine, { ids: true })); return; }
    const pair = await offerLink(check);
    if (pair) tried.replaceChildren(pairBlock(pair, { ids: true }));
  });
}

/** A sheet with the card, for a place that has no room for it. Used by the private lists. */
export function openTasteSheet() {
  sheet({ title: t('taste.title'), content: el('div', { class: 'stack' }, [tasteCard({ controls: true })]) });
}
