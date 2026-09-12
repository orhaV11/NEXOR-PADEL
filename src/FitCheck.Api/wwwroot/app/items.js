// Items on a look, the editor half (Round 10): the tagging widget the post sheet (views/check.js) and the look page's
// "Edit items" sheet (views/post.js) share. itemsEditor(source, photo) → { node, value() }: node goes into a sheet, value()
// is the PostItemInput[] the server takes (POST /api/posts { items } and PATCH /api/posts/{id}/items { items }).
//
// source is either the check being posted (CheckDto: feedback.items with name, category and brandSeen from rubric v3) or
// { items: PostItemDto[] } for a look that already has rows (their ids ride along so the server keeps their source).
// photo is the still's URL (or an <img>): the preview the dots are placed on; without one the list works and nothing is placed.
//
// The rules the plan holds the client to: the stylist's brand guess is only ever a suggestion. A chip carrying one shows
// "Looks like Nike? Confirm / Edit / Not a brand" and value() never sends the guess as brand until the person confirmed it
// (or typed one). At most MAX_ITEMS rows; names ≤ 40, brand ≤ 40, model ≤ 60, url ≤ 500 (PostItems' constants); x and y
// are fractions of the 4:5 photo box (the same box a card shows), both or neither.
//
// Hooks for the browser test: #items-editor, #items-photo (the box; .items-photo-tap is the tap target, .item-dot[data-key]
// the dots), #items-list > li.items-row[data-source=Stylist|User] with .items-main (the row), .items-place (the pill),
// .items-remove, .items-suggest[data-brand] with button[data-action=confirm|edit|dismiss], .items-form (the fields, one open
// at a time) and #items-add.
import { t, api, el, icon, fmtNumber, brandMark, reducedMotion } from './core.js';

export const CATEGORIES = ['top', 'bottom', 'dress', 'outerwear', 'shoes', 'accessory', 'other'];
export const MAX_ITEMS = 12;
/** PostItems.MaxPerPost: the stylist names a handful of pieces; the rest of the twelve are the person's own. */
const MAX_STYLIST = 8;
const NAME_MAX = 40;
const BRAND_MAX = 40;
const MODEL_MAX = 60;
const URL_MAX = 500;
const BRANDS_DEBOUNCE_MS = 220;
const BRANDS_SHOWN = 6;
const NUDGE = 0.02;   // an arrow key moves a dot by this much of the box

export const categoryOf = (c) => (CATEGORIES.includes(c) ? c : 'other');
export const categoryLabel = (c) => t('items.cat_' + categoryOf(c));
export const hasDot = (item) => !!item && typeof item.x === 'number' && typeof item.y === 'number' && isFinite(item.x) && isFinite(item.y);
const clean = (s, max) => (typeof s === 'string' ? s.trim().slice(0, max) : '');
const clamp01 = (v) => Math.min(1, Math.max(0, v));
const round3 = (v) => Math.round(v * 1000) / 1000;
const pct = (v) => (round3(v) * 100).toFixed(1) + '%';

/** The host a store link shows ("Shop at nike.com"): the DTO's host when the server gave one, else read from the url; www. dropped. */
export function hostOf(item) {
  if (item && item.host) return item.host;
  try { return new URL(item.url).host.replace(/^www\./i, ''); } catch (e) { return ''; }
}

/** "running shoes · Nike · Air Max 90" as nodes: the name in bold, the brand and the model after it. The look page and the editor share it. */
export function itemLine(item, fallbackName) {
  const name = clean(item.name, NAME_MAX) || fallbackName || t('items.unnamed');
  const parts = [el('b', { text: name })];
  for (const extra of [clean(item.brand, BRAND_MAX), clean(item.model, MODEL_MAX)]) {
    if (extra) parts.push(' · ', el('span', { text: extra }));
  }
  return parts;
}

let seq = 0;
function row(fields) {
  return {
    key: ++seq, id: null, name: '', category: 'other', brand: '', model: '', url: '', x: null, y: null, confirmed: false, source: 'User',
    suggested: null,   // the stylist's brand guess, kept so a typed brand that matches it counts as confirmed
    guess: null,       // the guess while it waits for Confirm / Edit / Not a brand
    ...fields
  };
}

