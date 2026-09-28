// Round 19 — Tomorrow (#/tomorrow): what should I wear, from the pieces you already kept. Pick where you are going;
// the stylist puts an outfit together from your own wardrobe and shows you wearing each piece — the photo of the check
// it was kept from. Nothing here asks anyone to photograph a closet: the wardrobe built itself, one keep at a time.
//
// The screen never spends on open. GET /api/tomorrow is the first paint (the strip of your own pieces, the recent
// outfits, the numbers under the button) and a stored answer to the same question is handed back without a call;
// only the button, "another idea" and "refresh" ever reach the model. The weather is the server's to fetch: the
// browser asks the person for their place only when they tap for it, rounds it to about a kilometre, keeps it in the
// phone's prefs and sends it with the request — no row and no log line ever holds it.
import {
  register, state, t, el, api, icon, setTopBar, signInPrompt, emptyState, errorBlock, toast, sheet, closeSheet, relative, fmtDate,
  hasMessage, loadPrefs, savePrefs, navigate, logoMark, $
} from '../core.js';
import { occasionChips, styleChips, OCCASIONS, STYLES } from './check.js';
import { forgetWardrobe } from '../wardrobe.js';

// Round 21, the look. The screen's rules live here (DESIGN §10): the day pills at 44px, the occasion row scrolling under
// them with no heading of its own, the style row and the forecast behind one folded line (details.tm-more), the closet
// strip as small prints in the gradient ring, the outfit as a glass card lit in its occasion's tint with the photo
// large beside the pieces as name chips, the forecast as the one amber pill, and the empty state under the mark.
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '#tomorrow.stack > * + * { margin-block-start: 14px; }',
    '#tomorrow > .tm-lede + * { margin-block-start: 0; }',   /* the lede is read, not seen (sr-only): the controls explain themselves */
    '.tm-when { display: flex; gap: 8px; }',
    '.tm-when .chip { flex: 1; justify-content: center; min-block-size: 44px; }',
    /* the occasion row runs to the screen's edges and scrolls, as the feed's filter does; the group carries its name for a reader */
    '#tomorrow .chips.scroll { margin-inline: -16px; padding-inline: 16px; scroll-padding-inline: 16px; }',
    /* the fold: the style row, the forecast line and the place behind one 44px line; the chevron turns when it opens.
       Element-qualified on purpose: .tm-tile.tm-more is the strip's "+N more" tile */
    'details.tm-more > summary { list-style: none; display: flex; align-items: center; gap: 8px; min-block-size: 44px; padding-inline: 4px; color: var(--ink-2); font-size: 14px; font-weight: 600; cursor: pointer; -webkit-tap-highlight-color: transparent; }',
    'details.tm-more > summary::-webkit-details-marker { display: none; }',
    'details.tm-more > summary .hint { margin: 0; min-inline-size: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 500; }',
    /* the chevron is a glyph: physical right + bottom edges, so it points down and up in both directions */
    'details.tm-more > summary::after { content: ""; flex: none; margin-inline-start: auto; margin-inline-end: 8px; margin-block-start: -4px; inline-size: 8px; block-size: 8px; border-right: 2px solid var(--ink-3); border-bottom: 2px solid var(--ink-3); transform: rotate(45deg); transition: transform 160ms ease, margin 160ms ease; }',
    'details.tm-more[open] > summary::after { transform: rotate(-135deg); margin-block-start: 4px; }',
    'details.tm-more > :not(summary) { margin-block-start: 6px; }',
    'details.tm-more > .tm-weather { margin-block-start: 16px; padding-block-end: 6px; }',
    '.tm-weather { display: flex; align-items: center; flex-wrap: wrap; gap: 6px 12px; font-size: 14px; color: var(--ink-2); }',
    '.tm-strip-title { font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); }',
    '.tm-strip { display: flex; gap: 12px; overflow-x: auto; padding-block: 4px 12px; scrollbar-width: none; }',
    '.tm-strip::-webkit-scrollbar { display: none; }',
    '.tm-tile { flex: none; inline-size: 80px; display: flex; flex-direction: column; align-items: center; text-align: center; text-decoration: none; color: inherit; }',
    '.tm-tile .tm-photo, .tm-row .tm-photo, .tm-big .tm-photo { inline-size: 88px; block-size: 88px; border-radius: var(--radius-sm); overflow: hidden; background: var(--surface-2); display: grid; place-items: center; color: var(--ink-3); }',
    '.tm-tile .tm-photo img, .tm-row .tm-photo img, .tm-big .tm-photo img { inline-size: 100%; block-size: 100%; object-fit: cover; display: block; }',
    /* a print in the gradient ring: 68px in an 80px tile, the ring 2px (the .featured padding-box trick), the photo's own corners inside it */
    '.tm-tile .tm-photo { inline-size: 68px; block-size: 68px; border-radius: 18px; border: 2px solid transparent; background: linear-gradient(var(--surface), var(--surface)) padding-box, var(--grad) border-box; box-shadow: 0 8px 20px rgba(8, 4, 20, 0.45); }',
    '.tm-tile .tm-photo img { border-radius: 14px; }',
    '.tm-tile .tm-name { margin-block-start: 8px; font-size: 12px; line-height: 1.3; font-weight: 500; color: var(--ink-2); unicode-bidi: plaintext; overflow-wrap: anywhere; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }',
    '.tm-tile.tm-more .tm-photo { color: var(--accent); font-weight: 700; font-size: 15px; }',
    '.tm-action { text-align: center; }',
    '.tm-action .hint { margin-block-start: 8px; }',
    /* the outfit: a glass card, no padding of its own (each child carries the 16px gutter, the photo and the chips sit on the
       card's own ground), lit from below in its occasion's tint when the card knows one */
    '.tm-card { padding: 0 0 16px; background: var(--glass); border: 1px solid var(--glass-edge); border-radius: var(--radius); box-shadow: var(--shadow-card); display: grid; gap: 12px; }',
    '.tm-card[data-occasion] { box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.07), 0 22px 60px var(--tint-glow), 0 2px 18px var(--tint-wash); }',
    '.tm-card { animation: rise-in 320ms cubic-bezier(0.2, 0.8, 0.2, 1) both; }',   /* the outfit rises in when it lands (app.css §9's keyframe) */
    '@media (prefers-reduced-motion: reduce) { .tm-card { animation: none; } }',   /* off outright: a collapsed rise-in still paints its first frame at opacity 0 */
    '.tm-card > * { margin-inline: 16px; }',
    '.tm-kicker { font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); display: flex; flex-wrap: wrap; align-items: center; gap: 6px 10px; margin-block-start: 14px; }',
    '.tm-kicker > span[data-occasion] { color: var(--accent-ink); background: var(--tint); padding: 5px 10px; border-radius: var(--pill); box-shadow: 0 4px 14px var(--tint-glow); }',   /* the occasion as its pastel, solid */
    '.tm-row { display: flex; align-items: center; gap: 12px; }',
    '.tm-row .tm-photo { flex: none; inline-size: 96px; block-size: 96px; cursor: pointer; }',
    '.tm-row .tm-body { flex: 1; min-inline-size: 0; }',
    '.tm-row .tm-name { font-weight: 700; font-size: 16px; line-height: 1.3; color: var(--ink); unicode-bidi: plaintext; overflow-wrap: anywhere; }',
    '.tm-row .tm-meta { margin-block-start: 3px; font-size: 13px; color: var(--ink-3); }',
    /* one look: the photo large (168px, 4:5) in its tint's glow with its own vignette, and beside it the column of pieces as
       name chips; on a narrow phone the photo gives way first so the chips keep at least 150px */
    '.tm-big { display: grid; grid-template-columns: minmax(0, 168px) minmax(150px, 1fr); gap: 14px; align-items: start; }',
    '.tm-big .tm-photo { position: relative; inline-size: 100%; block-size: auto; aspect-ratio: 4 / 5; border-radius: 18px; cursor: pointer; box-shadow: 0 14px 34px var(--tint-glow), 0 8px 24px rgba(8, 4, 20, 0.4); }',
    '.tm-big .tm-photo::after { content: ""; position: absolute; inset: 0; pointer-events: none; border-radius: inherit; background: linear-gradient(to top, rgba(20, 16, 30, 0.45), transparent 40%); }',
    '.tm-big .tm-side { display: grid; gap: 10px; min-inline-size: 0; align-content: start; }',
    '.tm-side > .hint { margin: 0; font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); }',
    '[dir="rtl"] .tm-side > .hint { font-size: 12.5px; }',
    '.tm-big .pieces { flex-direction: column; align-items: stretch; gap: 8px; min-block-size: 0; }',
    '.tm-big .pieces li { list-style: none; display: flex; min-inline-size: 0; }',
    '.tm-big .chip.piece { inline-size: 100%; min-block-size: 40px; padding-block: 9px; padding-inline: 12px; flex-wrap: wrap; column-gap: 6px; row-gap: 2px; justify-content: flex-start; line-height: 1.25; white-space: normal; background: var(--surface-2); font-size: 14px; border-color: var(--line-soft); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.05); }',
    '.tm-big .chip.piece .item-name { white-space: normal; overflow: visible; text-overflow: clip; overflow-wrap: anywhere; }',   /* a name wraps here rather than ellipsising: the column is narrow */
    '.tm-big .chip.piece .cat { flex: none; color: var(--ink-3); font-weight: 500; }',
    '.tm-sentence { font-size: 18px; line-height: 1.45; font-weight: 500; color: var(--ink); margin-block: 0; }',
    '[dir="rtl"] .tm-sentence { font-weight: 600; }',
    /* the forecast: the one amber pill, with a glowing sun; drawn once, on the card it was written for */
    '.tm-pill { display: inline-flex; align-items: center; gap: 8px; min-block-size: 36px; padding-inline: 12px; border-radius: var(--pill); background: var(--amber-tint); border: 1px solid rgba(255, 180, 107, 0.3); color: var(--ink); font-size: 14px; font-weight: 600; justify-self: start; }',
    '.tm-pill::before { content: ""; flex: none; inline-size: 10px; block-size: 10px; border-radius: 50%; background: var(--amber); box-shadow: 0 0 10px rgba(255, 180, 107, 0.85); }',
    '.tm-gap { padding: 12px 14px; border-radius: var(--radius-sm); background: var(--accent-tint); display: grid; gap: 8px; }',
    '.tm-gap p { margin: 0; font-size: 14px; line-height: 1.45; color: var(--ink); }',
    '.tm-thumbs { display: flex; gap: 8px; flex-wrap: wrap; }',
    '.tm-thumbs .chip { min-block-size: 44px; }',
    '.tm-thumbs .chip[aria-pressed="true"] { pointer-events: none; }',
    '.tm-links { display: flex; flex-wrap: wrap; gap: 0 16px; align-items: baseline; }',
    '.tm-stale { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 10px 12px; border-radius: var(--radius-sm); background: var(--surface-2); font-size: 14px; color: var(--ink-2); }',
    '.tm-recent { list-style: none; margin: 0; padding: 0; }',
    '.tm-recent li { border-block-end: 1px solid var(--line-soft); }',
    '.tm-recent li:last-child { border-block-end: 0; }',
    '.tm-recent button { display: flex; align-items: center; gap: 10px; inline-size: 100%; min-block-size: 44px; padding-block: 10px; background: none; border: 0; color: var(--ink); font: inherit; text-align: start; cursor: pointer; }',
    '.tm-recent .tm-recent-body { flex: 1; min-inline-size: 0; }',
    '.tm-recent .tm-recent-names { font-size: 14px; color: var(--ink-2); unicode-bidi: plaintext; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }',
    '.tm-skeleton { display: grid; gap: 12px; }',
    '.tm-skeleton div { block-size: 96px; border-radius: var(--radius-sm); background: linear-gradient(90deg, var(--surface-2) 25%, var(--surface) 50%, var(--surface-2) 75%); background-size: 200% 100%; animation: shimmer 1.2s linear infinite; }',
    '.tm-full { inline-size: 100%; border-radius: var(--radius-sm); display: block; }',
    '.tm-check-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }',
    '.tm-check-list label { display: flex; align-items: center; gap: 12px; min-block-size: 44px; font-size: 16px; }',
    '.tm-check-list input { inline-size: 24px; block-size: 24px; accent-color: var(--accent); }'
  ].join('\n') }));
}

