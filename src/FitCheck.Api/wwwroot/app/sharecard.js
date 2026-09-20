// The share card: a 1080×1920 story image of a look for Instagram and TikTok stories, drawn on a canvas from what the
// post already shows in public (or what the person's own check just showed them): the photo, the score ring, the
// headline, the intent, who wore it, and the OREVOSH wordmark with "Checked on OREVOSH". Never the tip, the breakdown
// or the occasion. No libraries; the layout is fixed, so the same look always draws the same card.
//
// Wiring: shareCardMenuItem(lookFromPost(post)) is a row for the post's "…" sheet; shareCardButton(lookFromCheck(result,
// state.check.previewUrl)) is a secondary button for the result screen. Both open openShareCard(look): a sheet that
// draws the card, shows it, and offers Share (the system share sheet with the file, where the browser can) and Save.
// The drawing helpers (the stage, the cover crop, the gradient, the ring's colours, text with its own direction, fit and
// wrap by measureText, the image and font loaders) are exported: app/sharevideo.js draws the 12-second video with them.
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
export const COLOR = {
  bg: '#0b0b0f', surface: '#15151c', surface2: '#1e1e27', ink: '#f4f4f7', ink2: '#b9b9c6', ink3: '#7f7f8e',
  accent: '#b39dff', rose: '#ff8fb1', tint: 'rgba(179, 157, 255, 0.16)', line: 'rgba(255, 255, 255, 0.1)'
};
// The same stacks as app.css, with Cairo behind Heebo so Arabic draws in the face the page already loaded; Cyrillic falls to the system face, as on the page.
export const DISPLAY = '"Outfit", "Heebo", "Cairo", system-ui, sans-serif';
export const BODY = '"Heebo", "Cairo", system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans Hebrew", "Noto Sans Arabic", sans-serif';

const pageDir = () => (document.documentElement.dir === 'rtl' ? 'rtl' : 'ltr');
/** The direction of a text by its first strong character (what unicode-bidi: plaintext does for headlines in the app); null when it has none. */
export function strongDir(text) {
  const rtl = /[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]/;
  const ltr = /[A-Za-z\u00C0-\u024F\u0370-\u03FF\u0400-\u04FF]/;
  const m = new RegExp(rtl.source + '|' + ltr.source).exec(text || '');
  if (!m) return null;
  return rtl.test(m[0]) ? 'rtl' : 'ltr';
}

/** The fonts the card sets; a missing face falls back to the stack, and a slow network never holds the card for more than a moment. */
export async function loadFonts() {
  if (!document.fonts || !document.fonts.load) return;
  const faces = ['700 40px Outfit', '800 40px Outfit', '500 40px Heebo', '600 40px Heebo', '700 40px Heebo'];
  const wait = Promise.all(faces.map((face) => document.fonts.load(face).catch(() => null)));
  await Promise.race([wait, new Promise((resolve) => setTimeout(resolve, 2500))]);
}

/**
 * fetch → blob → object URL → Image, for the photo (/api/posts/<id>/image with the session cookie) and for the wordmark
 * SVG. Everything is same-origin, so the canvas stays untainted and toBlob works.
 *
 * A blob: URL — what the check flow hands us for a photo that never left the phone — skips the fetch and goes straight
 * into the Image. It has to: the policy allows a blob in an <img> (img-src) but not as a connection (connect-src), so
 * fetching one is refused outright. It is the same object either way, already same-origin, and the canvas stays clean;
 * the caller owns that URL, so we do not revoke it here.
 */
export async function loadImage(url, revokes) {
  if (/^(blob|data):/.test(url)) return decoded(url);
  const response = await fetch(url, { credentials: 'same-origin' });
  if (!response.ok) throw new Error('image ' + response.status);
  const objectUrl = URL.createObjectURL(await response.blob());
  revokes.push(objectUrl);
  return decoded(objectUrl);
}

/** An Image with that source, returned once the pixels are actually there to draw. */
async function decoded(src) {
  const img = new Image();
  img.decoding = 'async';
  img.src = src;
  if (img.decode) await img.decode();
  else await new Promise((resolve, reject) => { img.onload = resolve; img.onerror = reject; });
  return img;
}

export function roundedRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}
export function disc(ctx, cx, cy, r, fill) {
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, Math.PI * 2);
  ctx.fillStyle = fill;
  ctx.fill();
}
/** The only gradient in the system, lilac at the top-start to rose at the bottom-end, over a box. */
export function theGradient(ctx, x, y, w, h) {
  const g = ctx.createLinearGradient(x, y, x + w, y + h);
  g.addColorStop(0, COLOR.accent);
  g.addColorStop(1, COLOR.rose);
  return g;
}