/** The stylist's pieces from the check, as the server will store them: distinct by name, at most MAX_STYLIST, no brand (the guess waits). */
function fromCheck(result) {
  const items = result && result.feedback && Array.isArray(result.feedback.items) ? result.feedback.items : [];
  const seen = new Set();
  const rows = [];
  for (const item of items) {
    const name = clean(item && item.name, NAME_MAX);
    const key = name.toLowerCase();
    if (!name || seen.has(key)) continue;
    seen.add(key);
    const guess = clean(item.brandSeen, BRAND_MAX) || null;
    rows.push(row({ name, category: categoryOf(item.category), source: 'Stylist', suggested: guess, guess }));
    if (rows.length >= MAX_STYLIST) break;
  }
  return rows;
}

/** A look's rows as the server holds them; their ids go back so the server keeps each row's source. */
function fromPost(items) {
  return (Array.isArray(items) ? items : []).slice(0, MAX_ITEMS).map((item) => row({
    id: item.id || null,
    name: clean(item.name, NAME_MAX),
    category: categoryOf(item.category),
    brand: clean(item.brand, BRAND_MAX),
    model: clean(item.model, MODEL_MAX),
    url: clean(item.url, URL_MAX),
    x: hasDot(item) ? clamp01(item.x) : null,
    y: hasDot(item) ? clamp01(item.y) : null,
    confirmed: !!item.confirmed,
    source: item.source === 'Stylist' ? 'Stylist' : 'User'
  }));
}

/**
 * itemsEditor(source, photo) → { node, value() }. See the file comment for source and photo. value() is the list in order,
 * rows without a name left out, the pending brand guess never sent.
 */