// ---------- words ----------

const categoryLabel = (category) => (hasMessage('wardrobe.category_' + category) ? t('wardrobe.category_' + category) : category);
const gapLabel = (category) => (hasMessage('tomorrow.gap_' + category) ? t('tomorrow.gap_' + category) : category);
const skyLabel = (sky) => (hasMessage('tomorrow.sky_' + sky) ? t('tomorrow.sky_' + sky) : sky);
/** The pill: the two temperatures always, the sky and the rain chance only when the service gave them. */
function weatherText(w) {
  const parts = [t('tomorrow.weather_temps', { high: Math.round(w.tempMaxC), low: Math.round(w.tempMinC) })];
  if (w.sky) parts.push(skyLabel(w.sky));
  if (typeof w.precipChance === 'number') parts.push(t('tomorrow.weather_rain', { rain: w.precipChance }));
  return parts.join(' · ');
}
const occasionLabel = (occasion) => (hasMessage('occasion.' + occasion) ? t('occasion.' + occasion) : occasion);
const styleLabel = (style) => (style && hasMessage('style.' + style) ? t('style.' + style) : '');
const whenLabel = (when) => t(when === 'today' ? 'tomorrow.when_today' : 'tomorrow.when_tomorrow');
const plans = () => (state.config && state.config.plans) || {};
const isPro = () => !!state.me && state.me.plan === 'pro';

