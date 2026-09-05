// The share card: a 1080×1920 story image of a look for Instagram and TikTok stories, drawn on a canvas from what the
// post already shows in public (or what the person's own check just showed them): the photo, the score ring, the
// headline, the intent, who wore it, and the OREVOSH wordmark with "Checked on OREVOSH". Never the tip, the breakdown
// or the occasion. No libraries; the layout is fixed, so the same look always draws the same card.
//
// Wiring: shareCardMenuItem(lookFromPost(post)) is a row for the post's "…" sheet; shareCardButton(lookFromCheck(result,
// state.check.previewUrl)) is a secondary button for the result screen. Both open openShareCard(look): a sheet that
// draws the card, shows it, and offers Share (the system share sheet with the file, where the browser can) and Save.
import { t, el, icon, sheet, toast, state, intentLabel, fmtNumber, isIos } from './core.js';

// ---------- the card ----------

export const CARD_WIDTH = 1080;
export const CARD_HEIGHT = 1920;
const MARGIN = 72;
const PHOTO = { x: MARGIN, y: 120, w: CARD_WIDTH - MARGIN * 2, h: Math.round((CARD_WIDTH - MARGIN * 2) * 5 / 4), r: 48 };   // 4:5, 936×1170
const RING = { r: 160, stroke: 18, inset: 100 };   // the .score-badge idea at story scale: 44px → 320px, 14px offsets → 100px
const HEADLINE = { size: 64, line: 72, baseline: 1478, lines: 2 };
const INTENT = { y: 1596, h: 56 };
const NAME = { baseline: 1722 };
const FOOTER = { rule: 1770, centre: 1838, wordmark: 52 };
const MAX_BYTES = 1.5 * 1024 * 1024;
const COLOR = {
  bg: '#0b0b0f', surface2: '#1e1e27', ink: '#f4f4f7', ink2: '#b9b9c6', ink3: '#7f7f8e',
  accent: '#b39dff', rose: '#ff8fb1', tint: 'rgba(179, 157, 255, 0.16)', line: 'rgba(255, 255, 255, 0.1)'
};
const DISPLAY = '"Outfit", "Heebo", system-ui, sans-serif';
const BODY = '"Heebo", system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans Hebrew", sans-serif';

const pageDir = () => (document.documentElement.dir === 'rtl' ? 'rtl' : 'ltr');
/** The direction of a text by its first strong character (what unicode-bidi: plaintext does for headlines in the app); null when it has none. */
function strongDir(text) {
  const rtl = /[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]/;
  const ltr = /[A-Za-z\u00C0-\u024F\u0370-\u03FF\u0400-\u04FF]/;
  const m = new RegExp(rtl.source + '|' + ltr.source).exec(text || '');
  if (!m) return null;
  return rtl.test(m[0]) ? 'rtl' : 'ltr';
}

/** The fonts the card sets; a missing face falls back to the stack, and a slow network never holds the card for more than a moment. */
async function loadFonts() {
  if (!document.fonts || !document.fonts.load) return;
  const faces = ['700 40px Outfit', '800 40px Outfit', '500 40px Heebo', '700 40px Heebo'];
  const wait = Promise.all(faces.map((face) => document.fonts.load(face).catch(() => null)));
  await Promise.race([wait, new Promise((resolve) => setTimeout(resolve, 2500))]);
}

/**
 * fetch → blob → object URL → Image, for the photo (/api/posts/<id>/image with the session cookie, or a blob: URL from the
 * check) and for the wordmark SVG. Everything is same-origin, so the canvas stays untainted and toBlob works.
 */
async function loadImage(url, revokes) {
  const response = await fetch(url, { credentials: 'same-origin' });
  if (!response.ok) throw new Error('image ' + response.status);
  const objectUrl = URL.createObjectURL(await response.blob());
  revokes.push(objectUrl);
  const img = new Image();
  img.decoding = 'async';
  img.src = objectUrl;
  if (img.decode) await img.decode();
  else await new Promise((resolve, reject) => { img.onload = resolve; img.onerror = reject; });
  return img;
}

function roundedRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}
function disc(ctx, cx, cy, r, fill) {
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, Math.PI * 2);
  ctx.fillStyle = fill;
  ctx.fill();
}
/** The only gradient in the system, lilac at the top-start to rose at the bottom-end, over a box. */
function theGradient(ctx, x, y, w, h) {
  const g = ctx.createLinearGradient(x, y, x + w, y + h);
  g.addColorStop(0, COLOR.accent);
  g.addColorStop(1, COLOR.rose);
  return g;
}

/** Trims with an ellipsis until the text fits the width. */
function fit(ctx, text, maxWidth) {
  if (ctx.measureText(text).width <= maxWidth) return text;
  const chars = Array.from(text);
  while (chars.length > 1 && ctx.measureText(chars.join('').replace(/[\s.,;:]+$/, '') + '…').width > maxWidth) chars.pop();
  return chars.join('').replace(/[\s.,;:]+$/, '') + '…';
}
/** Greedy word wrap into at most maxLines lines; the last line takes the ellipsis when there is more. */
function wrap(ctx, text, maxWidth, maxLines) {
  const words = String(text || '').trim().split(/\s+/).filter(Boolean);
  const lines = [];
  let line = '';
  for (const word of words) {
    const candidate = line ? line + ' ' + word : word;
    if (!line || ctx.measureText(candidate).width <= maxWidth) line = candidate;
    else { lines.push(line); line = word; }
  }
  if (line) lines.push(line);
  if (lines.length > maxLines) return lines.slice(0, maxLines - 1).concat(fit(ctx, lines.slice(maxLines - 1).join(' '), maxWidth));
  return lines.map((l) => fit(ctx, l, maxWidth));   // a single word longer than the line
}
/** Text pinned to the start edge (left in LTR, right in RTL) with its own bidi direction. */
function text(ctx, value, x, y, opts) {
  ctx.font = opts.font;
  ctx.fillStyle = opts.color;
  ctx.direction = opts.textDir || opts.dir;
  ctx.textAlign = opts.align || (opts.dir === 'rtl' ? 'right' : 'left');
  ctx.textBaseline = opts.baseline || 'alphabetic';
  if ('letterSpacing' in ctx) ctx.letterSpacing = opts.tracking || '0px';
  ctx.fillText(value, x, y);
  return ctx.measureText(value).width;
}