export function itemsEditor(source, photo) {
  const rows = source && Array.isArray(source.items) ? fromPost(source.items) : fromCheck(source);
  let placing = null;    // the row whose dot the next tap on the photo places
  let expanded = null;   // the row whose fields are open

  const node = el('div', { class: 'field items-editor', id: 'items-editor' });
  node.appendChild(el('span', { class: 'label', text: t('items.editor_title') }));
  node.appendChild(el('span', { class: 'hint', text: t('items.editor_hint') }));

  // ---- the photo box: the still with the dots over it; a tap places the row being placed (or the open one) ----
  const src = typeof photo === 'string' ? photo : photo && (photo.currentSrc || photo.src) ? (photo.currentSrc || photo.src) : null;
  let box = null; let dots = null; let photoHint = null;
  if (src) {
    dots = el('div', { class: 'item-dots' });
    const tap = el('button', { type: 'button', class: 'items-photo-tap', 'aria-label': t('items.place'), onclick: onPhotoTap }, [
      el('img', { src, alt: '', decoding: 'async', draggable: 'false' })
    ]);
    photoHint = el('p', { class: 'hint items-photo-hint', 'aria-live': 'polite' });
    box = el('div', { class: 'items-photo', id: 'items-photo' }, [tap, dots]);
    node.appendChild(box);
    node.appendChild(photoHint);
  }

  const list = el('ul', { class: 'items-list', id: 'items-list' });
  node.appendChild(list);
  const add = el('button', { type: 'button', class: 'pill items-add', id: 'items-add', onclick: addItem }, [icon('tag'), t('items.add')]);
  node.appendChild(el('div', { class: 'items-foot' }, [add, el('span', { class: 'hint', text: t('items.max', { n: fmtNumber(MAX_ITEMS) }) })]));

  const displayName = (r) => clean(r.name, NAME_MAX) || t('items.unnamed');
  const number = (r) => rows.indexOf(r) + 1;
  const matchesGuess = (r) => !!r.suggested && clean(r.brand, BRAND_MAX).toLowerCase() === r.suggested.toLowerCase();

  function setDot(r, x, y) {
    r.x = round3(clamp01(x));
    r.y = round3(clamp01(y));
  }

  function onPhotoTap(event) {
    const target = placing || expanded;
    if (!target || !box) return;
    const rect = event.currentTarget.getBoundingClientRect();
    // A keyboard "click" (Enter or Space) has no point: the dot lands in the middle and the arrow keys take it from there.
    const keyboard = event.detail === 0 || !rect.width || !rect.height;
    setDot(target, keyboard ? 0.5 : (event.clientX - rect.left) / rect.width, keyboard ? 0.5 : (event.clientY - rect.top) / rect.height);
    placing = null;
    refresh();
    const dot = dots.querySelector('.item-dot[data-key="' + target.key + '"]');
    if (dot && keyboard) dot.focus({ preventScroll: true });
  }

  function dotButton(r) {
    const n = number(r);
    const b = el('button', {
      type: 'button', class: 'item-dot' + (r === expanded || r === placing ? ' active' : ''), 'data-key': String(r.key),
      style: 'left:' + pct(r.x) + ';top:' + pct(r.y), 'aria-label': t('items.dot_label', { n: fmtNumber(n), name: displayName(r) })
    }, [el('span', { text: fmtNumber(n) })]);
    // Drag to move: the pointer is captured, the dot follows within the box, and the click that follows a drag is swallowed
    // so it does not also open the row. A plain tap (no movement) opens the row's fields.
    let drag = null; let swallowClick = false;
    b.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      drag = { id: e.pointerId, moved: false, sx: e.clientX, sy: e.clientY };
      try { b.setPointerCapture(e.pointerId); } catch (err) { /* a synthetic pointer */ }
      e.preventDefault();
    });
    b.addEventListener('pointermove', (e) => {
      if (!drag || e.pointerId !== drag.id) return;
      if (!drag.moved && Math.hypot(e.clientX - drag.sx, e.clientY - drag.sy) < 4) return;
      drag.moved = true;
      const rect = box.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      setDot(r, (e.clientX - rect.left) / rect.width, (e.clientY - rect.top) / rect.height);
      b.style.left = pct(r.x); b.style.top = pct(r.y);
    });
    const end = (e) => {
      if (!drag || e.pointerId !== drag.id) return;
      swallowClick = drag.moved;
      drag = null;
      if (swallowClick) syncRow(r);
    };
    b.addEventListener('pointerup', end);
    b.addEventListener('pointercancel', end);
    b.addEventListener('click', (e) => {
      e.preventDefault();
      if (swallowClick) { swallowClick = false; return; }
      expand(r, true);
    });
    b.addEventListener('keydown', (e) => {
      const delta = { ArrowLeft: [-NUDGE, 0], ArrowRight: [NUDGE, 0], ArrowUp: [0, -NUDGE], ArrowDown: [0, NUDGE] }[e.key];
      if (!delta) return;
      e.preventDefault();
      setDot(r, r.x + delta[0], r.y + delta[1]);
      b.style.left = pct(r.x); b.style.top = pct(r.y);
    });
    return b;
  }

  function syncDots() {
    if (!dots) return;
    dots.replaceChildren(...rows.filter(hasDot).map(dotButton));
    box.classList.toggle('placing', !!placing);
    photoHint.textContent = placing ? t('items.place_hint', { name: displayName(placing) }) : '';
  }

  // ---- rows ----

  function input(type, id, attrs) {
    return el('input', { type, id, autocomplete: 'off', spellcheck: 'false', ...attrs });
  }
  const field = (id, label, control, hint) => el('div', { class: 'field' }, [el('label', { for: id, text: label }), control, hint ? el('span', { class: 'hint', text: hint }) : null]);

  function buildForm(r) {
    const base = 'item-' + r.key + '-';
    const name = input('text', base + 'name', { maxlength: String(NAME_MAX), placeholder: t('items.name_placeholder'), value: r.name, enterkeyhint: 'next',
      oninput: (e) => { r.name = e.target.value; syncRow(r); syncDots(); } });
    const cats = el('div', { class: 'chips scroll', role: 'group', 'aria-label': t('items.category') }, CATEGORIES.map((c) => el('button', {
      type: 'button', class: 'chip', 'data-category': c, 'aria-pressed': String(r.category === c), text: categoryLabel(c),
      onclick: () => { r.category = c; for (const chip of cats.children) chip.setAttribute('aria-pressed', String(chip.dataset.category === c)); }
    })));
    const brands = el('ul', { class: 'items-brands', role: 'listbox', 'aria-label': t('items.brand'), hidden: true });
    const brand = input('text', base + 'brand', { maxlength: String(BRAND_MAX), placeholder: t('items.brand_placeholder'), value: r.brand, autocapitalize: 'words', enterkeyhint: 'next', 'aria-autocomplete': 'list',
      oninput: (e) => { r.brand = e.target.value; r.confirmed = matchesGuess(r); syncRow(r); suggestBrands(e.target.value); } });
    brand.addEventListener('blur', () => setTimeout(() => { brands.hidden = true; }, 150));
    brand.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !brands.hidden) { e.stopPropagation(); brands.hidden = true; } });
    brands.addEventListener('pointerdown', (e) => e.preventDefault());   // keep the focus in the field: the option's click lands first
    const model = input('text', base + 'model', { maxlength: String(MODEL_MAX), placeholder: t('items.model_placeholder'), value: r.model, enterkeyhint: 'next',
      oninput: (e) => { r.model = e.target.value; syncRow(r); } });
    const url = input('url', base + 'url', { maxlength: String(URL_MAX), inputmode: 'url', autocapitalize: 'off', placeholder: t('items.link_placeholder'), value: r.url, enterkeyhint: 'done',
      oninput: (e) => { r.url = e.target.value; } });
    const unplace = el('button', { type: 'button', class: 'btn-text items-unplace', text: t('items.unplace'), onclick: () => { r.x = null; r.y = null; refresh(); } });

    let timer = 0; let reqSeq = 0;
    function suggestBrands(q) {
      clearTimeout(timer);
      q = q.trim();
      if (!q) { brands.hidden = true; return; }
      timer = setTimeout(async () => {
        const mine = ++reqSeq;
        let data = null;
        try { data = await api('GET', '/api/items/brands?q=' + encodeURIComponent(q)); } catch (e) { data = null; }   // 501 until the route lands, or offline: no list
        if (mine !== reqSeq || !document.contains(brands)) return;
        const found = ((data && data.items) || []).filter((b) => b && clean(b.name, BRAND_MAX)).slice(0, BRANDS_SHOWN);
        brands.replaceChildren(...found.map((b) => el('li', { role: 'option' }, [
          el('button', { type: 'button', onclick: () => { r.brand = clean(b.name, BRAND_MAX); brand.value = r.brand; r.confirmed = matchesGuess(r); brands.hidden = true; syncRow(r); } }, [
            el('span', { class: 'name' }, [b.name, b.account ? brandMark(b.account) : null]),
            el('span', { class: 'n', text: t('items.looks', { n: b.looks === 1 ? 1 : fmtNumber(b.looks || 0) }) })
          ])
        ])));
        brands.hidden = !found.length;
      }, BRANDS_DEBOUNCE_MS);
    }

    r.parts.name = name; r.parts.brand = brand; r.parts.unplace = unplace;
    return el('div', { class: 'items-form', hidden: true }, [
      field(base + 'name', t('items.name'), name),
      el('div', { class: 'field' }, [el('span', { class: 'label', text: t('items.category') }), cats]),
      el('div', { class: 'field items-brand' }, [el('label', { for: base + 'brand', text: t('items.brand') }), brand, brands]),
      field(base + 'model', t('items.model'), model),
      field(base + 'url', t('items.link'), url, t('items.link_hint')),
      unplace
    ]);
  }

  function buildRow(r) {
    r.parts = {};
    const num = el('span', { class: 'items-num', 'aria-hidden': 'true' });
    const txt = el('span', { class: 'txt' });
    const main = el('button', { type: 'button', class: 'items-main', 'aria-expanded': 'false', onclick: () => expand(r, expanded !== r) }, [num, txt]);
    const place = box ? el('button', { type: 'button', class: 'pill items-place', 'aria-pressed': 'false', onclick: () => startPlacing(r) }) : null;
    const remove = el('button', { type: 'button', class: 'icon-btn items-remove', 'aria-label': t('items.remove'), onclick: () => removeRow(r) }, [icon('x')]);
    const suggest = el('div', { class: 'items-suggest', hidden: true });
    const form = buildForm(r);
    Object.assign(r.parts, { num, txt, main, place, suggest, form });
    r.node = el('li', { class: 'items-row', 'data-key': String(r.key), 'data-source': r.source }, [
      el('div', { class: 'items-row-head' }, [main, place, remove]),
      suggest,
      form
    ]);
    return r.node;
  }

  function syncRow(r) {
    const p = r.parts;
    const n = number(r);
    p.num.textContent = fmtNumber(n);
    p.txt.replaceChildren(...itemLine(r));
    if (r.source === 'Stylist') p.txt.appendChild(el('span', { class: 'items-src', title: t('items.by_stylist'), 'aria-label': t('items.by_stylist'), icon: 'sparkle' }));
    p.txt.classList.toggle('empty', !clean(r.name, NAME_MAX));
    p.main.setAttribute('aria-expanded', String(expanded === r));
    p.form.hidden = expanded !== r;
    r.node.classList.toggle('open', expanded === r);
    r.node.classList.toggle('placed', hasDot(r));
    if (p.place) {
      p.place.textContent = t(hasDot(r) ? 'items.placed' : 'items.place');
      p.place.setAttribute('aria-pressed', String(hasDot(r)));
      p.place.classList.toggle('placing', placing === r);
    }
    p.unplace.hidden = !hasDot(r);
    // The stylist's guess waits here until the person answers; it never reaches the brand field by itself.
    if (r.guess) {
      p.suggest.dataset.brand = r.guess;
      p.suggest.replaceChildren(
        el('span', { class: 'items-suggest-q', text: t('items.looks_like', { brand: r.guess }) }),
        el('button', { type: 'button', class: 'chip', 'data-action': 'confirm', text: t('items.confirm'), onclick: () => { r.brand = r.guess; r.confirmed = true; r.guess = null; syncRow(r); } }),
        el('button', { type: 'button', class: 'chip', 'data-action': 'edit', text: t('items.edit'), onclick: () => { r.brand = r.guess; r.confirmed = true; r.guess = null; expand(r, true); p.brand.value = r.brand; p.brand.focus({ preventScroll: true }); p.brand.select(); } }),
        el('button', { type: 'button', class: 'chip', 'data-action': 'dismiss', text: t('items.not_brand'), onclick: () => { r.guess = null; r.brand = ''; r.confirmed = false; p.brand.value = ''; syncRow(r); } })
      );
      p.suggest.hidden = false;
    } else {
      p.suggest.hidden = true;
      p.suggest.replaceChildren();
      delete p.suggest.dataset.brand;
    }
  }

  function refresh() {
    for (const r of rows) syncRow(r);
    syncDots();
    add.disabled = rows.length >= MAX_ITEMS;
  }

  function expand(r, open) {
    const was = expanded;
    expanded = open ? r : null;
    if (was && was !== r) syncRow(was);
    syncRow(r);
    syncDots();
    if (open) r.node.scrollIntoView({ block: 'nearest', behavior: reducedMotion() ? 'auto' : 'smooth' });
  }

  function startPlacing(r) {
    placing = placing === r ? null : r;
    for (const other of rows) syncRow(other);
    syncDots();
    if (placing && box) box.scrollIntoView({ block: 'nearest', behavior: reducedMotion() ? 'auto' : 'smooth' });
  }

  function addItem() {
    if (rows.length >= MAX_ITEMS) return;
    const r = row({ source: 'User' });
    rows.push(r);
    list.appendChild(buildRow(r));
    expanded = r;
    refresh();
    r.node.scrollIntoView({ block: 'nearest', behavior: reducedMotion() ? 'auto' : 'smooth' });
    r.parts.name.focus({ preventScroll: true });
  }

  function removeRow(r) {
    const at = rows.indexOf(r);
    if (at < 0) return;
    rows.splice(at, 1);
    r.node.remove();
    if (expanded === r) expanded = null;
    if (placing === r) placing = null;
    refresh();
    add.focus({ preventScroll: true });
  }

  for (const r of rows) list.appendChild(buildRow(r));
  refresh();

  /** PostItemInput[]: rows with a name, in order; the pending guess is not a brand; x and y both or neither. */
  function value() {
    return rows
      .filter((r) => clean(r.name, NAME_MAX))
      .slice(0, MAX_ITEMS)
      .map((r) => {
        const brand = clean(r.brand, BRAND_MAX) || null;
        return {
          id: r.id || undefined,
          name: clean(r.name, NAME_MAX),
          category: categoryOf(r.category),
          brand,
          model: clean(r.model, MODEL_MAX) || null,
          url: clean(r.url, URL_MAX) || null,
          x: hasDot(r) ? round3(r.x) : null,
          y: hasDot(r) ? round3(r.y) : null,
          confirmed: !!brand && !!r.confirmed
        };
      });
  }

  return { node, value };
}