/** The phone's own calendar date: "tomorrow" is the person's tomorrow, not the server's. */
/** The phone's own calendar day, offset days from today, as yyyy-MM-dd: what "today" and "tomorrow" mean to the person. */
function localDay(offset = 0) {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  const pad = (n) => String(n).padStart(2, '0');
  return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
}
const localToday = () => localDay(0);

// ---------- state that outlives a render ----------

// The question being asked. The style is the check screen's saved preference, as there; the day is remembered too.
const pick = { occasion: null, style: null, when: null, loaded: false };
let page = null;      // GET /api/tomorrow, the last answer
let card = null;      // the outfit on screen
let busy = false;
let root = null;
let ctx = null;
let moreOpen = null;   // the fold (details.tm-more) as the person left it during this visit; null = untouched, the default applies

function weatherPref() {
  const w = loadPrefs().weather;
  return w && typeof w.lat === 'number' && typeof w.lon === 'number' ? w : null;
}

/**
 * The stored outfit that answers the pressed chips, if any: switching a chip never asks the server. The same day as the
 * pressed pill (a plan for last Tuesday is not tomorrow's, whatever its label) and not one the person turned down.
 */
function matching() {
  if (!page || !pick.occasion) return null;
  const day = localDay(pick.when === 'tomorrow' ? 1 : 0);
  return (page.recent || []).find((s) => s.status === 'ok' && s.occasion === pick.occasion && (s.style || null) === (pick.style || null)
    && s.when === pick.when && s.forDate === day && s.useful !== false) || null;
}

