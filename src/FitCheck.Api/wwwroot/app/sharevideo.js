// Share as video: the share card brought to life. A 12-second, silent, vertical (1080×1920) video of a check, drawn frame by
// frame on a canvas as a pure function of the time, encoded ON THE PHONE with WebCodecs (VideoEncoder) and muxed in the
// browser (vendor/mp4-muxer, vendor/webm-muxer, local modules, no CDN), then handed to the system share sheet (TikTok,
// Instagram, Photos) or downloaded. The server never encodes video: it only hears, through POST /api/checks/{id}/shared-video,
// that one was shared or saved (a tally for the numbers page).
//
// Codec ladder, detected with VideoEncoder.isConfigSupported and never assumed: (1) H.264 baseline in an MP4, the file TikTok
// and Instagram want, on real phones (Chrome on Android, Safari 16.4+); (2) VP9 or VP8 in a WebM where H.264 is not there
// (Chromium builds without proprietary codecs, Playwright's included); (3) the PNG share card with a one-line toast where
// there is no VideoEncoder at all.
//
// The film, in the check's own language (canvas text runs rtl for Hebrew and Arabic; the tip is wrapped by measureText):
//   0.0–1.5   the look full bleed (the check's own photo, cover-cropped: the video is never made without it), the intent
//             pill, a small wordmark in a corner
//   1.5–3.5   the score ring draws in the brand gradient and the number lands big with /10 under it
//   3.5–6.0   Fit · Color · Accessories snap in with their numbers
//   6.0–10.0  the one tip on its card with the accent bar: the longest hold, it is the product
//   10.0–12.0 the end card: the mark, the wordmark, "Check the look.", the @handle when signed in, and the look's
//             public address when it has been posted (orevosh.app/look/<id>, Round 13), the app's host otherwise
// Nothing readable sits in the bottom 320 px or the right 180 px: the platforms' own chrome lives there, so the content
// column is x 72–876 and its centre (474) is the visible centre once that chrome is on.
//
// Wiring: shareVideoButton(videoLookFromCheck(result, judgedStill)) on the result screen (#share-video, busy while it
// renders; the screen draws it only while the still this result was judged on is at hand, so a past check opened from
// "Your checks" has no #share-video); openShareVideo(look) is the sheet (#sv-progress while it renders, then #sv-video with
// #sv-share / #sv-save); renderShareVideo(look, { onProgress, signal }) is the pure rendering call the browser test drives.
import { t, el, icon, sheet, toast, state, api, getLocale, fmtNumber, fmtPercent, isIos } from './core.js';
import {
  COLOR, DISPLAY, BODY, strongDir, loadFonts, loadImage, roundedRect, theGradient, fit, wrap, text, drawStage, coverImage,
  openShareCard, publicLinkLine
} from './sharecard.js';

// ---------- the film ----------

export const VIDEO_WIDTH = 1080;
export const VIDEO_HEIGHT = 1920;
export const DURATION = 12;
const W = VIDEO_WIDTH; const H = VIDEO_HEIGHT;
const BITRATE = 3200000;   // 12 s at 3.2 Mbps is 4.8 MB at most; a mostly still film lands well under
const SAFE = { x: 72, y: 96, right: W - 180 - 24, bottom: H - 320 - 24 };   // 876 and 1576: TikTok's and Instagram's chrome stays out
const COL = { x: SAFE.x, w: SAFE.right - SAFE.x };   // 804 wide
const CX = COL.x + COL.w / 2;   // 474: the visible centre
const OVERLAY = 'rgba(11, 11, 15, 0.58)';
const ROWS = ['fit', 'color', 'accessories'];
const ROW = { y: 720, pitch: 154, h: 130 };
// brand/mark.svg: the ring, the flame's path, where the flame breaks the ring (its base sits up-right of the centre).
const MARK = {
  box: 512, cx: 256, cy: 292, r: 142, stroke: 68, cut: 30,
  flame: 'M0 72 C52 72 72 28 66 -14 C60 -58 42 -82 32 -106 C22 -132 4 -154 -14 -184 C-16 -156 -26 -128 -36 -102 C-48 -104 -60 -112 -64 -124 C-74 -98 -72 -52 -64 -14 C-58 30 -40 72 0 72 Z',
  flameAt: { x: 351.02, y: 186.47, rotate: 42 * Math.PI / 180 },
  fire: '#ff6a2b', fireTip: '#ffa83a'
};

const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);
/** 0 → 1 as t runs from `from` to `to`, clamped. */
const span = (time, from, to) => clamp01((time - from) / (to - from));
const easeOut = (p) => 1 - Math.pow(1 - p, 3);
const easeInOut = (p) => (p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2);
/** Overshoots a little before settling: the snap. */
const easeBack = (p) => { const c1 = 1.70158; const c3 = c1 + 1; return 1 + c3 * Math.pow(p - 1, 3) + c1 * Math.pow(p - 1, 2); };
const lerp = (a, b, p) => a + (b - a) * p;
const mix = (a, b, p) => { const out = {}; for (const k of Object.keys(a)) out[k] = lerp(a[k], b[k], p); return out; };

const rtlLanguage = (lang) => lang === 'he' || lang === 'ar';
const layer = () => { const c = document.createElement('canvas'); c.width = W; c.height = H; return c; };

/**
 * The strings in the check's language. The page's dictionary when it is the same language (nearly always: the check was
 * made in it); otherwise the language's own file, with the page's strings behind it.
 */
async function stringsFor(lang) {
  if (!lang || lang === getLocale()) return t;
  try {
    const response = await fetch('/i18n/' + lang + '.json', { cache: 'no-cache' });
    if (!response.ok) return t;
    const table = await response.json();
    return (key, params) => {
      let value = table[key];
      if (value === undefined) return t(key, params);
      if (params) value = value.replace(/\{(\w+)\}/g, (m, name) => (name in params ? String(params[name]) : m));
      return value;
    };
  } catch (e) { return t; }
}

/**
 * The host in /api/config's publicOrigin (Email:PublicOrigin, else Billing:PublicOrigin) and nothing else; '' when the
 * server publishes none, never a guess from the page's own address. Round 13: the end card names publicLinkLine(look)
 * instead, which is this host with the posted look's page on it; this stays as the host on its own.
 */
export function publicHost() {
  const configured = state.config && state.config.publicOrigin;
  const origin = typeof configured === 'string' ? configured.trim() : '';
  if (!origin) return '';
  try { return new URL(origin).host; } catch (e) { return ''; }
}

/** A look for the video from a check result and the URL of the still it was judged on (a blob: URL). The person is sharing their own check. */
export function videoLookFromCheck(result, imageUrl) {
  const feedback = result.feedback || {};
  const me = state.me ? { name: state.me.name, handle: state.me.handle } : null;
  return {
    checkId: result.id, imageUrl, score: feedback.score, intent: result.intent, headline: feedback.headline,
    breakdown: feedback.breakdown || null, tip: feedback.oneTip || '', language: result.language || getLocale(), user: me,
    // Round 13 - the growth loop: the end card names the look's public address once the check has been posted.
    postId: state.resultPostId || result.postId || null
  };
}
/** The same look for the PNG card (the fallback when the browser cannot make video). */
const cardLook = (look) => ({ imageUrl: look.imageUrl, score: look.score, intent: look.intent, headline: look.headline, user: look.user, postId: look.postId || null });