function drawStage(ctx) {
  ctx.fillStyle = COLOR.bg;
  ctx.fillRect(0, 0, CARD_WIDTH, CARD_HEIGHT);
  // app.css: radial-gradient(90% 38% at 50% -8%, rgba(179,157,255,.16), transparent 62%) — an ellipse, so the circle is squashed.
  const rx = 0.9 * CARD_WIDTH; const ry = 0.38 * CARD_HEIGHT;
  ctx.save();
  ctx.translate(CARD_WIDTH / 2, -0.08 * CARD_HEIGHT);
  ctx.scale(1, ry / rx);
  const glow = ctx.createRadialGradient(0, 0, 0, 0, 0, rx);
  glow.addColorStop(0, 'rgba(179, 157, 255, 0.16)');
  glow.addColorStop(0.62, 'rgba(179, 157, 255, 0)');
  ctx.fillStyle = glow;
  ctx.fillRect(-CARD_WIDTH, -CARD_HEIGHT * 2, CARD_WIDTH * 2, CARD_HEIGHT * 6);
  ctx.restore();
}
function drawPhoto(ctx, img) {
  const { x, y, w, h, r } = PHOTO;
  ctx.save();
  roundedRect(ctx, x, y, w, h, r);
  ctx.clip();
  ctx.fillStyle = COLOR.surface2;
  ctx.fillRect(x, y, w, h);
  const iw = img.naturalWidth || img.width; const ih = img.naturalHeight || img.height;
  const scale = Math.max(w / iw, h / ih);   // cover
  const dw = iw * scale; const dh = ih * scale;
  ctx.drawImage(img, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
  ctx.restore();
}
/** The score ring: a gradient ring around a stage-dark disc, the numeral inside, "/10" under it, straddling the photo's bottom-end corner. */
function drawRing(ctx, score, dir) {
  const { r, stroke, inset } = RING;
  const cx = dir === 'rtl' ? PHOTO.x + inset + r : PHOTO.x + PHOTO.w - inset - r;
  const cy = PHOTO.y + PHOTO.h + inset - r;
  ctx.save();
  ctx.shadowColor = 'rgba(0, 0, 0, 0.45)'; ctx.shadowBlur = 40; ctx.shadowOffsetY = 14;   // lifts it off the photo
  disc(ctx, cx, cy, r, theGradient(ctx, cx - r, cy - r, r * 2, r * 2));
  ctx.restore();
  ctx.save();
  ctx.shadowColor = 'rgba(179, 157, 255, 0.35)'; ctx.shadowBlur = 70; ctx.shadowOffsetY = 20;   // the hero's lilac halo
  disc(ctx, cx, cy, r, theGradient(ctx, cx - r, cy - r, r * 2, r * 2));
  ctx.restore();
  disc(ctx, cx, cy, r - stroke, COLOR.bg);
  text(ctx, fmtNumber(score), cx, cy + 44, { font: '800 160px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', align: 'center' });
  text(ctx, t('result.out_of'), cx, cy + 92, { font: '700 36px ' + BODY, color: COLOR.ink3, dir: 'ltr', align: 'center', tracking: '1px' });
}
function drawWords(ctx, look, dir) {
  const startX = dir === 'rtl' ? CARD_WIDTH - MARGIN : MARGIN;
  const width = CARD_WIDTH - MARGIN * 2;
  // The headline: Outfit 700, two lines at most, an ellipsis after that. Its own direction inside the page's alignment.
  ctx.font = '700 ' + HEADLINE.size + 'px ' + DISPLAY;
  if ('letterSpacing' in ctx) ctx.letterSpacing = '-0.5px';
  const lines = wrap(ctx, look.headline, width, HEADLINE.lines);
  lines.forEach((line, i) => text(ctx, line, startX, HEADLINE.baseline + i * HEADLINE.line, {
    font: '700 ' + HEADLINE.size + 'px ' + DISPLAY, color: COLOR.ink, dir, textDir: strongDir(line) || dir, tracking: '-0.5px'
  }));
  // The intent as the label pill: caps in lilac on the lilac tint. Hebrew has no capitals; its label steps up a size, as in app.css.
  const label = intentLabel(look.intent).toUpperCase();
  const rtlLabel = dir === 'rtl';
  const labelFont = '700 ' + (rtlLabel ? 29 : 26) + 'px ' + BODY;
  ctx.font = labelFont;
  if ('letterSpacing' in ctx) ctx.letterSpacing = rtlLabel ? '0.6px' : '2px';
  const labelWidth = ctx.measureText(label).width;
  const pillWidth = Math.min(width, labelWidth + 52);
  const pillX = rtlLabel ? startX - pillWidth : startX;
  roundedRect(ctx, pillX, INTENT.y, pillWidth, INTENT.h, INTENT.h / 2);
  ctx.fillStyle = COLOR.tint;
  ctx.fill();
  text(ctx, fit(ctx, label, pillWidth - 40), pillX + pillWidth / 2, INTENT.y + INTENT.h / 2 + 1, {
    font: labelFont, color: COLOR.accent, dir, align: 'center', baseline: 'middle', tracking: rtlLabel ? '0.6px' : '2px'
  });
  // Who: the name in Heebo 700, the @handle after it in the meta grey, always left-to-right.
  const user = look.user || {};
  const name = user.name || user.handle || '';
  if (!name) return;
  ctx.font = '700 34px ' + BODY;
  if ('letterSpacing' in ctx) ctx.letterSpacing = '0px';
  const shownName = fit(ctx, name, user.handle ? 560 : width);
  const nameWidth = text(ctx, shownName, startX, NAME.baseline, { font: '700 34px ' + BODY, color: COLOR.ink, dir, textDir: strongDir(shownName) || dir });
  if (user.handle && user.handle !== name) {
    ctx.font = '500 30px ' + BODY;
    const handle = fit(ctx, '@' + user.handle, width - nameWidth - 16);
    const x = dir === 'rtl' ? startX - nameWidth - 16 : startX + nameWidth + 16;
    text(ctx, handle, x, NAME.baseline, { font: '500 30px ' + BODY, color: COLOR.ink3, dir, textDir: 'ltr' });
  }
}
/** The colophon: a hairline, the wordmark at the start edge (a logo: it never mirrors) and "Checked on OREVOSH" at the end. */
function drawFooter(ctx, wordmark, dir) {
  ctx.fillStyle = COLOR.line;
  ctx.fillRect(MARGIN, FOOTER.rule, CARD_WIDTH - MARGIN * 2, 2);
  const h = FOOTER.wordmark;
  const w = wordmark ? Math.round(h * (wordmark.naturalWidth || wordmark.width) / (wordmark.naturalHeight || wordmark.height)) : 0;
  const x = dir === 'rtl' ? CARD_WIDTH - MARGIN - w : MARGIN;
  if (wordmark) ctx.drawImage(wordmark, x, FOOTER.centre - h / 2, w, h);
  else text(ctx, 'OREVOSH', dir === 'rtl' ? CARD_WIDTH - MARGIN : MARGIN, FOOTER.centre, { font: '800 40px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', baseline: 'middle', tracking: '2px' });
  ctx.font = '500 26px ' + BODY;
  if ('letterSpacing' in ctx) ctx.letterSpacing = '0px';
  const line = fit(ctx, t('sharecard.checked'), CARD_WIDTH - MARGIN * 2 - w - 40);
  text(ctx, line, dir === 'rtl' ? MARGIN : CARD_WIDTH - MARGIN, FOOTER.centre, {
    font: '500 26px ' + BODY, color: COLOR.ink2, dir, align: dir === 'rtl' ? 'left' : 'right', baseline: 'middle'
  });
}

/** PNG when it fits the budget, else the smallest JPEG quality step that does: stories re-encode anyway, and a story-sized PNG of a photo is big. */
async function encode(canvas, maxBytes) {
  const toBlob = (type, quality) => new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('toBlob failed'))), type, quality));
  const png = await toBlob('image/png');
  if (png.size <= maxBytes) return png;
  let jpeg = null;
  for (const quality of [0.92, 0.86, 0.8, 0.72, 0.6]) {
    jpeg = await toBlob('image/jpeg', quality);
    if (jpeg.size <= maxBytes) break;
  }
  return jpeg;
}