function mergeRecent(suggestion) {
  if (!page) return;
  page.recent = [suggestion, ...(page.recent || []).filter((s) => s.id !== suggestion.id)].slice(0, 5);
  for (const key of ['leftToday', 'capToday', 'leftMonth', 'capMonth']) if (typeof suggestion[key] === 'number') page[key] = suggestion[key];
}

// ---------- pieces of the screen ----------

/**
 * A piece's photo, or the bag mark when there is none or the file is gone — never a broken image. The image is in the
 * page from the start: a lazily loaded image that is not in the document never loads at all.
 */
function photoBox(url, name, onOpen) {
  // When the box is a button it carries the name itself, so a screen reader hears what it opens and not "button".
  const box = el('div', { class: 'tm-photo', role: onOpen ? 'button' : undefined, tabindex: onOpen ? '0' : undefined, 'aria-label': onOpen && name ? name : undefined }, [icon('bag')]);
  if (url) {
    const img = el('img', { src: url, alt: onOpen ? '' : (name || ''), loading: 'lazy', decoding: 'async' });
    img.addEventListener('error', () => box.replaceChildren(icon('bag')));
    box.replaceChildren(img);
    if (onOpen) {
      box.addEventListener('click', onOpen);
      box.addEventListener('keydown', (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onOpen(); } });
    }
  }
  return box;
}

/** The full photo in a sheet: "You, 12 Sep, wearing it." */
function openPhoto(url, wornAt) {
  const img = el('img', { class: 'tm-full', src: url, alt: '' });
  sheet({ title: t('tomorrow.photo_caption', { date: wornAt ? fmtDate(wornAt) : '' }), content: el('div', { class: 'stack' }, [img]) });
}

function whenPills() {
  const group = el('div', { class: 'chips tm-when', id: 'when', role: 'group', 'aria-label': t('tomorrow.a11y_when') });
  for (const when of ['today', 'tomorrow']) {
    group.appendChild(el('button', {
      type: 'button', class: 'chip', 'data-when': when, text: whenLabel(when), 'aria-pressed': String(pick.when === when),
      onclick: () => { pick.when = when; savePrefs({ tomorrowWhen: when }); card = matching(); repaint(); }
    }));
  }
  return group;
}

/**
 * The forecast line: the ask, the consent, or the way to forget the place. The forecast an outfit was written for is
 * drawn once, on its card (lookCard), not here as well.
 */
function weatherLine() {
  const line = el('div', { class: 'tm-weather', id: 'tm-weather' });
  const prefs = loadPrefs();
  const pref = weatherPref();
  const forget = () => el('button', { type: 'button', class: 'btn-text', id: 'weather-forget', text: t('tomorrow.weather_forget'), onclick: () => { savePrefs({ weather: null }); repaint(); } });
  if (pref) {
    line.appendChild(el('span', { id: 'weather-ready', text: t('tomorrow.weather_ready') }));
    line.appendChild(forget());
    return line;
  }

  if ((prefs.weatherDenied || 0) >= 2) {
    // "Allow it in settings, then try again" - with the button that is the trying again, so a person who did allow it
    // is not locked out for good; a browser still blocking it says so once more.
    line.appendChild(el('span', { id: 'weather-denied', text: t('tomorrow.weather_denied_again') }));
    line.appendChild(el('button', { type: 'button', class: 'btn-text', id: 'weather-use', text: t('tomorrow.weather_use'), onclick: askForPlace }));
    return line;
  }

  line.appendChild(el('span', { text: t('tomorrow.weather_ask') }));
  line.appendChild(el('button', { type: 'button', class: 'btn-text', id: 'weather-use', text: t('tomorrow.weather_use'), onclick: askForPlace }));
  line.appendChild(el('span', { class: 'hint', style: 'flex-basis: 100%; margin: 0;', text: t('tomorrow.weather_hint') }));
  return line;
}

/** The consent moment: the browser's own prompt, on the tap and never before; the answer rounded before it is kept. */
function askForPlace() {
  if (!navigator.geolocation) { toast(t('tomorrow.weather_denied')); return; }
  navigator.geolocation.getCurrentPosition((position) => {
    const lat = Math.round(position.coords.latitude * 100) / 100;
    const lon = Math.round(position.coords.longitude * 100) / 100;
    savePrefs({ weather: { lat, lon, at: Date.now() }, weatherDenied: 0 });
    repaint();
  }, (error) => {
    // A refusal is remembered: an iPhone's home-screen app cannot ask again from the page, and the second time the line
    // says where to allow it instead of offering a button that would do nothing.
    if (error && error.code === 1) savePrefs({ weatherDenied: (loadPrefs().weatherDenied || 0) + 1 });
    toast(t('tomorrow.weather_denied'));
    repaint();
  }, { timeout: 8000, maximumAge: 1800000 });
}