/** Trims with an ellipsis until the text fits the width. */
export function fit(ctx, text, maxWidth) {
  if (ctx.measureText(text).width <= maxWidth) return text;
  const chars = Array.from(text);
  while (chars.length > 1 && ctx.measureText(chars.join('').replace(/[\s.,;:]+$/, '') + '…').width > maxWidth) chars.pop();
  return chars.join('').replace(/[\s.,;:]+$/, '') + '…';
}
/** Greedy word wrap into at most maxLines lines; the last line takes the ellipsis when there is more. */
export function wrap(ctx, text, maxWidth, maxLines) {
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
export function text(ctx, value, x, y, opts) {
  ctx.font = opts.font;
  ctx.fillStyle = opts.color;
  ctx.direction = opts.textDir || opts.dir;
  ctx.textAlign = opts.align || (opts.dir === 'rtl' ? 'right' : 'left');
  ctx.textBaseline = opts.baseline || 'alphabetic';
  if ('letterSpacing' in ctx) ctx.letterSpacing = opts.tracking || '0px';
  ctx.fillText(value, x, y);
  return ctx.measureText(value).width;
}

export function drawStage(ctx) {
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
/** The image cover-fitted to a box, centred (object-fit: cover), on a placeholder fill. */
export function coverImage(ctx, img, x, y, w, h) {
  ctx.fillStyle = COLOR.surface2;
  ctx.fillRect(x, y, w, h);
  const iw = img.naturalWidth || img.width; const ih = img.naturalHeight || img.height;
  const scale = Math.max(w / iw, h / ih);   // cover
  const dw = iw * scale; const dh = ih * scale;
  ctx.drawImage(img, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
}
function drawPhoto(ctx, img) {
  const { x, y, w, h, r } = PHOTO;
  ctx.save();
  roundedRect(ctx, x, y, w, h, r);
  ctx.clip();
  coverImage(ctx, img, x, y, w, h);
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
/**
 * Round 13 — the growth loop: the line the card and the video's end card carry, so the picture says where to go.
 * "orevosh.app/look/<id>" for a look that is posted (the page anyone can open with no app and no account), the host
 * alone for one that is not, and '' when the server publishes no origin of its own (/api/config publicOrigin, which is
 * Email:PublicOrigin or Billing:PublicOrigin): a card travels, and a localhost line on somebody's story would be a lie.
 */
export function publicLinkLine(look) {
  const configured = state.config && state.config.publicOrigin;
  const origin = typeof configured === 'string' ? configured.trim() : '';
  if (!origin) return '';
  let host = '';
  try { host = new URL(origin).host; } catch (e) { return ''; }
  if (!host) return '';
  return look && look.postId ? host + '/look/' + look.postId : host;
}

/**
 * The colophon: a hairline, the wordmark at the start edge (a logo: it never mirrors) and, at the end, the look's
 * public address when there is one, otherwise "Checked on OREVOSH" as before.
 */
function drawFooter(ctx, wordmark, dir, link) {
  ctx.fillStyle = COLOR.line;
  ctx.fillRect(MARGIN, FOOTER.rule, CARD_WIDTH - MARGIN * 2, 2);
  const h = FOOTER.wordmark;
  const w = wordmark ? Math.round(h * (wordmark.naturalWidth || wordmark.width) / (wordmark.naturalHeight || wordmark.height)) : 0;
  const x = dir === 'rtl' ? CARD_WIDTH - MARGIN - w : MARGIN;
  if (wordmark) ctx.drawImage(wordmark, x, FOOTER.centre - h / 2, w, h);
  else text(ctx, 'OREVOSH', dir === 'rtl' ? CARD_WIDTH - MARGIN : MARGIN, FOOTER.centre, { font: '800 40px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', baseline: 'middle', tracking: '2px' });
  ctx.font = '500 26px ' + BODY;
  if ('letterSpacing' in ctx) ctx.letterSpacing = '0px';
  const line = fit(ctx, link || t('sharecard.checked'), CARD_WIDTH - MARGIN * 2 - w - 40);
  text(ctx, line, dir === 'rtl' ? MARGIN : CARD_WIDTH - MARGIN, FOOTER.centre, {
    // An address is an address: left to right and in the accent, whatever direction the card itself runs.
    font: '500 26px ' + BODY, color: link ? COLOR.accent : COLOR.ink2, dir, textDir: link ? 'ltr' : undefined,
    align: dir === 'rtl' ? 'left' : 'right', baseline: 'middle'
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
    // Round 14 - post the look, keep the grade: no number, no ring. The card is the look, the headline and the intent.
    if (look.score !== null && look.score !== undefined) drawRing(ctx, look.score, dir);
    drawWords(ctx, look, dir);
    drawFooter(ctx, wordmark, dir, publicLinkLine(look));
    return await encode(canvas, opts.maxBytes || MAX_BYTES);
  } finally {
    for (const url of revokes) URL.revokeObjectURL(url);
  }
}

// ---------- the look, from what the app already has ----------

/** A look from a PostDto: everything on the card is what the post shows everyone. postId is what gives it a public address. */
export function lookFromPost(post) {
  return { imageUrl: post.imageUrl, score: post.score, intent: post.intent, headline: post.headline, user: post.user, createdAt: post.createdAt, postId: post.id };
}
/**
 * A look from a check result (the result screen) and the photo's URL (state.check.previewUrl, a blob: URL). The person
 * is sharing their own check; postId is there only once they have posted it, and only then does the card carry a link.
 */
export function lookFromCheck(result, imageUrl) {
  const feedback = result.feedback || {};
  const me = state.me ? { name: state.me.name, handle: state.me.handle } : null;
  return {
    imageUrl, score: feedback.score, intent: result.intent, headline: feedback.headline, user: me, createdAt: result.createdAt,
    postId: state.resultPostId || result.postId || null
  };
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
export async function openShareCard(look, opts) {
  // Round 14 - before and after: opts lets the pair's card use this sheet as it is — { render(look) } draws something
  // else, { title } names it, { onKept } hears that the person actually shared or saved it (the tally). Without opts
  // this is the single look's card, exactly as it was.
  opts = opts || {};
  ensureStyle();
  const content = el('div');
  let objectUrl = null;
  let closed = false;
  const release = () => { if (objectUrl) URL.revokeObjectURL(objectUrl); objectUrl = null; };
  const s = sheet({ title: opts.title || t('sharecard.title'), content, onClose: () => { closed = true; release(); } });

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
      try { await navigator.share({ files: [file], title: t('app.name') }); if (opts.onKept) opts.onKept(); }
      catch (e) { if (!(e && (e.name === 'AbortError' || e.name === 'InvalidStateError'))) toast(t('sharecard.error')); }
      finally { state.sharing = false; }
    } }, [icon('share'), t('sharecard.share')]) : null;
    const save = el('a', { class: 'btn ' + (share ? 'btn-secondary' : ''), id: 'sc-save', href: objectUrl, download: file.name, onclick: () => { if (opts.onKept) opts.onKept(); if (!isIos()) toast(t('sharecard.saved')); } }, [icon('image'), t('sharecard.save')]);
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
    try { blob = await (opts.render ? opts.render(look) : renderShareCard(look)); }
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

// ---------- Round 14 — before and after: the pair's own card ----------
//
// "I tried it": the earlier look, the one after the change, what changed, and — only if the person wants it — the two
// verdicts. It is the same stage, the same faces and the same colophon as the card above, drawn in two columns so the
// two looks are read together, with the before on the start edge (right in Hebrew and Arabic: the pair reads the way
// the language does).
//
// The numbers are a choice, not a property of the card: openBeforeAfterShare (app/sharevideo.js) offers "with both
// scores" and "just the two looks", and a look whose author kept its grade private is only ever offered the second —
// pair.numbers is false and no ring is drawn, here or in the film. The whole point is comfort: people may well want to
// share the decision rather than the grade.
//
// pair: { before: { imageUrl, score }, after: { imageUrl, score }, intent, headline, change, user, postId, numbers }.

const PAIR = (() => {
  const gap = 24;
  const w = Math.round((CARD_WIDTH - MARGIN * 2 - gap) / 2);
  return { y: 320, w, h: Math.round(w * 5 / 4), gap, r: 32, badge: 68 };
})();

/** One of the two looks: the photo in its rounded box, the caps label under it, the small ring when numbers are on. */
function drawPairPhoto(ctx, img, x, label, score, dir) {
  const { y, w, h, r, badge } = PAIR;
  ctx.save();
  roundedRect(ctx, x, y, w, h, r);
  ctx.clip();
  coverImage(ctx, img, x, y, w, h);
  ctx.restore();
  if (score !== null && score !== undefined) {
    // The ring straddles the photo's bottom-inner corner, as the single card's straddles its bottom-end one.
    const cx = dir === 'rtl' ? x + badge - 8 : x + w - badge + 8;
    const cy = y + h - badge + 8;
    ctx.save();
    ctx.shadowColor = 'rgba(0, 0, 0, 0.45)'; ctx.shadowBlur = 30; ctx.shadowOffsetY = 10;
    disc(ctx, cx, cy, badge, theGradient(ctx, cx - badge, cy - badge, badge * 2, badge * 2));
    ctx.restore();
    disc(ctx, cx, cy, badge - 10, COLOR.bg);
    text(ctx, fmtNumber(score), cx, cy + 20, { font: '800 72px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', align: 'center' });
    text(ctx, t('result.out_of'), cx, cy + 46, { font: '700 18px ' + BODY, color: COLOR.ink3, dir: 'ltr', align: 'center', tracking: '1px' });
  }

  const caps = label.toUpperCase();
  const rtl = dir === 'rtl';
  const font = '700 ' + (rtl ? 30 : 27) + 'px ' + BODY;
  ctx.font = font;
  if ('letterSpacing' in ctx) ctx.letterSpacing = rtl ? '0.6px' : '2.5px';
  text(ctx, fit(ctx, caps, w), x + w / 2, y + h + 56, {
    font, color: COLOR.ink2, dir, align: 'center', tracking: rtl ? '0.6px' : '2.5px'
  });
}

/** The arrow between the two looks: the one gradient, a short shaft with a head, mirrored in RTL. */
function drawPairArrow(ctx, dir) {
  const { y, h, gap } = PAIR;
  const cx = CARD_WIDTH / 2;
  const cy = y + Math.round(h / 2);
  const reach = Math.min(gap, 24);
  ctx.save();
  ctx.translate(cx, cy);
  if (dir === 'rtl') ctx.scale(-1, 1);
  ctx.fillStyle = COLOR.bg;
  disc(ctx, 0, 0, 34, COLOR.bg);
  ctx.strokeStyle = theGradient(ctx, -reach, -reach, reach * 2, reach * 2);
  ctx.lineWidth = 6;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  ctx.beginPath();
  ctx.moveTo(-14, 0); ctx.lineTo(12, 0);
  ctx.moveTo(2, -10); ctx.lineTo(12, 0); ctx.lineTo(2, 10);
  ctx.stroke();
  ctx.restore();
}

/** What changed, on the tip card's surface: the caps label in lilac and up to three lines of the person's own words. */
function drawChange(ctx, plan, dir) {
  const { lines, size, lineH, y, h } = plan;
  if (!lines.length) return;
  const rtl = dir === 'rtl';
  const x = MARGIN;
  const w = CARD_WIDTH - MARGIN * 2;
  roundedRect(ctx, x, y, w, h, 36);
  ctx.fillStyle = 'rgba(21, 21, 28, 0.94)';
  ctx.fill();
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 2; ctx.stroke();
  const barX = rtl ? x + w - 36 - 8 : x + 36;
  roundedRect(ctx, barX, y + 34, 8, h - 68, 4);
  ctx.fillStyle = theGradient(ctx, barX, y + 34, 8, h - 68);
  ctx.fill();
  const textX = rtl ? x + w - 68 : x + 68;
  text(ctx, t('share.change_label').toUpperCase(), textX, y + 62, {
    font: '700 ' + (rtl ? 30 : 26) + 'px ' + BODY, color: COLOR.accent, dir, tracking: rtl ? '0.6px' : '2.5px'
  });
  lines.forEach((line, i) => text(ctx, line, textX, y + 130 + i * lineH, {
    font: '700 ' + size + 'px ' + DISPLAY, color: COLOR.ink, dir, textDir: strongDir(line) || dir, tracking: '-0.5px'
  }));
}

/** The change block measured before anything is drawn: at most three lines, the size stepping down until they fit. */
function planChange(words) {
  const measure = document.createElement('canvas').getContext('2d');
  const width = CARD_WIDTH - MARGIN * 2 - 68 - 48;
  let best = { size: 48, lines: [] };
  for (const size of [48, 44, 40, 36]) {
    measure.font = '700 ' + size + 'px ' + DISPLAY;
    if ('letterSpacing' in measure) measure.letterSpacing = '-0.5px';
    const lines = wrap(measure, words || '', width, 3);
    best = { size, lines };
    if (lines.length <= 2) break;
  }
  const lineH = Math.round(best.size * 1.22);
  const y = PAIR.y + PAIR.h + 110;
  return { ...best, lineH, y, h: best.lines.length ? 130 + (best.lines.length - 1) * lineH + 56 : 0 };
}

/**
 * Draws the before/after card and resolves to its image (a PNG or JPEG Blob within 1.5 MB), exactly like
 * renderShareCard. opts: { dir, maxBytes }. Both photos must load: the pair is the picture, and half of it is not it.
 */
export async function renderBeforeAfterCard(pair, opts) {
  opts = opts || {};
  const dir = opts.dir || pageDir();
  const numbers = !!pair.numbers && pair.before.score !== null && pair.before.score !== undefined
    && pair.after.score !== null && pair.after.score !== undefined;
  const revokes = [];
  try {
    const [before, after, wordmark] = await Promise.all([
      loadImage(pair.before.imageUrl, revokes),
      loadImage(pair.after.imageUrl, revokes),
      loadImage('/brand/wordmark.svg', revokes).catch((e) => { console.warn('before/after card: wordmark', e); return null; }),
      loadFonts()
    ]);
    const canvas = document.createElement('canvas');
    canvas.width = CARD_WIDTH; canvas.height = CARD_HEIGHT;
    const ctx = canvas.getContext('2d');
    drawStage(ctx);

    // The title, centred over the pair: "One change" — what the whole format is about.
    text(ctx, t('share.before_after_title'), CARD_WIDTH / 2, 216, {
      font: '700 62px ' + DISPLAY, color: COLOR.ink, dir, textDir: strongDir(t('share.before_after_title')) || dir, align: 'center', tracking: '-0.5px'
    });

    const startX = MARGIN;
    const endX = MARGIN + PAIR.w + PAIR.gap;
    const beforeX = dir === 'rtl' ? endX : startX;
    const afterX = dir === 'rtl' ? startX : endX;
    drawPairPhoto(ctx, before, beforeX, t('share.before_label'), numbers ? pair.before.score : null, dir);
    drawPairPhoto(ctx, after, afterX, t('share.after_label'), numbers ? pair.after.score : null, dir);
    drawPairArrow(ctx, dir);

    // The person's own words about the change when there are any; the stylist's headline for the newer look otherwise.
    // Never the tip: it is the paid thing and it stays inside the app, on this card as on the single one.
    const plan = planChange(pair.change || pair.headline || '');
    drawChange(ctx, plan, dir);

    // Who: the same line the single card carries, at the same baseline.
    drawWords(ctx, { headline: '', intent: pair.intent, user: pair.user }, dir);
    drawFooter(ctx, wordmark, dir, publicLinkLine(pair));
    return await encode(canvas, opts.maxBytes || MAX_BYTES);
  } finally {
    for (const url of revokes) URL.revokeObjectURL(url);
  }
}

/**
 * The pair from a look that names an earlier one (PostDto.before, "after the tip"). numbers is true only when this
 * reader may read both numbers — on your own look, always; on a look whose grade you kept private, never, and the
 * server has already sent null for it either way.
 *
 * A pair that is not two posted looks — the private "I tried it" second check, where both photos are blob: URLs the
 * phone still holds — needs no code here: build the same object and hand it to openShareCard(pair, { render:
 * renderBeforeAfterCard }) or openShareVideo(pair, { render: renderBeforeAfterVideo }) (app/sharevideo.js, which also
 * has openBeforeAfterShare(post) for a posted pair):
 *
 *   { before: { imageUrl, score }, after: { imageUrl, score }, intent, headline, change, user, postId, numbers, language }
 *
 * change is the person's own words about what they changed (never the tip); score may be null on either side, and with
 * numbers false no ring is drawn at all. postId only gives the card its public address, so it is null until the look is
 * posted. The tally route POST /api/posts/{id}/shared-after is a posted look's; a pair of checks has nothing to count
 * against yet, and counting it would need a route of its own on the check.
 */
export function beforeAfterFromPost(post) {
  return {
    before: { imageUrl: post.before.imageUrl, score: post.before.score },
    after: { imageUrl: post.imageUrl, score: post.score },
    intent: post.intent,
    headline: post.headline,
    change: post.caption || '',
    user: post.user,
    postId: post.id,
    numbers: post.score !== null && post.score !== undefined && post.before.score !== null && post.before.score !== undefined
  };
}