/**
 * Everything measured once, before the first frame: the direction, the strings, the tip's lines and size, the card's box,
 * the pre-rendered layers (the photo cover-cropped to the frame, the stage, the big ring's halo). drawFrame reads only this.
 * The photo is the look and is always there: renderShareVideo fails before this runs when it could not be loaded.
 */
function planFilm(look, s, photo, wordmark) {
  const lang = look.language || 'en';
  const dir = rtlLanguage(lang) ? 'rtl' : 'ltr';
  const measure = document.createElement('canvas').getContext('2d');
  // A check from before rubric v2 has no sub-scores: no rows then, and the big ring holds until the tip.
  const breakdown = look.breakdown;
  const rows = breakdown ? ROWS.map((key) => ({ key, label: s('result.' + key), value: Math.max(0, Math.min(10, Number(breakdown[key]) || 0)) })) : null;

  // The tip: on its card, wrapped to the text width; the size steps down until it fits six lines.
  const card = { x: COL.x, w: COL.w, r: 36 };
  const textStart = 68; const textEnd = 48;   // the bar sits in the start inset; the ring badge straddles the top-end corner
  const textW = card.w - textStart - textEnd;
  let tip = { size: 56, lines: [] };
  for (const size of [56, 50, 46, 42, 38]) {
    measure.font = '700 ' + size + 'px ' + DISPLAY;
    if ('letterSpacing' in measure) measure.letterSpacing = '-0.5px';
    const lines = wrap(measure, look.tip || '', textW, 6);
    tip = { size, lines };
    if (lines.length <= 5) break;
  }
  tip.lineH = Math.round(tip.size * 1.22);
  tip.labelBaseline = 90;
  tip.firstBaseline = tip.labelBaseline + 40 + Math.round(tip.size * 0.85);
  card.h = tip.firstBaseline + (tip.lines.length - 1) * tip.lineH + 68;
  card.y = Math.max(320, Math.round(860 - card.h / 2));
  card.textX = dir === 'rtl' ? card.x + card.w - textStart : card.x + textStart;
  // The ring badge: straddling the card's top-end corner, as the card's ring straddles the photo.
  const badge = { cx: dir === 'rtl' ? card.x + 40 + 76 : card.x + card.w - 40 - 76, cy: card.y, r: 76, stroke: 12 };

  const layers = { photo: layer(), stage: layer(), halo: layer() };
  coverImage(layers.photo.getContext('2d'), photo, 0, 0, W, H);
  drawStage(layers.stage.getContext('2d'));
  // The big ring's lilac halo and lift, drawn once with shadows (expensive) and blended in when the ring has finished drawing.
  const halo = layers.halo.getContext('2d');
  const big = ringBig();
  for (const shadow of [['rgba(0, 0, 0, 0.45)', 40, 14], ['rgba(179, 157, 255, 0.35)', 70, 20]]) {
    halo.save();
    halo.shadowColor = shadow[0]; halo.shadowBlur = shadow[1]; halo.shadowOffsetY = shadow[2];
    halo.beginPath(); halo.arc(big.cx, big.cy, big.r, 0, Math.PI * 2);
    halo.fillStyle = theGradient(halo, big.cx - big.r, big.cy - big.r, big.r * 2, big.r * 2);
    halo.fill();
    halo.restore();
  }
  halo.save(); halo.globalCompositeOperation = 'destination-out';
  halo.beginPath(); halo.arc(big.cx, big.cy, big.r + 1, 0, Math.PI * 2); halo.fill();   // only the halo stays
  halo.restore();

  const intentText = s('intent.' + look.intent);
  return {
    dir, lang, s, score: Math.max(0, Math.min(10, Number(look.score) || 0)), rows, tip, card, badge, layers, wordmark,
    intent: (intentText === 'intent.' + look.intent ? String(look.intent || '') : intentText).toUpperCase(),
    outOf: s('result.out_of'), tipLabel: s('result.tip').toUpperCase(), cta: s('video.check_look'),
    handle: look.user && look.user.handle ? '@' + look.user.handle : '', host: publicLinkLine(look),
    mark: { canvas: Object.assign(document.createElement('canvas'), { width: MARK.box, height: MARK.box }), flame: new Path2D(MARK.flame) }
  };
}

function ringBig() { return { cx: CX, cy: 720, r: 220, stroke: 28 }; }
/** Where the ring is at t: big while it draws and the number lands, smaller above the rows, then the badge on the tip card. */
function ringAt(time, plan) {
  const big = ringBig();
  const mid = { cx: CX, cy: 470, r: 150, stroke: 20 };
  const badge = plan.badge;
  const held = plan.rows ? mid : big;   // no rows to make room for: the ring stays big until the tip
  if (time < 3.5) return big;
  if (time < 4.1) return mix(big, held, easeInOut(span(time, 3.5, 4.1)));
  if (time < 5.8) return held;
  if (time < 6.4) return mix(held, badge, easeInOut(span(time, 5.8, 6.4)));
  return badge;
}