/** Whether the screen is at the point of composing (or has composed): the notices and the empty state come before it. */
function ready() {
  return !!page && !page.needsPro && page.stylistOn && page.available && page.have >= page.needs && page.haveKinds >= page.needsKinds;
}

/**
 * The fold: the style row, the forecast line and the place behind one line that says "More" (and the style picked, so
 * it is known without opening). It starts open while there is an outfit to compose and the forecast is still a question
 * (no place kept, not refused twice), so the ask is read once; before there are pieces, and once the question is
 * answered, it starts folded. A repaint keeps whatever state the person left it in.
 */
function moreFold() {
  const open = moreOpen === null ? (ready() && !weatherPref() && (loadPrefs().weatherDenied || 0) < 2) : moreOpen;
  return el('details', { class: 'tm-more', id: 'tm-more', open }, [
    el('summary', {}, [
      el('span', { text: t('common.more') }),
      pick.style ? el('span', { class: 'hint', text: styleLabel(pick.style) }) : null
    ]),
    styleChips(pick, () => { card = matching(); repaint(); }),
    weatherLine()
  ]);
}

/** The occasion row for this screen: no heading of its own (the group is named for a reader), one row that scrolls. */
function occasionRow() {
  const block = occasionChips(pick, () => { card = matching(); repaint(); });
  const heading = block.querySelector('h2');
  if (heading) heading.classList.add('sr-only');
  const row = block.querySelector('#occasions');
  if (row) row.classList.add('scroll');
  return block;
}

/** The kit's empty state under the mark, large and lit (app.css .empty-mark): the one warm picture on a screen with nothing on it yet. */
function emptyWithMark(title, body) {
  const empty = emptyState(title, body);
  const mark = logoMark(136);
  if (mark) empty.prepend(el('span', { class: 'empty-mark', 'aria-hidden': 'true' }, [mark]));
  return empty;
}

/** The strip: the person's own closet as photos, before anything is spent. */
function strip() {
  if (!page || !page.strip || page.strip.length === 0) return null;
  const block = el('div', { class: 'stack', style: 'gap: 8px;' });
  block.appendChild(el('span', { class: 'tm-strip-title', id: 'strip-title', text: t('tomorrow.strip_title', { n: page.have }) }));
  const row = el('div', { class: 'tm-strip', id: 'strip' });
  for (const piece of page.strip) {
    row.appendChild(el('div', { class: 'tm-tile', 'data-item': piece.itemId }, [
      photoBox(piece.photoUrl, piece.name),
      el('div', { class: 'tm-name', dir: 'auto', text: piece.name })
    ]));
  }
  if (page.have > page.strip.length) {
    row.appendChild(el('a', { class: 'tm-tile tm-more', href: '#/wardrobe' }, [
      el('div', { class: 'tm-photo', text: t('tomorrow.strip_more', { n: page.have - page.strip.length }) }),
      el('div', { class: 'tm-name', text: t('wardrobe.title') })
    ]));
  }
  block.appendChild(row);
  return block;
}

function leftLine() {
  if (!page) return null;
  return el('p', { class: 'hint', id: 'tm-left', text: page.capMonth > 0
    ? t('tomorrow.left', { n: page.leftToday, m: page.leftMonth })
    : t('tomorrow.left_day', { n: page.leftToday }) });
}

/** The one thing to do: the button, or the honest card that says why not (and never a dead button). */
function actionBlock() {
  const block = el('div', { class: 'tm-action', id: 'tm-action' });
  if (!page) return block;
  if (page.needsPro) {
    block.appendChild(el('div', { class: 'notice', id: 'tm-pro' }, [
      el('p', { text: t('tomorrow.pro_only') }),
      el('a', { class: 'btn-text', id: 'tm-pro-link', href: '#/pro', text: t('tomorrow.pro_go') })
    ]));
    return block;
  }
  if (!page.stylistOn) {
    block.appendChild(el('div', { class: 'notice', id: 'tm-stylist-off' }, [
      el('p', { text: t('tomorrow.stylist_off') }),
      el('a', { class: 'btn-text', href: '#/wardrobe', text: t('tomorrow.stylist_off_go') })
    ]));
    return block;
  }
  if (page.have < page.needs || page.haveKinds < page.needsKinds) {
    block.appendChild(emptyWithMark(t('tomorrow.needs_title'), page.have === 0 ? t('tomorrow.needs_zero') : t('tomorrow.needs', { n: page.have })));
    block.appendChild(el('a', { class: 'btn', id: 'tm-needs-go', href: '#/check', text: t('tomorrow.needs_go') }));
    block.lastChild.id = 'tm-needs-go';
    return block;
  }
  if (!page.available) {
    block.appendChild(emptyWithMark(t('tomorrow.off_title'), t('tomorrow.off')));
    return block;
  }
  if (busy) {
    block.appendChild(el('div', { class: 'tm-skeleton', id: 'tm-skeleton', 'aria-busy': 'true' }, [el('div'), el('div'), el('div')]));
    block.appendChild(el('p', { class: 'hint', role: 'status', text: t('tomorrow.composing') }));
    return block;
  }
  if (card) return block;   // the card below is the answer; "another idea" lives on it
  if (page.leftToday <= 0) {
    block.appendChild(el('p', { class: 'hint', id: 'tm-none-left', text: t('tomorrow.none_left') }));
    if (!isPro()) block.appendChild(el('a', { class: 'btn-text', href: '#/pro', text: t('check.go_pro') }));
    return block;
  }
  block.appendChild(el('button', { type: 'button', class: 'btn', id: 'tm-compose', text: t('tomorrow.compose'), disabled: !pick.occasion, onclick: () => compose(false) }));
  block.appendChild(leftLine());
  return block;
}