/**
 * Draws the card and resolves to its image (a PNG or JPEG Blob within 1.5 MB). look: { imageUrl, score, intent, headline,
 * user: { name, handle }, createdAt? }. opts: { dir: 'ltr' | 'rtl' (the page's direction by default), maxBytes }.
 */
export async function renderShareCard(look, opts) {
  opts = opts || {};
  const dir = opts.dir || pageDir();
  const revokes = [];
  try {
    const [photo, wordmark] = await Promise.all([
      loadImage(look.imageUrl, revokes),
      loadImage('/brand/wordmark.svg', revokes).catch((e) => { console.warn('share card: wordmark', e); return null; }),
      loadFonts()
    ]);
    const canvas = document.createElement('canvas');
    canvas.width = CARD_WIDTH; canvas.height = CARD_HEIGHT;
    const ctx = canvas.getContext('2d');
    drawStage(ctx);
    drawPhoto(ctx, photo);
    drawRing(ctx, look.score, dir);
    drawWords(ctx, look, dir);
    drawFooter(ctx, wordmark, dir);
    return await encode(canvas, opts.maxBytes || MAX_BYTES);
  } finally {
    for (const url of revokes) URL.revokeObjectURL(url);
  }
}

// ---------- the look, from what the app already has ----------

/** A look from a PostDto: everything on the card is what the post shows everyone. */
export function lookFromPost(post) {
  return { imageUrl: post.imageUrl, score: post.score, intent: post.intent, headline: post.headline, user: post.user, createdAt: post.createdAt };
}
/** A look from a check result (the result screen) and the photo's URL (state.check.previewUrl, a blob: URL). The person is sharing their own check. */
export function lookFromCheck(result, imageUrl) {
  const feedback = result.feedback || {};
  const me = state.me ? { name: state.me.name, handle: state.me.handle } : null;
  return { imageUrl, score: feedback.score, intent: result.intent, headline: feedback.headline, user: me, createdAt: result.createdAt };
}