/** The score ring: the gradient arc drawing clockwise from the top over a stage-dark disc, the numeral inside and "/10" under it, everything scaled from r. */
function drawRing(ctx, ring, progress, value, plan, alpha) {
  const { cx, cy, r, stroke } = ring;
  if (alpha <= 0 || progress <= 0) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.beginPath(); ctx.arc(cx, cy, r - stroke / 2, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(11, 11, 15, 0.82)';
  ctx.fill();
  ctx.beginPath();
  ctx.arc(cx, cy, r - stroke / 2, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * progress);
  ctx.strokeStyle = theGradient(ctx, cx - r, cy - r, r * 2, r * 2);
  ctx.lineWidth = stroke;
  ctx.lineCap = progress < 1 ? 'round' : 'butt';
  ctx.stroke();
  const num = r; const out = Math.round(r * 0.225);
  text(ctx, fmtNumber(value), cx, cy + Math.round(r * 0.275), { font: '800 ' + Math.round(num) + 'px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', align: 'center' });
  text(ctx, plan.outOf, cx, cy + Math.round(r * 0.575), { font: '700 ' + out + 'px ' + BODY, color: COLOR.ink3, dir: 'ltr', align: 'center', tracking: '1px' });
  ctx.restore();
}

/** A caps label pill on a translucent dark ground (the intent), pinned to the start edge. */
function drawPill(ctx, label, x, y, h, dir, alpha) {
  if (alpha <= 0) return;
  const rtl = dir === 'rtl';
  const font = '700 ' + (rtl ? 32 : 28) + 'px ' + BODY;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.font = font;
  if ('letterSpacing' in ctx) ctx.letterSpacing = rtl ? '0.6px' : '2.5px';
  const shown = fit(ctx, label, COL.w - 60);
  const w = ctx.measureText(shown).width + 60;
  const px = rtl ? x - w : x;
  roundedRect(ctx, px, y, w, h, h / 2);
  ctx.fillStyle = 'rgba(11, 11, 15, 0.62)';
  ctx.fill();
  roundedRect(ctx, px, y, w, h, h / 2);
  ctx.fillStyle = COLOR.tint;
  ctx.fill();
  text(ctx, shown, px + w / 2, y + h / 2 + 1, { font, color: COLOR.accent, dir, align: 'center', baseline: 'middle', tracking: rtl ? '0.6px' : '2.5px' });
  ctx.restore();
}

/** The wordmark (a logo: never mirrored) at a height, its left edge at x, on a soft dark pill when it sits over the photo. */
function drawWordmark(ctx, plan, x, y, h, alpha, onPhoto) {
  if (alpha <= 0) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  const img = plan.wordmark;
  const w = img ? Math.round(h * (img.naturalWidth || img.width) / (img.naturalHeight || img.height)) : Math.round(h * 4.6);
  if (onPhoto) {
    roundedRect(ctx, x - 22, y - 14, w + 44, h + 28, (h + 28) / 2);
    ctx.fillStyle = 'rgba(11, 11, 15, 0.55)';
    ctx.fill();
  }
  if (img) ctx.drawImage(img, x, y, w, h);
  else text(ctx, 'OREVOSH', x, y + h / 2, { font: '800 ' + Math.round(h * 0.8) + 'px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', baseline: 'middle', tracking: '2px' });
  ctx.restore();
  return w;
}

/** Fit · Color · Accessories: a row each, the caps label at the start, the number at the end, a gradient bar to the score under both. */
function drawRows(ctx, time, plan) {
  const gone = 1 - span(time, 5.7, 6.0);
  if (!plan.rows || gone <= 0) return;
  const rtl = plan.dir === 'rtl';
  const startX = rtl ? COL.x + COL.w : COL.x;
  const endX = rtl ? COL.x : COL.x + COL.w;
  plan.rows.forEach((row, i) => {
    const t0 = 3.9 + i * 0.25;
    const snap = span(time, t0, t0 + 0.32);
    if (snap <= 0) return;
    const fill = easeOut(span(time, t0 + 0.15, t0 + 0.85));
    const y = ROW.y + i * ROW.pitch;
    ctx.save();
    ctx.globalAlpha = easeOut(snap) * gone;
    const scale = 0.72 + 0.28 * easeBack(snap);
    ctx.translate(CX, y + ROW.h / 2); ctx.scale(scale, scale); ctx.translate(-CX, -(y + ROW.h / 2));
    text(ctx, fit(ctx, row.label.toUpperCase(), COL.w - 200), startX, y + 54, { font: '700 ' + (rtl ? 38 : 34) + 'px ' + BODY, color: COLOR.ink2, dir: plan.dir, tracking: rtl ? '0.6px' : '2.5px' });
    text(ctx, fmtNumber(Math.round(row.value * fill)), endX, y + 70, { font: '800 84px ' + DISPLAY, color: COLOR.ink, dir: 'ltr', align: rtl ? 'left' : 'right' });
    roundedRect(ctx, COL.x, y + 102, COL.w, 10, 5);
    ctx.fillStyle = 'rgba(255, 255, 255, 0.14)';
    ctx.fill();
    const barW = Math.max(10, COL.w * (row.value / 10) * fill);
    roundedRect(ctx, rtl ? COL.x + COL.w - barW : COL.x, y + 102, barW, 10, 5);
    ctx.fillStyle = theGradient(ctx, COL.x, y, COL.w, 10);
    ctx.fill();
    ctx.restore();
  });
}

/** The one tip on its card: the surface, the gradient bar at the start edge, the caps label in lilac, the lines. */
function drawTip(ctx, time, plan) {
  const rise = easeOut(span(time, 6.0, 6.5));
  if (rise <= 0) return;
  const { card, tip } = plan;
  const rtl = plan.dir === 'rtl';
  ctx.save();
  ctx.globalAlpha = easeOut(span(time, 6.0, 6.4));
  ctx.translate(0, (1 - rise) * 40);
  roundedRect(ctx, card.x, card.y, card.w, card.h, card.r);
  ctx.fillStyle = 'rgba(21, 21, 28, 0.94)';
  ctx.fill();
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 2; ctx.stroke();
  const barX = rtl ? card.x + card.w - 36 - 8 : card.x + 36;
  roundedRect(ctx, barX, card.y + 60, 8, card.h - 120, 4);
  ctx.fillStyle = theGradient(ctx, barX, card.y + 60, 8, card.h - 120);
  ctx.fill();
  text(ctx, fit(ctx, plan.tipLabel, card.w - 68 - 200), card.textX, card.y + tip.labelBaseline, { font: '700 ' + (rtl ? 32 : 28) + 'px ' + BODY, color: COLOR.accent, dir: plan.dir, tracking: rtl ? '0.6px' : '2.5px' });
  tip.lines.forEach((line, i) => text(ctx, line, card.textX, card.y + tip.firstBaseline + i * tip.lineH, {
    font: '700 ' + tip.size + 'px ' + DISPLAY, color: COLOR.ink, dir: plan.dir, textDir: strongDir(line) || plan.dir, tracking: '-0.5px'
  }));
  ctx.restore();
}

/** The mark from brand/mark.svg, drawn live so the ring can draw itself and the flame can pop: ring progress and flame scale in 0–1. */
function drawMark(ctx, plan, x, y, size, ringProgress, flameScale, alpha) {
  if (alpha <= 0) return;
  const m = plan.mark.canvas; const g = m.getContext('2d');
  g.clearRect(0, 0, MARK.box, MARK.box);
  const start = Math.atan2(MARK.flameAt.y - MARK.cy, MARK.flameAt.x - MARK.cx);   // the ring draws clockwise from where the flame breaks it
  if (ringProgress > 0) {
    g.beginPath(); g.arc(MARK.cx, MARK.cy, MARK.r, start, start + Math.PI * 2 * ringProgress);
    const grad = g.createLinearGradient(106, 142, 406, 442);
    grad.addColorStop(0, COLOR.accent); grad.addColorStop(1, COLOR.rose);
    g.strokeStyle = grad; g.lineWidth = MARK.stroke; g.lineCap = ringProgress < 1 ? 'round' : 'butt';
    g.stroke();
  }
  // The cut around the flame (the SVG's mask): the flame's outline, 30 wide, taken out of the ring.
  g.save();
  g.translate(MARK.flameAt.x, MARK.flameAt.y); g.rotate(MARK.flameAt.rotate);
  g.globalCompositeOperation = 'destination-out';
  g.lineWidth = MARK.cut; g.lineJoin = 'round'; g.strokeStyle = '#000'; g.fillStyle = '#000';
  g.stroke(plan.mark.flame); g.fill(plan.mark.flame);
  g.restore();
  if (flameScale > 0) {
    g.save();
    g.translate(MARK.flameAt.x, MARK.flameAt.y); g.rotate(MARK.flameAt.rotate);
    g.translate(0, 72); g.scale(flameScale, flameScale); g.translate(0, -72);   // the pop grows from the flame's base
    const fire = g.createLinearGradient(0, 60, 0, -190);
    fire.addColorStop(0, MARK.fire); fire.addColorStop(0.55, MARK.fire); fire.addColorStop(1, MARK.fireTip);
    g.fillStyle = fire; g.fill(plan.mark.flame);
    g.restore();
  }
  ctx.save(); ctx.globalAlpha = alpha; ctx.drawImage(m, x, y, size, size); ctx.restore();
}

/** Seconds 0–10: the look, the ring, the rows, the tip. */
function drawLookScene(ctx, time, plan) {
  const { layers } = plan;
  const zoom = 1 + 0.08 * span(time, 0, 10);   // the slow push in
  ctx.drawImage(layers.photo, W / 2 - (W / 2) * zoom, H / 2 - (H / 2) * zoom, W * zoom, H * zoom);
  // A scrim at the bottom, always, so the platforms' captions read over anything; then the dim once the score takes the stage.
  const scrim = ctx.createLinearGradient(0, H * 0.55, 0, H);
  scrim.addColorStop(0, 'rgba(11, 11, 15, 0)'); scrim.addColorStop(1, 'rgba(11, 11, 15, 0.6)');
  ctx.fillStyle = scrim; ctx.fillRect(0, H * 0.55, W, H * 0.45);
  const dim = easeInOut(span(time, 1.2, 1.9));
  if (dim > 0) { ctx.save(); ctx.globalAlpha = dim; ctx.fillStyle = OVERLAY; ctx.fillRect(0, 0, W, H); ctx.restore(); }

  drawWordmark(ctx, plan, SAFE.x, SAFE.y, 40, easeOut(span(time, 0.2, 0.6)), true);
  const pillIn = easeOut(span(time, 0.4, 0.85));
  drawPill(ctx, plan.intent, plan.dir === 'rtl' ? COL.x + COL.w : COL.x, 176 + (1 - pillIn) * 24, 64, plan.dir, pillIn);

  if (time >= 1.5) {
    const ring = ringAt(time, plan);
    const drawn = easeOut(span(time, 1.5, 2.8));
    const value = Math.round(plan.score * easeOut(span(time, 1.5, 2.7)));
    const halo = span(time, 2.6, 3.1) * (1 - span(time, 3.5, 3.9));
    if (halo > 0) { ctx.save(); ctx.globalAlpha = halo; ctx.drawImage(layers.halo, 0, 0); ctx.restore(); }
    // The numeral lands: scaled from 1.35 down as the ring draws.
    const land = easeBack(span(time, 1.5, 2.3));
    ctx.save();
    if (land < 1) { const sc = lerp(1.35, 1, land); ctx.translate(ring.cx, ring.cy); ctx.scale(sc, sc); ctx.translate(-ring.cx, -ring.cy); }
    drawRing(ctx, ring, drawn, value, plan, easeOut(span(time, 1.5, 1.8)));
    ctx.restore();
  }
  if (time >= 3.9) drawRows(ctx, time, plan);
  if (time >= 6.0) drawTip(ctx, time, plan);
  if (time >= 6.0) {
    // The badge sits over the card (drawn after it), so it is drawn again here once the card is up.
    const ring = ringAt(time, plan);
    if (time >= 6.4) drawRing(ctx, ring, 1, plan.score, plan, 1);
  }
}

/**
 * Seconds 10–12: the stage, the mark drawing itself and the flame popping, the wordmark, "Check the look.", the handle, the
 * host. The call to action runs in the check's language; the @handle and the host are left-to-right whatever their script,
 * as handleText() and the story card draw them (the @ first, never mirrored to the right of a Hebrew or Arabic name).
 */
function drawEndScene(ctx, time, plan) {
  ctx.drawImage(plan.layers.stage, 0, 0);
  const size = 320;
  drawMark(ctx, plan, CX - size / 2, 470, size, easeOut(span(time, 10.05, 10.7)), easeBack(span(time, 10.55, 10.85)), easeOut(span(time, 10.0, 10.2)));
  const wordIn = easeOut(span(time, 10.4, 10.8));
  const wordH = 80; const img = plan.wordmark;
  const wordW = img ? Math.round(wordH * (img.naturalWidth || img.width) / (img.naturalHeight || img.height)) : Math.round(wordH * 4.6);
  ctx.save(); ctx.translate(0, (1 - wordIn) * 24);
  drawWordmark(ctx, plan, CX - wordW / 2, 880, wordH, wordIn, false);
  ctx.restore();
  const lines = [
    { value: plan.cta, y: 1080, font: '700 66px ' + DISPLAY, color: COLOR.ink, dir: plan.dir, textDir: strongDir(plan.cta) || plan.dir, at: 10.6 },
    plan.handle ? { value: plan.handle, y: 1170, font: '500 40px ' + BODY, color: COLOR.ink2, dir: 'ltr', textDir: 'ltr', at: 10.8 } : null,
    plan.host ? { value: plan.host, y: plan.handle ? 1250 : 1170, font: '600 36px ' + BODY, color: COLOR.accent, dir: 'ltr', textDir: 'ltr', at: 10.9 } : null
  ].filter(Boolean);
  for (const line of lines) {
    const p = easeOut(span(time, line.at, line.at + 0.4));
    if (p <= 0) continue;
    ctx.save(); ctx.globalAlpha = p; ctx.translate(0, (1 - p) * 20);
    ctx.font = line.font;
    if ('letterSpacing' in ctx) ctx.letterSpacing = '0px';
    text(ctx, fit(ctx, line.value, COL.w), CX, line.y, { font: line.font, color: line.color, dir: line.dir, textDir: line.textDir, align: 'center' });
    ctx.restore();
  }
}

/** One frame at `time` seconds: a pure function of the time and the plan, so a dropped frame never desyncs the film. */
export function drawFrame(ctx, time, plan) {
  if (time < 10.15) drawLookScene(ctx, time, plan);
  const end = span(time, 9.9, 10.15);
  if (end > 0) {
    ctx.save(); ctx.globalAlpha = end;
    drawEndScene(ctx, time, plan);
    ctx.restore();
  }
}

// ---------- encoding ----------

// In order: H.264 baseline in an MP4 (4.0 fits 1080×1920 at 30 fps; the lower levels are what the brief names and what a
// stricter encoder may still take), High 4.0 as the last MP4 before WebM (every phone decodes it and an MP4 is what the
// apps want), then VP9 and VP8 in a WebM.
const LADDER = [
  { codec: 'avc1.42E028', container: 'mp4', path: 'h264-mp4' },
  { codec: 'avc1.42E01F', container: 'mp4', path: 'h264-mp4' },
  { codec: 'avc1.42001F', container: 'mp4', path: 'h264-mp4' },
  { codec: 'avc1.640028', container: 'mp4', path: 'h264-mp4' },
  { codec: 'vp09.00.10.08', container: 'webm', path: 'vp9-webm' },
  { codec: 'vp09.00.40.08', container: 'webm', path: 'vp9-webm' },
  { codec: 'vp8', container: 'webm', path: 'vp8-webm' }
];
function encoderConfig(step, fps) {
  const config = { codec: step.codec, width: W, height: H, bitrate: BITRATE, framerate: fps, latencyMode: 'quality', bitrateMode: 'variable' };
  if (step.container === 'mp4') config.avc = { format: 'avc' };
  return config;
}
/** The first rung of the ladder this browser's VideoEncoder takes at 1080×1920, or null when there is none (or no VideoEncoder). */
export async function pickEncoding(fps) {
  if (typeof VideoEncoder === 'undefined' || typeof VideoEncoder.isConfigSupported !== 'function' || typeof VideoFrame === 'undefined') return null;
  for (const step of LADDER) {
    try {
      const result = await VideoEncoder.isConfigSupported(encoderConfig(step, fps || 30));
      if (result && result.supported) return step;
    } catch (e) { /* a codec string this browser cannot parse: the next rung */ }
  }
  return null;
}

const abortError = () => { const e = new Error('cancelled'); e.name = 'AbortError'; return e; };
const tick = (ms) => new Promise((resolve) => setTimeout(resolve, ms || 0));

/**
 * Renders the video and resolves to { blob, mime, ext, codec, path, fps, frames, bytes, drawMsAvg, drawMsMax, totalMs }.
 * opts: { onProgress(fraction), signal (an AbortSignal) }. Throws NotSupportedError when no encoder is available,
 * AbortError when cancelled, and a plain Error when the look has no photo or it could not be loaded: the photo is the look,
 * never an optional layer, so no film is made without it. 30 fps, or 24 when three rehearsal frames say drawing is the
 * bottleneck on this device.
 */
export async function renderShareVideo(look, opts) {
  opts = opts || {};
  const onProgress = opts.onProgress || (() => {});
  const cancelled = () => !!(opts.signal && opts.signal.aborted);
  const started = performance.now();
  const encoding = await pickEncoding(30);
  if (!encoding) { const e = new Error('no video encoder'); e.name = 'NotSupportedError'; throw e; }
  if (!look.imageUrl) throw new Error('share video: no photo');
  const s = await stringsFor(look.language);
  const revokes = [];
  try {
    const [photo, wordmark] = await Promise.all([
      loadImage(look.imageUrl, revokes),   // the look itself: a failure here is the video's failure
      loadImage('/brand/wordmark.svg', revokes).catch((e) => { console.warn('share video: wordmark', e); return null; }),
      loadFonts()
    ]);
    if (cancelled()) throw abortError();
    const plan = planFilm(look, s, photo, wordmark);
    return await encodeFilm({ plan, draw: drawFrame, duration: DURATION, rehearseAt: [0.8, 4.3, 7.0], encoding, started, cancelled, onProgress });
  } finally {
    for (const url of revokes) URL.revokeObjectURL(url);
  }
}

/**
 * The encoder, for any film: the rehearsal that picks 30 or 24 fps, the muxer for the rung that was chosen, the frame
 * loop with its backpressure, and the same { blob, mime, ext, codec, path, fps, frames, bytes, drawMs... } every
 * caller of renderShareVideo has always had. draw(ctx, time, plan) is a pure function of the time, so a film is only
 * ever its plan and its drawing: Round 14's before/after (renderBeforeAfterVideo, at the end of this file) is a second
 * plan and a second drawing through this one loop, never a second encoder.
 */
async function encodeFilm({ plan, draw, duration, rehearseAt, encoding, started, cancelled, onProgress }) {
  let encoder = null;
  try {
    const canvas = layer();
    const ctx = canvas.getContext('2d', { alpha: false });
    // Rehearsal: three frames from the busiest moments, timed. Slower than a 30 fps budget allows and the film runs at 24.
    let rehearsal = 0;
    for (const at of rehearseAt) { const a = performance.now(); draw(ctx, at, plan); rehearsal += performance.now() - a; }
    const fps = rehearsal / rehearseAt.length > 22 ? 24 : 30;
    const frames = Math.round(duration * fps);
    const { Muxer, ArrayBufferTarget } = await import(encoding.container === 'mp4' ? '/vendor/mp4-muxer/mp4-muxer.mjs' : '/vendor/webm-muxer/webm-muxer.mjs');
    const target = new ArrayBufferTarget();
    const muxer = new Muxer(encoding.container === 'mp4'
      ? { target, video: { codec: 'avc', width: W, height: H, frameRate: fps }, fastStart: 'in-memory' }
      : { target, video: { codec: encoding.path === 'vp8-webm' ? 'V_VP8' : 'V_VP9', width: W, height: H, frameRate: fps } });
    let failure = null;
    encoder = new VideoEncoder({ output: (chunk, meta) => muxer.addVideoChunk(chunk, meta), error: (e) => { failure = e; } });
    encoder.configure(encoderConfig(encoding, fps));
    const stats = { draw: 0, max: 0 };
    for (let i = 0; i < frames; i++) {
      if (cancelled()) throw abortError();
      if (failure) throw failure;
      const a = performance.now();
      draw(ctx, i / fps, plan);
      const d = performance.now() - a;
      stats.draw += d; if (d > stats.max) stats.max = d;
      const frame = new VideoFrame(canvas, { timestamp: Math.round(i * 1e6 / fps), duration: Math.round(1e6 / fps) });
      try { encoder.encode(frame, { keyFrame: i % (fps * 2) === 0 }); } finally { frame.close(); }
      onProgress((i + 1) / frames);
      // Backpressure: the encoder's queue never grows past a few frames, and the page gets a turn to paint the progress.
      while (encoder.encodeQueueSize > 4) { if (cancelled()) throw abortError(); await tick(4); }
      if (i % 3 === 0) await tick(0);
    }
    await encoder.flush();
    if (failure) throw failure;
    encoder.close(); encoder = null;
    muxer.finalize();
    const mime = encoding.container === 'mp4' ? 'video/mp4' : 'video/webm';
    const blob = new Blob([target.buffer], { type: mime });
    return {
      blob, mime, ext: encoding.container, codec: encoding.codec, path: encoding.path, fps, frames, bytes: blob.size,
      drawMsAvg: stats.draw / frames, drawMsMax: stats.max, totalMs: performance.now() - started
    };
  } finally {
    if (encoder && encoder.state !== 'closed') { try { encoder.close(); } catch (e) { /* already gone */ } }
  }
}

// ---------- the sheet ----------

const CSS = `
.sv-making { display: grid; gap: 14px; min-block-size: 220px; align-content: center; justify-items: center; color: var(--ink-2); text-align: center; }
.sv-progress { inline-size: 100%; block-size: 8px; border-radius: var(--pill); background: var(--surface-2); overflow: hidden; }
.sv-fill { block-size: 100%; inline-size: 0%; border-radius: var(--pill); background: var(--grad); transition: inline-size 120ms linear; }
.sv-making .btn-ghost { min-block-size: 44px; }
.sv-stage { display: grid; place-items: center; }
.sv-stage video { max-block-size: 55vh; max-inline-size: 100%; border-radius: 12px; background: #000; box-shadow: 0 10px 30px rgba(0, 0, 0, 0.35); }
.sv-actions { display: flex; flex-direction: column; gap: 10px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const fileName = (result) => 'orevosh-look.' + result.ext;
const canShareFile = (file) => !!(navigator.share && navigator.canShare && navigator.canShare({ files: [file] }));

/**
 * Makes the video in a sheet (a progress bar and Cancel), then shows it with Share (the system share sheet with the file,
 * when the browser can share files) and Save (a download). Without a VideoEncoder, or with none that takes 1080×1920, the
 * PNG share card opens instead, after a one-line toast. Resolves once the video is made, failed or cancelled.
 */
export async function openShareVideo(look, opts) {
  // Round 14 - before and after: opts lets the pair's film use this sheet as it is — { render(look, o) } makes another
  // film, { title } names it, { fallback() } is what happens where there is no encoder at all (the pair falls back to
  // the pair's card, never to the single look's), { onKept } hears a share or a save. Without opts this is the check's
  // own 12-second film, exactly as it was.
  opts = opts || {};
  if (!(await pickEncoding(30))) {
    toast(t('video.no_encoder'));
    return opts.fallback ? opts.fallback() : openShareCard(cardLook(look));
  }
  ensureStyle();
  const content = el('div');
  let objectUrl = null;
  let closed = false;
  let controller = null;
  const release = () => { if (objectUrl) URL.revokeObjectURL(objectUrl); objectUrl = null; };
  const s = sheet({ title: opts.title || t('video.title'), content, onClose: () => { closed = true; if (controller) controller.abort(); release(); } });
  // The videosMade tally: one POST per video made, on its first save or share (a share and then a save of the same file
  // count once; every video, a retry's or the next tap's, counts again since make() lifts the latch). Never blocking, never a message.
  let counted = false;
  const count = () => {
    if (counted) return;
    counted = true;
    if (opts.onKept) { opts.onKept(); return; }
    if (!look.checkId) return;
    api('POST', '/api/checks/' + encodeURIComponent(look.checkId) + '/shared-video').catch(() => {});
  };

  const paintMaking = () => {
    const fill = el('div', { class: 'sv-fill' });
    const status = el('span', { id: 'sv-status', role: 'status', text: t('video.rendering', { percent: fmtPercent(0) }) });
    const bar = el('div', { class: 'sv-progress', id: 'sv-progress', role: 'progressbar', 'aria-labelledby': 'sv-status', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': '0' }, [fill]);
    content.replaceChildren(el('div', { class: 'sv-making' }, [
      el('span', { class: 'loading-mark', 'aria-hidden': 'true' }), status, bar,
      el('button', { type: 'button', class: 'btn btn-ghost', id: 'sv-cancel', text: t('common.cancel'), onclick: () => s.close() })
    ]));
    // The bar and aria-valuenow follow every frame; the live region is rewritten only when the whole percent has moved and
    // at most twice a second (and at 100), or a screen reader would read the number out hundreds of times.
    let spoken = 0; let spokenAt = 0;
    return (fraction) => {
      const percent = Math.round(fraction * 100);
      fill.style.inlineSize = percent + '%';
      bar.setAttribute('aria-valuenow', String(percent));
      const now = performance.now();
      if (percent === spoken || (now - spokenAt < 500 && percent < 100)) return;
      spoken = percent; spokenAt = now;
      status.textContent = t('video.rendering', { percent: fmtPercent(percent / 100) });
    };
  };
  const paintError = () => content.replaceChildren(
    el('p', { class: 'alert danger', role: 'alert', text: t('video.error') }),
    el('div', { class: 'sv-actions' }, [el('button', { type: 'button', class: 'btn btn-secondary', text: t('common.retry'), onclick: make })])
  );
  const paintVideo = (result) => {
    release();
    objectUrl = URL.createObjectURL(result.blob);
    const file = new File([result.blob], fileName(result), { type: result.mime });
    const share = canShareFile(file) ? el('button', { type: 'button', class: 'btn', id: 'sv-share', onclick: async () => {
      if (state.sharing) return;
      state.sharing = true;
      try { await navigator.share({ files: [file], title: t('app.name') }); count(); }
      catch (e) { if (!(e && (e.name === 'AbortError' || e.name === 'InvalidStateError'))) toast(t('video.error')); }
      finally { state.sharing = false; }
    } }, [icon('share'), t('video.share')]) : null;
    const save = el('a', { class: 'btn ' + (share ? 'btn-secondary' : ''), id: 'sv-save', href: objectUrl, download: file.name, onclick: () => { count(); if (!isIos()) toast(t('video.saved')); } }, [icon('clip'), t('video.save')]);
    const hints = [t('video.hint')];
    if (result.ext === 'webm') hints.push(t('video.webm_hint'));
    if (isIos() && share) hints.push(t('video.ios_hint'));
    content.replaceChildren(
      // The data attributes are the browser test's window on the render: which rung of the ladder ran, at what rate, how big, how slow.
      el('div', { class: 'sv-stage' }, [el('video', {
        id: 'sv-video', src: objectUrl, muted: 'muted', autoplay: 'autoplay', loop: 'loop', playsinline: 'playsinline', 'aria-label': t('video.title'),
        'data-path': result.path, 'data-codec': result.codec, 'data-fps': String(result.fps), 'data-bytes': String(result.bytes),
        'data-draw-ms': result.drawMsAvg.toFixed(1), 'data-draw-max-ms': result.drawMsMax.toFixed(1), 'data-total-ms': String(Math.round(result.totalMs))
      })]),
      el('p', { class: 'hint', text: hints.join(' ') }),
      el('div', { class: 'sv-actions' }, [share, save])
    );
    const video = content.querySelector('video');
    video.muted = true;
    const play = video.play(); if (play && play.catch) play.catch(() => {});
    const first = share || save;
    requestAnimationFrame(() => { if (!closed && first.isConnected) first.focus({ preventScroll: true }); });
  };
  async function make() {
    counted = false;   // a new video: its own save or share counts
    const progress = paintMaking();
    controller = new AbortController();
    let result = null;
    try {
      const render = opts.render || renderShareVideo;
      result = await render(look, { onProgress: progress, signal: controller.signal });
    }
    catch (e) { if (!(e && e.name === 'AbortError')) console.warn('share video', e); }
    if (closed) return;
    if (result) paintVideo(result); else paintError();
  }
  await make();
  return s;
}

/**
 * The result screen's button: secondary, the clip glyph, aria-busy while the video renders. Never disabled: the page behind
 * the sheet is inert, so a second tap cannot reach it, and the sheet hands focus back to this button when it closes (a
 * cancel included), which a disabled button would refuse.
 */
export function shareVideoButton(look) {
  const button = el('button', { type: 'button', class: 'btn btn-secondary', id: 'share-video' }, [icon('clip'), t('video.action')]);
  let busy = false;
  button.addEventListener('click', async () => {
    if (busy) return;
    busy = true;
    button.setAttribute('aria-busy', 'true');
    try { await openShareVideo(look); }
    finally { busy = false; button.removeAttribute('aria-busy'); }
  });
  return button;
}

// ---------- Round 14 — before and after: the pair's film, and the sheet that offers both formats ----------
//
// Ten seconds, silent, vertical, encoded on the phone through the same encodeFilm as the 12-second one:
//   0.0–2.4   the earlier look, full bleed, the BEFORE pill, its ring when the numbers are on
//   2.4–4.8   the newer look takes its place, the AFTER pill, its ring
//   4.8–7.9   the two side by side with the arrow between them and what changed on its card
//   7.9–10.0  the end card the single film ends on: the mark, the wordmark, "Check the look.", the handle, the address
// The numbers are a choice and never a property of the film: with pair.numbers false, no ring is drawn at any point and
// the film is the two looks and the change. A look whose grade its author kept private can only be shared that way —
// the server sent no number to anyone else, and the sheet below does not offer what it does not have.
import { renderBeforeAfterCard, beforeAfterFromPost } from './sharecard.js';

export const PAIR_DURATION = 10;
const PAIR_CUT = { after: 2.4, both: 4.8, end: 7.9 };
// The two-up, inside the safe column: 4:5 boxes with the labels under them, the change card below.
const TWO = (() => {
  const gap = 24;
  const w = Math.round((COL.w - gap) / 2);
  return { y: 520, w, h: Math.round(w * 5 / 4), gap, r: 28, ring: 62 };
})();

/** Everything the pair's film needs, measured once: the two photos, the strings, the change's lines, the end card's parts. */
function planPair(pair, s, before, after, wordmark) {
  const lang = pair.language || getLocale();
  const dir = rtlLanguage(lang) ? 'rtl' : 'ltr';
  const measure = document.createElement('canvas').getContext('2d');
  const numbers = !!pair.numbers && pair.before.score !== null && pair.before.score !== undefined
    && pair.after.score !== null && pair.after.score !== undefined;

  // What changed: the person's own words, wrapped to the column, at most three lines and never the tip.
  let change = { size: 46, lines: [] };
  for (const size of [46, 42, 38, 34]) {
    measure.font = '700 ' + size + 'px ' + DISPLAY;
    if ('letterSpacing' in measure) measure.letterSpacing = '-0.5px';
    const lines = wrap(measure, pair.change || '', COL.w - 120, 3);
    change = { size, lines };
    if (lines.length <= 2) break;
  }
  change.lineH = Math.round(change.size * 1.22);
  change.y = TWO.y + TWO.h + 150;
  change.h = change.lines.length ? 104 + (change.lines.length - 1) * change.lineH + 56 : 0;

  const layers = { before: layer(), after: layer(), stage: layer() };
  coverImage(layers.before.getContext('2d'), before, 0, 0, W, H);
  coverImage(layers.after.getContext('2d'), after, 0, 0, W, H);
  drawStage(layers.stage.getContext('2d'));

  return {
    dir, lang, s, numbers, images: { before, after }, layers, wordmark, change,
    scores: { before: pair.before.score, after: pair.after.score },
    labels: { before: s('share.before_label').toUpperCase(), after: s('share.after_label').toUpperCase() },
    changeLabel: s('share.change_label').toUpperCase(),
    title: s('share.before_after_title'),
    outOf: s('result.out_of'),
    cta: s('video.check_look'),
    handle: pair.user && pair.user.handle ? '@' + pair.user.handle : '',
    host: publicLinkLine(pair),
    mark: { canvas: Object.assign(document.createElement('canvas'), { width: MARK.box, height: MARK.box }), flame: new Path2D(MARK.flame) }
  };
}

/** One look, full bleed, with its pill and — when the numbers are on — its ring at the column's centre. */
function drawPairFull(ctx, plan, which, time, from, alpha) {
  if (alpha <= 0) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  const zoom = 1 + 0.05 * span(time, from, from + 2.4);
  ctx.drawImage(plan.layers[which], W / 2 - (W / 2) * zoom, H / 2 - (H / 2) * zoom, W * zoom, H * zoom);
  const scrim = ctx.createLinearGradient(0, H * 0.55, 0, H);
  scrim.addColorStop(0, 'rgba(11, 11, 15, 0)'); scrim.addColorStop(1, 'rgba(11, 11, 15, 0.6)');
  ctx.fillStyle = scrim; ctx.fillRect(0, H * 0.55, W, H * 0.45);
  drawWordmark(ctx, plan, SAFE.x, SAFE.y, 40, easeOut(span(time, from + 0.2, from + 0.6)), true);
  const pillIn = easeOut(span(time, from + 0.1, from + 0.5));
  drawPill(ctx, plan.labels[which], plan.dir === 'rtl' ? COL.x + COL.w : COL.x, 176 + (1 - pillIn) * 24, 64, plan.dir, pillIn);
  if (plan.numbers) {
    const ring = { cx: CX, cy: 1180, r: 150, stroke: 20 };
    const drawn = easeOut(span(time, from + 0.4, from + 1.4));
    drawRing(ctx, ring, drawn, Math.round(plan.scores[which] * easeOut(span(time, from + 0.4, from + 1.3))), plan, easeOut(span(time, from + 0.4, from + 0.7)));
  }
  ctx.restore();
}

/** Seconds 4.8–7.9: the two looks side by side on the stage, the arrow between them, what changed under them. */
function drawPairTogether(ctx, plan, time) {
  const rise = easeOut(span(time, PAIR_CUT.both, PAIR_CUT.both + 0.5));
  ctx.drawImage(plan.layers.stage, 0, 0);
  const rtl = plan.dir === 'rtl';
  const startX = COL.x;
  const endX = COL.x + TWO.w + TWO.gap;
  const boxes = [
    { which: 'before', x: rtl ? endX : startX, at: PAIR_CUT.both },
    { which: 'after', x: rtl ? startX : endX, at: PAIR_CUT.both + 0.2 }
  ];
  text(ctx, fit(ctx, plan.title, COL.w), CX, 380, {
    font: '700 58px ' + DISPLAY, color: COLOR.ink, dir: plan.dir, textDir: strongDir(plan.title) || plan.dir, align: 'center', tracking: '-0.5px'
  });
  for (const box of boxes) {
    const p = easeOut(span(time, box.at, box.at + 0.45));
    if (p <= 0) continue;
    ctx.save();
    ctx.globalAlpha = p;
    ctx.translate(0, (1 - p) * 30);
    ctx.save();
    roundedRect(ctx, box.x, TWO.y, TWO.w, TWO.h, TWO.r);
    ctx.clip();
    coverImage(ctx, plan.images[box.which], box.x, TWO.y, TWO.w, TWO.h);
    ctx.restore();
    if (plan.numbers) {
      const cx = rtl ? box.x + TWO.ring - 6 : box.x + TWO.w - TWO.ring + 6;
      const cy = TWO.y + TWO.h - TWO.ring + 6;
      drawRing(ctx, { cx, cy, r: TWO.ring, stroke: 10 }, 1, plan.scores[box.which], plan, 1);
    }

    const font = '700 ' + (rtl ? 30 : 27) + 'px ' + BODY;
    ctx.font = font;
    if ('letterSpacing' in ctx) ctx.letterSpacing = rtl ? '0.6px' : '2.5px';
    text(ctx, fit(ctx, plan.labels[box.which], TWO.w), box.x + TWO.w / 2, TWO.y + TWO.h + 58, {
      font, color: COLOR.ink2, dir: plan.dir, align: 'center', tracking: rtl ? '0.6px' : '2.5px'
    });
    ctx.restore();
  }

  // The arrow between the boxes: the one gradient, pointing the way the language reads.
  const arrow = easeOut(span(time, PAIR_CUT.both + 0.45, PAIR_CUT.both + 0.9));
  if (arrow > 0) {
    ctx.save();
    ctx.globalAlpha = arrow;
    ctx.translate(CX, TWO.y + TWO.h / 2);
    if (rtl) ctx.scale(-1, 1);
    ctx.beginPath(); ctx.arc(0, 0, 30, 0, Math.PI * 2); ctx.fillStyle = COLOR.bg; ctx.fill();
    ctx.strokeStyle = theGradient(ctx, -20, -20, 40, 40);
    ctx.lineWidth = 6; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    ctx.beginPath();
    ctx.moveTo(-13, 0); ctx.lineTo(11, 0);
    ctx.moveTo(2, -9); ctx.lineTo(11, 0); ctx.lineTo(2, 9);
    ctx.stroke();
    ctx.restore();
  }

  const { change } = plan;
  if (change.lines.length && rise > 0) {
    const p = easeOut(span(time, PAIR_CUT.both + 0.7, PAIR_CUT.both + 1.2));
    if (p <= 0) return;
    ctx.save();
    ctx.globalAlpha = p;
    ctx.translate(0, (1 - p) * 30);
    roundedRect(ctx, COL.x, change.y, COL.w, change.h, 32);
    ctx.fillStyle = 'rgba(21, 21, 28, 0.94)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'; ctx.lineWidth = 2; ctx.stroke();
    const barX = rtl ? COL.x + COL.w - 32 - 8 : COL.x + 32;
    roundedRect(ctx, barX, change.y + 30, 8, change.h - 60, 4);
    ctx.fillStyle = theGradient(ctx, barX, change.y + 30, 8, change.h - 60);
    ctx.fill();
    const textX = rtl ? COL.x + COL.w - 60 : COL.x + 60;
    text(ctx, plan.changeLabel, textX, change.y + 56, {
      font: '700 ' + (rtl ? 30 : 26) + 'px ' + BODY, color: COLOR.accent, dir: plan.dir, tracking: rtl ? '0.6px' : '2.5px'
    });
    change.lines.forEach((line, i) => text(ctx, line, textX, change.y + 104 + 30 + i * change.lineH, {
      font: '700 ' + change.size + 'px ' + DISPLAY, color: COLOR.ink, dir: plan.dir, textDir: strongDir(line) || plan.dir, tracking: '-0.5px'
    }));
    ctx.restore();
  }
}

/**
 * One frame of the pair's film at `time` seconds: a pure function of the time and the plan, like drawFrame. The end
 * card is the single film's, drawn with the clock shifted so its 10.0–11.0 beats land in this film's last two seconds.
 */
export function drawPairFrame(ctx, time, plan) {
  if (time < PAIR_CUT.both) {
    drawPairFull(ctx, plan, 'before', time, 0, 1 - span(time, PAIR_CUT.after, PAIR_CUT.after + 0.4));
    drawPairFull(ctx, plan, 'after', time, PAIR_CUT.after, span(time, PAIR_CUT.after, PAIR_CUT.after + 0.4));
  } else if (time < PAIR_CUT.end + 0.15) {
    drawPairTogether(ctx, plan, time);
  }
  const end = span(time, PAIR_CUT.end - 0.25, PAIR_CUT.end);
  if (end > 0) {
    ctx.save();
    ctx.globalAlpha = end;
    drawEndScene(ctx, time + (10.0 - PAIR_CUT.end), plan);
    ctx.restore();
  }
}

/**
 * Renders the pair's film and resolves to the same result object renderShareVideo does. Throws NotSupportedError with
 * no encoder, AbortError when cancelled, and a plain Error when either photo is missing: half a pair is not a pair.
 */
export async function renderBeforeAfterVideo(pair, opts) {
  opts = opts || {};
  const onProgress = opts.onProgress || (() => {});
  const cancelled = () => !!(opts.signal && opts.signal.aborted);
  const started = performance.now();
  const encoding = await pickEncoding(30);
  if (!encoding) { const e = new Error('no video encoder'); e.name = 'NotSupportedError'; throw e; }
  if (!pair.before || !pair.after || !pair.before.imageUrl || !pair.after.imageUrl) throw new Error('before/after video: no pair');
  const s = await stringsFor(pair.language);
  const revokes = [];
  try {
    const [before, after, wordmark] = await Promise.all([
      loadImage(pair.before.imageUrl, revokes),
      loadImage(pair.after.imageUrl, revokes),
      loadImage('/brand/wordmark.svg', revokes).catch((e) => { console.warn('before/after video: wordmark', e); return null; }),
      loadFonts()
    ]);
    if (cancelled()) throw abortError();
    const plan = planPair(pair, s, before, after, wordmark);
    return await encodeFilm({ plan, draw: drawPairFrame, duration: PAIR_DURATION, rehearseAt: [1.0, 3.4, 5.6], encoding, started, cancelled, onProgress });
  } finally {
    for (const url of revokes) URL.revokeObjectURL(url);
  }
}

// ---------- the sheet: the pair, with the numbers or without them ----------

/**
 * "Share before / after" on a look that follows an earlier one. One sheet: the two looks as they will appear, the
 * choice of carrying the two verdicts or no numbers at all, and the two formats (the story card and the film). The
 * choice is the point of the whole thing — people may well want to share the decision rather than the grade — so it
 * is a plain pair of chips, the plain one is always there, and the one with numbers only when there are numbers to
 * show: a look whose grade is private has none, for its reader or for its card.
 *
 * Whatever is made is counted once, when it is actually shared or saved, through POST /api/posts/{id}/shared-after
 * with withScores — two rows on the numbers page, so the owner can see which version people use. Nothing else is sent.
 */
export function openBeforeAfterShare(post) {
  if (!post || !post.before) return null;
  const pair = beforeAfterFromPost(post);
  // The numbers can be carried when this reader has both of them — on your own look, always. A look whose grade you
  // chose to keep starts on the version with no numbers: the choice was made once already, and this is the same one.
  const canNumber = pair.numbers;
  let numbers = canNumber && !post.scorePrivate;

  const count = () => {
    if (!post.isMine) return;   // the tally is the author's own act, and the route answers 404 to anyone else
    api('POST', '/api/posts/' + encodeURIComponent(post.id) + '/shared-after', { withScores: numbers }).catch(() => {});
  };
  const withPair = () => ({ ...pair, numbers });

  const chips = el('div', { class: 'ba-choice', role: 'group', 'aria-label': t('share.numbers_label') });
  const paint = () => { for (const chip of chips.children) chip.setAttribute('aria-pressed', String((chip.dataset.numbers === 'true') === numbers)); };
  const chip = (on, label) => {
    const node = el('button', { type: 'button', class: 'chip', 'data-numbers': String(on), text: label, onclick: () => { numbers = on; paint(); } });
    return node;
  };
  if (canNumber) chips.appendChild(chip(true, t('share.with_numbers')));
  chips.appendChild(chip(false, t('share.without_numbers')));
  paint();

  const content = el('div', { class: 'stack ba-sheet' }, [
    el('div', { class: 'ba-pair', 'aria-hidden': 'true' }, [
      el('img', { src: pair.before.imageUrl, alt: '' }),
      el('img', { src: pair.after.imageUrl, alt: '' })
    ]),
    el('p', { class: 'hint', text: t('share.before_after_hint') }),
    post.scorePrivate ? el('p', { class: 'hint', id: 'ba-private', text: t('share.before_after_private') }) : null,
    el('span', { class: 'label', text: t('share.numbers_label') }),
    chips,
    el('div', { class: 'sv-actions' }, [
      el('button', {
        type: 'button', class: 'btn', id: 'ba-card',
        onclick: () => {
          s.close();
          openShareCard(withPair(), {
            title: t('share.before_after_title'),
            render: (look) => renderBeforeAfterCard(look),
            onKept: count
          });
        }
      }, [icon('card'), t('share.before_after_card')]),
      el('button', {
        type: 'button', class: 'btn btn-secondary', id: 'ba-video',
        onclick: () => {
          s.close();
          const made = withPair();
          openShareVideo(made, {
            title: t('share.before_after_title'),
            render: (look, o) => renderBeforeAfterVideo(look, o),
            fallback: () => openShareCard(made, { title: t('share.before_after_title'), render: (look) => renderBeforeAfterCard(look), onKept: count }),
            onKept: count
          });
        }
      }, [icon('clip'), t('share.before_after_video')])
    ])
  ]);
  ensurePairStyle();
  const s = sheet({ title: t('share.before_after_title'), content });
  s.panel.id = 'before-after-sheet';
  return s;
}

let pairStyled = false;
function ensurePairStyle() {
  if (pairStyled) return;
  pairStyled = true;
  document.head.appendChild(el('style', { text: [
    '.ba-sheet .ba-pair { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }',
    '.ba-sheet .ba-pair img { inline-size: 100%; aspect-ratio: 4 / 5; object-fit: cover; border-radius: 12px; background: var(--surface-2); }',
    '.ba-sheet .ba-choice { display: flex; gap: 8px; flex-wrap: wrap; }',
    '.ba-sheet .ba-choice .chip { min-block-size: 44px; }'
  ].join('\n') }));
}