/** One row of the look: the photo of them wearing the piece, the name, the kind, how often it was worn. */
function pieceRow(piece) {
  return el('div', { class: 'tm-row', 'data-item': piece.itemId || '' }, [
    photoBox(piece.photoUrl, piece.name, piece.photoUrl ? () => openPhoto(piece.photoUrl, piece.photoWornAt) : null),
    el('div', { class: 'tm-body' }, [
      el('div', { class: 'tm-name', dir: 'auto', text: piece.name }),
      el('div', { class: 'tm-meta', text: categoryLabel(piece.category) + (piece.worn > 0 ? ' · ' + t('tomorrow.worn', { n: piece.worn }) : '') + (piece.lastWornAt ? ' · ' + relative(piece.lastWornAt) : '') })
    ])
  ]);
}

/** The thumbs: yes, or no with a reason; after an answer the row stays lit and says so. */
function thumbsRow(suggestion) {
  const row = el('div', { class: 'tm-thumbs', id: 'tm-thumbs' });
  const answered = suggestion.usefulReason || null;
  const chip = (id, text, pressed, onclick) => el('button', { type: 'button', class: 'chip', id, text, 'aria-pressed': String(pressed), onclick });
  row.appendChild(chip('tm-yes', t('tomorrow.yes'), answered === 'worked', () => answer(suggestion, 'worked')));
  row.appendChild(chip('tm-no', t('tomorrow.no'), !!answered && answered !== 'worked', () => openNoSheet(suggestion)));
  if (answered) row.appendChild(el('span', { class: 'hint', style: 'align-self: center; margin: 0;', text: t('tomorrow.answered') }));
  return row;
}

async function answer(suggestion, reason, note) {
  try {
    const dto = await api('POST', '/api/tomorrow/' + encodeURIComponent(suggestion.id) + '/useful', { reason, note: note || null });
    suggestion.useful = dto.useful;
    suggestion.usefulReason = dto.reason;
    if (card && card.id === suggestion.id) card = suggestion;
    repaint();
  } catch (e) {
    toast((e && e.message) || t('error.generic'));
  }
}

/** "Not this": three reasons; "I don't have one of these any more" also corrects the wardrobe at the source. */
function openNoSheet(suggestion) {
  const list = el('div', { class: 'sheet-list' });
  const reason = (id, key, onclick) => list.appendChild(el('button', { type: 'button', id, onclick }, [t(key)]));
  reason('tm-reason-didnt-work', 'tomorrow.reason_didnt_work', () => { closeSheet(); answer(suggestion, 'didnt_work'); });
  reason('tm-reason-not-my-style', 'tomorrow.reason_not_my_style', () => { closeSheet(); answer(suggestion, 'not_my_style'); });
  reason('tm-reason-dont-own', 'tomorrow.reason_dont_own', () => openDontOwnSheet(suggestion));
  sheet({ title: t('tomorrow.no_title'), content: list });
}

function openDontOwnSheet(suggestion) {
  const boxes = (suggestion.pieces || []).filter((p) => p.itemId).map((p) => {
    const input = el('input', { type: 'checkbox', 'data-item': p.itemId });
    return { input, piece: p, label: el('label', {}, [input, el('span', { dir: 'auto', text: p.name })]) };
  });
  const remove = el('button', { type: 'button', class: 'btn btn-danger', id: 'tm-dont-own-remove', text: t('tomorrow.dont_own_remove'), onclick: async () => {
    remove.disabled = true;
    try {
      for (const box of boxes) {
        if (box.input.checked) await api('DELETE', '/api/wardrobe/' + encodeURIComponent(box.piece.itemId));
      }
      forgetWardrobe();
      closeSheet();
      await answer(suggestion, 'dont_own');
      await load();
      repaint();
    } catch (e) {
      toast((e && e.message) || t('error.generic'));
      remove.disabled = false;
    }
  } });
  sheet({ title: t('tomorrow.dont_own_which'), content: el('div', { class: 'stack' }, [
    el('ul', { class: 'tm-check-list' }, boxes.map((b) => el('li', {}, [b.label]))),
    remove
  ]) });
}

/**
 * The look card: the pieces as photos of you, the sentence, the forecast, the gap, the thumbs, the two doors. The card
 * and the kicker's occasion carry data-occasion, which app.css maps to the occasion's tint (the glow, the pill).
 */