// ---------- the sheet ----------

const CSS = `
.sc-making { display: grid; place-items: center; gap: 4px; min-block-size: 220px; color: var(--ink-2); }
.sc-stage { display: grid; place-items: center; }
.sc-stage img { max-block-size: 60vh; max-inline-size: 100%; border-radius: 12px; box-shadow: 0 10px 30px rgba(0, 0, 0, 0.35); }
.sc-actions { display: flex; flex-direction: column; gap: 10px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const fileName = (blob) => 'orevosh-look.' + (blob.type === 'image/png' ? 'png' : 'jpg');
const canShareFile = (file) => !!(navigator.share && navigator.canShare && navigator.canShare({ files: [file] }));

/**
 * Draws the card in a sheet ("Drawing the card…"), then shows it with Share (the system share sheet with the image, when
 * the browser can share files) and Save (a download; on iOS the hint says to press and hold the image instead).
 */
export async function openShareCard(look) {
  ensureStyle();
  const content = el('div');
  let objectUrl = null;
  let closed = false;
  const release = () => { if (objectUrl) URL.revokeObjectURL(objectUrl); objectUrl = null; };
  const s = sheet({ title: t('sharecard.title'), content, onClose: () => { closed = true; release(); } });

  const paintMaking = () => content.replaceChildren(el('div', { class: 'sc-making', role: 'status' }, [
    el('span', { class: 'loading-mark', 'aria-hidden': 'true' }), el('span', { text: t('sharecard.making') })
  ]));
  const paintError = () => content.replaceChildren(
    el('p', { class: 'alert danger', role: 'alert', text: t('sharecard.error') }),
    el('div', { class: 'sc-actions' }, [el('button', { type: 'button', class: 'btn btn-secondary', text: t('common.retry'), onclick: draw })])
  );
  const paintCard = (blob) => {
    release();
    objectUrl = URL.createObjectURL(blob);
    const file = new File([blob], fileName(blob), { type: blob.type });
    const share = canShareFile(file) ? el('button', { type: 'button', class: 'btn', id: 'sc-share', onclick: async () => {
      if (state.sharing) return;
      state.sharing = true;
      try { await navigator.share({ files: [file], title: t('app.name') }); }
      catch (e) { if (!(e && (e.name === 'AbortError' || e.name === 'InvalidStateError'))) toast(t('sharecard.error')); }
      finally { state.sharing = false; }
    } }, [icon('share'), t('sharecard.share')]) : null;
    const save = el('a', { class: 'btn ' + (share ? 'btn-secondary' : ''), id: 'sc-save', href: objectUrl, download: file.name, onclick: () => { if (!isIos()) toast(t('sharecard.saved')); } }, [icon('image'), t('sharecard.save')]);
    content.replaceChildren(
      el('div', { class: 'sc-stage' }, [el('img', { src: objectUrl, alt: t('sharecard.title'), id: 'sc-card' })]),
      el('p', { class: 'hint', text: t('sharecard.hint') + (isIos() ? ' ' + t('sharecard.ios_hint') : '') }),
      el('div', { class: 'sc-actions' }, [share, save])
    );
    const first = share || save;
    requestAnimationFrame(() => { if (!closed && first.isConnected) first.focus({ preventScroll: true }); });
  };
  async function draw() {
    paintMaking();
    let blob = null;
    try { blob = await renderShareCard(look); }
    catch (e) { console.warn('share card', e); }
    if (closed) return;
    if (blob) paintCard(blob); else paintError();
  }
  await draw();
  return s;
}

/** A row for the post's "…" sheet (actionSheet items): { icon, text, onclick }. */
export function shareCardMenuItem(look) {
  return { icon: 'card', text: t('sharecard.action'), onclick: () => openShareCard(look) };
}
/** A secondary button for the result screen. */
export function shareCardButton(look) {
  return el('button', { type: 'button', class: 'btn btn-secondary', id: 'share-card', onclick: () => openShareCard(look) }, [icon('card'), t('sharecard.action')]);
}