function lookCard(suggestion) {
  const kicker = el('div', { class: 'tm-kicker' }, [
    el('span', { text: whenLabel(suggestion.when) }),
    el('span', { 'data-occasion': suggestion.occasion || null, text: occasionLabel(suggestion.occasion) }),
    suggestion.style ? el('span', { text: styleLabel(suggestion.style) }) : null,
    suggestion.seq > 1 ? el('span', { id: 'tm-idea', text: t('tomorrow.idea', { n: suggestion.seq }) }) : null
  ]);
  const cardEl = el('article', { class: 'tm-card', id: 'tm-card', 'data-suggestion': suggestion.id, 'data-occasion': suggestion.occasion || null }, [kicker]);
  if (suggestion.status === 'rejected') {
    cardEl.appendChild(el('p', { class: 'tm-sentence', text: t('tomorrow.rejected') }));
    return cardEl;
  }
  if (suggestion.stale) {
    cardEl.appendChild(el('div', { class: 'tm-stale', id: 'tm-stale' }, [
      el('span', { text: t('tomorrow.stale') }),
      el('button', { type: 'button', class: 'btn btn-sm btn-secondary', id: 'tm-refresh', text: t('tomorrow.refresh'), disabled: page && page.leftToday <= 0, onclick: () => compose(true) })
    ]));
  }

  const pieces = suggestion.pieces || [];
  const photoIds = pieces.map((p) => p.photoCheckId).filter(Boolean);
  const oneLook = pieces.length > 1 && photoIds.length === pieces.length && photoIds.every((id) => id === photoIds[0]);
  if (oneLook) {
    // Every piece came from one check: the photo once, large, and beside it the pieces as name chips (the name, then
    // its kind) - never the same photo three times. Each chip's li keeps data-item, as the wrapped list before it did.
    const first = pieces[0];
    cardEl.appendChild(el('div', { class: 'tm-big', id: 'tm-one-look' }, [
      photoBox(first.photoUrl, t('tomorrow.photo_caption', { date: first.photoWornAt ? fmtDate(first.photoWornAt) : '' }), () => openPhoto(first.photoUrl, first.photoWornAt)),
      el('div', { class: 'tm-side' }, [
        el('span', { class: 'hint', text: t('tomorrow.same_photo') }),
        el('ul', { class: 'pieces chips' }, pieces.map((p) => el('li', { dir: 'auto', 'data-item': p.itemId || '' }, [
          el('span', { class: 'chip piece' }, [
            el('span', { class: 'item-name', text: p.name }),
            el('span', { class: 'cat', text: '· ' + categoryLabel(p.category) })
          ])
        ])))
      ])
    ]));
  } else {
    for (const piece of pieces) cardEl.appendChild(pieceRow(piece));
  }

  cardEl.appendChild(el('p', { class: 'tm-sentence', id: 'tm-sentence', dir: 'auto', text: suggestion.sentence }));
  if (suggestion.weather) {
    cardEl.appendChild(el('span', { class: 'tm-pill', id: 'weather-pill', text: weatherText(suggestion.weather) }));
  } else {
    cardEl.appendChild(el('span', { class: 'hint', id: 'tm-weather-none', text: t('tomorrow.weather_none') }));
  }
  if (suggestion.gap) {
    cardEl.appendChild(el('div', { class: 'tm-gap', id: 'tm-gap' }, [
      el('p', { text: t('tomorrow.gap', { category: gapLabel(suggestion.gap) }) }),
      el('a', { class: 'btn-text', id: 'tm-gap-go', href: '#/check', text: t('tomorrow.gap_go') })
    ]));
  }
  cardEl.appendChild(thumbsRow(suggestion));
  const links = el('div', { class: 'tm-links' });
  if (page && page.leftToday > 0 && page.available && !page.needsPro) {
    links.appendChild(el('button', { type: 'button', class: 'btn-text', id: 'tm-another', text: t('tomorrow.another'), onclick: () => compose(true) }));
    links.appendChild(el('span', { class: 'hint', style: 'margin: 0;', text: t('tomorrow.another_cost') }));
  }
  links.appendChild(el('a', { class: 'btn-text', id: 'tm-check-it', href: '#/check?suggestion=' + encodeURIComponent(suggestion.id), onclick: () => {
    // The check screen pre-lights this outfit's chips and sends the id with the photo: the loop closes on the server.
    state.check.suggestion = { id: suggestion.id, occasion: suggestion.occasion, style: suggestion.style || null };
  } }, [t('tomorrow.check_it')]));
  cardEl.appendChild(links);
  if (page && page.leftToday > 0) cardEl.appendChild(leftLine());
  return cardEl;
}

/** The other recent outfits, one line each; a tap paints one as the card and lights its chips. */
function recentList() {
  if (!page) return null;
  const others = (page.recent || []).filter((s) => !card || s.id !== card.id);
  if (others.length === 0) return null;
  return el('div', { class: 'stack', style: 'gap: 6px;' }, [
    el('span', { class: 'tm-strip-title', text: t('tomorrow.recent_title') }),
    el('ul', { class: 'tm-recent', id: 'tm-recent' }, others.map((s) => el('li', {}, [
      el('button', { type: 'button', 'data-suggestion': s.id, onclick: () => {
        pick.occasion = s.occasion; pick.style = s.style || null; pick.when = s.when; card = s; repaint();
      } }, [
        icon(s.usefulReason === 'worked' ? 'check' : 'calendar'),
        el('div', { class: 'tm-recent-body' }, [
          el('div', { text: occasionLabel(s.occasion) + ' · ' + whenLabel(s.when) + ' · ' + relative(s.createdAt) }),
          el('div', { class: 'tm-recent-names', text: (s.pieces || []).slice(0, 2).map((p) => p.name).join(' + ') })
        ])
      ])
    ])))
  ]);
}

// ---------- the calls ----------

// Round 20: a tap on the morning push arrives as #/tomorrow?from=push; the read carries it once so the server can stamp
// the push as opened (nothing else about the answer changes), and the query is then dropped from the address so a refresh
// or a back-swipe does not send it again (the server counts each push once anyway).
async function load(from) {
  page = await api('GET', '/api/tomorrow' + (from === 'push' ? '?from=push' : ''));
  return page;
}

function fromQuery() {
  const q = location.hash.indexOf('?');
  return q < 0 ? null : new URLSearchParams(location.hash.slice(q + 1)).get('from');
}

async function compose(fresh) {
  if (busy || !pick.occasion || !page) return;
  busy = true;
  repaint();
  const place = weatherPref();
  try {
    const answer = await api('POST', '/api/tomorrow', {
      occasion: pick.occasion, style: pick.style, when: pick.when, today: localToday(), fresh,
      lat: place ? place.lat : null, lon: place ? place.lon : null
    });
    if (answer && answer.id) {
      card = answer;
      mergeRecent(answer);
    } else if (answer && typeof answer.needs === 'number') {
      page.have = answer.have; page.haveKinds = answer.haveKinds; page.needs = answer.needs; page.needsKinds = answer.needsKinds;
      card = null;
    }
  } catch (e) {
    toast((e && e.message) || t('error.generic'));
    // A refusal that is about the day, the month or the money: the numbers on screen are re-read so the line is true.
    if (e && (e.status === 429 || e.status === 503 || e.status === 409 || e.status === 403)) { try { await load(); } catch (ignored) { /* the toast said it */ } }
  } finally {
    busy = false;
    repaint();
  }
}

// ---------- the screen ----------

function repaint() {
  if (!root || !ctx || ctx.stale()) return;
  const body = $('tomorrow') || root;
  const fold = $('tm-more');
  if (fold) moreOpen = fold.open;   // a repaint keeps the fold as the person left it
  body.replaceChildren();
  body.appendChild(el('p', { class: 'lede tm-lede sr-only', text: t('tomorrow.lede') }));
  body.appendChild(whenPills());
  body.appendChild(occasionRow());
  body.appendChild(moreFold());
  const stripBlock = strip();
  if (stripBlock) body.appendChild(stripBlock);
  body.appendChild(actionBlock());
  if (card && !busy) body.appendChild(lookCard(card));
  const recent = recentList();
  if (recent) body.appendChild(recent);
}

register('tomorrow', async (r, params, c) => {
  ensureStyle();
  root = r;
  ctx = c;
  setTopBar({ back: '#/me', title: t('tomorrow.title') });
  // The heading a screen reader lands on (the top bar carries the visible title), as the check screen has.
  root.appendChild(el('h1', { class: 'sr-only', text: t('tomorrow.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (plans().tomorrow === false) { root.appendChild(emptyWithMark(t('tomorrow.off_title'), t('tomorrow.off'))); return; }

  moreOpen = null;   // a fresh visit starts the fold from its default
  const body = el('div', { class: 'stack', id: 'tomorrow' });
  root.appendChild(body);
  body.appendChild(el('p', { class: 'lede tm-lede sr-only', text: t('tomorrow.lede') }));
  body.appendChild(el('div', { class: 'tm-skeleton', 'aria-busy': 'true' }, [el('div')]));

  if (!pick.loaded) {
    const prefs = loadPrefs();
    pick.style = STYLES.includes(prefs.style) ? prefs.style : null;
    pick.when = prefs.tomorrowWhen === 'today' || prefs.tomorrowWhen === 'tomorrow' ? prefs.tomorrowWhen : (new Date().getHours() >= 15 ? 'tomorrow' : 'today');
    pick.loaded = true;
  }

  const from = fromQuery();
  if (from) history.replaceState(history.state, '', location.pathname + location.search + '#/tomorrow');
  try {
    await load(from);
  } catch (e) {
    if (ctx.stale()) return;
    body.replaceChildren(errorBlock(e));
    return;
  }
  if (ctx.stale()) return;
  if (!pick.occasion) pick.occasion = OCCASIONS.includes(page.defaultOccasion) ? page.defaultOccasion : 'Everyday';
  if (page.defaultStyle && STYLES.includes(page.defaultStyle) && !loadPrefs().style) pick.style = page.defaultStyle;
  card = matching();
  repaint();
});
