// Check and result: add a photo or a clip (the camera, the library), say where the outfit is going, let the stylist look,
// read the verdict, post the look. Ported from the Phase 2 monolith onto the kit. The ids (#photo, #submit, #occasion,
// #check-error, #result, #post-open, #post-confirm, #post-link, #caption, #challenge-pick) and the .score/.result-headline/
// .items/.working/.tip/.bar structure are part of the browser test contract; keep them when changing the layout. The media
// sheet's rows are #media-camera, #media-library and #media-clip; a clip's frame slider is #clip-frame. The rubric v2
// block is #breakdown (ul.breakdown with li[data-part=fit|color|accessories]) and #accessories (.acc-verdict.<verdict>,
// .acc-present .chip, .acc-note, .acc-add.tip). Round 9, guests: #guest-banner sits above the form when signed out, the
// result of a guest's check shows #guest-keep ("Sign up to keep it and post it") where #post-open would be, and #post-open
// takes its place once the claim has run after signup; #checks-left is the signed-in cap line, with #go-pro when none are left.
import {
  register, state, t, api, el, icon, setTopBar, navigate, requireSignIn, sheet, toast, announce, focusHeading, onLeave, pickFile, prepareImage, frameToJpeg, fmtNumber, fmtPercent, intentLabel, INTENTS, MAX_EDGE, isBrand, isMe, loadMe, claimGuestChecks, getLocale, reducedMotion, copyText, view, $, redirect, showAlert, logoMark, breakdownRow
} from '../core.js';
import { shareCardButton, lookFromCheck } from '../sharecard.js';
import { afterPicker } from '../after.js';

const SCORE_COUNT_MS = 900;
const ACCESSORY_VERDICTS = ['adds', 'neutral', 'missing', 'clashes'];

// The clip in the photo box, and the frame picker under it: the slider is the one control, the rest is copy. The guest
// banner is a notice card with the display face; the cap line sits under the submit button with the private note.
const CSS = `
.photo video { inline-size: 100%; block-size: 100%; object-fit: cover; background: #000; }
.guest-banner { display: grid; gap: 4px; margin-block-end: 16px; }
.guest-banner h3 { font-family: var(--font-display); font-size: 20px; line-height: 1.15; font-weight: 800; margin: 0; }
.guest-banner p { margin: 0; }
.guest-banner .btn-text { min-block-size: 44px; padding-block: 0; justify-self: start; }
.checks-left { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 10px; }
.checks-left .btn-text { font-size: 13px; min-block-size: 44px; padding-block: 0; }
.clip-tools { display: flex; flex-direction: column; gap: 10px; }
.clip-tools > label { color: var(--ink-3); }
.clip-tools input[type="range"] { inline-size: 100%; min-block-size: 44px; margin: 0; accent-color: var(--accent); cursor: pointer; }
.clip-row { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.clip-row .tag { gap: 5px; }
.clip-row .tag svg { inline-size: 12px; block-size: 12px; }
.clip-row .btn-text { padding-block: 0; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/** Set by the check screen before it opens the camera, so "Use it" and the close button return with Back (no duplicate history entry). */
export const cameraReturn = { fromCheck: false };

// The frame of the current clip that is the still (ms into the clip); null until one is chosen. Module state, like the clip
// it belongs to in state.check: it survives a trip to the camera and a language switch, and goes with the clip.
let frameMs = null;
let busyKind = 'photo';   // what the "preparing" label talks about while a library file is read
let capturing = false;    // a frame is on its way to the canvas: the submit waits for it
let captureSeq = 0;
let seekSeq = 0;

// ---------- check ----------

register('check', async (root) => {
  const ck = state.check;
  setTopBar({ title: t('check.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('check.title') }));
  if (ck.busy) { root.appendChild(loadingBlock()); return; }   // a check is in flight; the result view takes over when it lands
  ensureStyle();

  // Signed out is not a wall any more: one check as a guest, and the account comes after the verdict.
  if (!state.me) root.appendChild(guestBanner());
  const form = el('form', { class: 'stack', novalidate: true, onsubmit: (event) => { event.preventDefault(); submitCheck(); } });
  root.appendChild(form);

  if (ck.challenge) {
    form.appendChild(el('div', { class: 'alert between', id: 'challenge-banner' }, [
      el('div', {}, [
        el('b', { text: t('check.entering', { title: ck.challenge.title }) }),
        el('div', { class: 'muted', style: 'font-size: 13px; margin-block-start: 2px;', text: t('check.entering_hint', { tag: '#' + ck.challenge.tag }) })
      ]),
      el('button', { type: 'button', class: 'btn btn-secondary btn-sm', style: 'flex: none;', text: t('common.cancel'), onclick: () => { ck.challenge = null; navigate('#/check'); } })
    ]));
  }

  const chips = el('div', { class: 'chips', role: 'group', 'aria-label': t('a11y.intent_group') });
  for (const intent of INTENTS) {
    chips.appendChild(el('button', {
      type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(ck.intent === intent), text: intentLabel(intent),
      onclick: () => {
        ck.intent = intent;
        for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === intent));
        updateSubmit();
      }
    }));
  }
  form.appendChild(el('div', {}, [el('h2', { text: t('check.intent_label') }), el('div', { style: 'margin-block-start: 10px;' }, [chips])]));

  const occasion = el('input', {
    type: 'text', id: 'occasion', maxlength: '120', autocomplete: 'off', enterkeyhint: 'done', placeholder: t('check.occasion_placeholder'),
    value: ck.occasion, oninput: (event) => { ck.occasion = event.target.value; }
  });
  form.appendChild(el('div', { class: 'field' }, [el('label', { for: 'occasion', text: t('check.occasion_label') }), occasion]));

  form.appendChild(el('button', { id: 'photo', class: 'photo', type: 'button', onclick: chooseMedia }));
  // Two outfits, one verdict: the comparison has its own screen.
  form.appendChild(el('p', { class: 'hint', style: 'text-align: center; margin-block-start: -4px;' }, [el('a', { class: 'btn-text', id: 'which-one', href: '#/compare', text: t('compare.title') })]));
  form.appendChild(el('div', { id: 'clip-tools', class: 'clip-tools', hidden: true }));
  const error = el('p', { id: 'check-error', class: 'alert danger', role: 'alert', hidden: true });
  if (ck.error) { error.textContent = ck.error; error.hidden = false; ck.error = null; }
  form.appendChild(error);
  form.appendChild(el('button', { id: 'submit', class: 'btn', type: 'submit', text: t('check.submit') }));
  const left = checksLeftLine();
  if (left) form.appendChild(left);
  form.appendChild(el('p', { class: 'hint', text: t('check.private_note') }));
  renderPhoto();
  updateSubmit();
});

/** "Try it first": one free check, no account; signing up keeps it. A join link for the visitor who already used it. */
function guestBanner() {
  return el('div', { class: 'notice guest-banner', id: 'guest-banner' }, [
    el('h3', { text: t('guest.title') }),
    el('p', { class: 'muted', text: t('guest.hint') }),
    el('a', { class: 'btn-text', id: 'guest-join', href: '#/signup', text: t('auth.signup'), onclick: () => { state.returnTo = '#/check'; } })
  ]);
}

/**
 * "{n} of {cap} checks left today" for a signed-in person, from MeDto.checksToday / checksPerDay (the server fills them in;
 * 0 for the cap means unknown and the line stays out). At zero left, the way to more is the Pro screen.
 */
function checksLeftLine() {
  const me = state.me;
  if (!me || !(me.checksPerDay > 0)) return null;
  const cap = me.checksPerDay;
  const n = Math.max(0, cap - (me.checksToday || 0));
  return el('p', { class: 'hint checks-left', id: 'checks-left' }, [
    el('span', { text: t('check.left', { n, cap: fmtNumber(cap) }) }),
    n === 0 ? el('a', { class: 'btn-text', id: 'go-pro', href: '#/pro', text: t('check.go_pro') }) : null
  ]);
}

/** While the stylist looks: the mark at 96px with its flame breathing (app.css animates .breathing); the pulse dot when index.html has no #mark-template. */
function loadingBlock() {
  const mark = logoMark(96);
  return el('div', { class: 'loading', role: 'status' }, [
    el('div', {}, [
      mark ? el('div', { class: 'mark breathing', 'aria-hidden': 'true' }, [mark]) : el('div', { class: 'loading-mark', 'aria-hidden': 'true' }),
      el('p', { text: t('loading.line'), tabindex: '-1' })
    ])
  ]);
}

/** Paints the photo button from state: empty prompt, "preparing", the photo, or the clip paused on its chosen frame (with the picker under it). */
function renderPhoto() {
  const photo = $('photo');
  if (!photo) return;
  const ck = state.check;
  const hasMedia = !!(ck.previewUrl || ck.clipUrl);
  const label = ck.photoBusy ? t(busyKind === 'clip' ? 'check.clip_processing' : 'check.photo_processing') : t(hasMedia ? 'check.media_replace' : 'check.media_add');
  photo.classList.toggle('has-image', hasMedia);
  photo.classList.toggle('has-clip', !!ck.clipUrl);
  photo.setAttribute('aria-label', label);
  photo.setAttribute('aria-busy', String(ck.photoBusy));
  photo.innerHTML = '';
  if (ck.clipUrl) {
    const video = el('video', { src: ck.clipUrl, muted: true, playsinline: true, 'webkit-playsinline': true, preload: 'auto', 'aria-label': t('a11y.photo_preview') });
    video.muted = true;
    photo.appendChild(video);
    photo.appendChild(el('span', { class: 'photo-replace', text: label }));
  } else if (ck.previewUrl) {
    photo.appendChild(el('img', { src: ck.previewUrl, alt: t('a11y.photo_preview') }));
    photo.appendChild(el('span', { class: 'photo-replace', text: label }));
  } else {
    photo.appendChild(el('span', { class: 'photo-empty' }, [
      ck.photoBusy ? el('span', { class: 'loading-mark', 'aria-hidden': 'true', style: 'margin-block-end: 0;' }) : icon('camera'),
      el('strong', { text: label }),
      ck.photoBusy ? null : el('span', { class: 'hint', text: t('check.photo_hint') })
    ]));
  }
  renderClipTools();
}

const seconds = (ms) => fmtNumber(Math.round(ms / 100) / 10);
/** 40% into a clip longer than two seconds (people are usually posed by then), else the first frame. */
const preselectMs = (clipMs) => (clipMs > 2000 ? Math.round(clipMs * 0.4) : 0);

/**
 * The frame picker: the clip sits in the photo box, paused on the chosen frame; the slider seeks it, and on release the
 * frame goes to a canvas and becomes the still the stylist judges (and the clip's poster). Moving the slider only seeks;
 * a capture that a newer one overtakes lands nowhere.
 */
function renderClipTools() {
  const tools = $('clip-tools');
  if (!tools) return;
  const ck = state.check;
  tools.innerHTML = '';
  tools.hidden = !ck.clipUrl;
  if (!ck.clipUrl) return;
  const video = $('photo').querySelector('video');
  const max = Math.max(100, Math.round(ck.clipMs));
  if (frameMs === null) frameMs = preselectMs(ck.clipMs);
  frameMs = Math.min(frameMs, max);
  const range = el('input', { type: 'range', id: 'clip-frame', min: '0', max: String(max), step: '50', value: String(frameMs), 'aria-valuetext': t('check.clip_ready', { s: seconds(frameMs) }) });
  range.addEventListener('input', () => { frameMs = Number(range.value); range.setAttribute('aria-valuetext', t('check.clip_ready', { s: seconds(frameMs) })); seekVideo(video, frameMs); });
  range.addEventListener('change', () => { frameMs = Number(range.value); captureFrame(video, frameMs); });
  tools.appendChild(el('label', { for: 'clip-frame', text: t('check.clip_frame') }));
  tools.appendChild(range);
  tools.appendChild(el('p', { class: 'hint', text: t('check.clip_frame_hint') }));
  tools.appendChild(el('div', { class: 'clip-row' }, [
    el('span', { class: 'tag' }, [icon('clip'), t('check.clip_ready', { s: seconds(ck.clipMs) })]),
    el('button', { type: 'button', class: 'btn-text', id: 'clip-remove', text: t('check.clip_remove'), onclick: removeClip })
  ]));
  tools.appendChild(el('p', { class: 'hint', text: t('check.clip_note') }));
  // Land on the frame: capture it when there is no still yet (a fresh clip), otherwise just show it.
  const needStill = !ck.photo;
  primeVideo(video).then(() => {
    if (!document.contains(video)) return null;
    return needStill ? captureFrame(video, frameMs) : seekVideo(video, frameMs);
  }).catch(() => { if (document.contains(video)) showError(t('error.video_read')); });
}

/** Waits for the clip's metadata; a MediaRecorder webm has no duration until the browser has scanned it (the far seek does that). */
async function primeVideo(video) {
  if (video.readyState < 1) {
    await new Promise((resolve, reject) => {
      video.addEventListener('loadedmetadata', resolve, { once: true });
      video.addEventListener('error', () => reject(new Error('video')), { once: true });
      setTimeout(() => reject(new Error('timeout')), 15000);
    });
  }
  if (!isFinite(video.duration)) {
    await new Promise((resolve) => {
      const done = () => { video.removeEventListener('durationchange', done); resolve(); };
      video.addEventListener('durationchange', done);
      setTimeout(done, 2000);
      video.currentTime = 1e101;
    });
    video.currentTime = 0;
  }
  // Some browsers only paint frames onto a canvas after the element has played once; muted inline play is always allowed.
  try { await video.play(); video.pause(); } catch (e) { /* frames draw anyway on the rest */ }
}
/** Seeks and resolves once the frame is there (false when a newer seek overtook this one). Never hangs on a broken file. */
function seekVideo(video, ms) {
  const mine = ++seekSeq;
  return new Promise((resolve) => {
    const limit = isFinite(video.duration) ? Math.max(0, video.duration * 1000 - 40) : ms;   // the very last frame is often blank
    const target = Math.min(ms, limit) / 1000;
    if (video.readyState >= 2 && Math.abs(video.currentTime - target) < 0.01 && !video.seeking) { resolve(mine === seekSeq); return; }
    let settled = false;
    const done = () => { if (settled) return; settled = true; video.removeEventListener('seeked', done); resolve(mine === seekSeq); };
    video.addEventListener('seeked', done);
    setTimeout(done, 4000);
    try { video.currentTime = target; } catch (e) { done(); }
  });
}
/** The chosen frame becomes the still: ck.photo (what the stylist judges) and ck.previewUrl (the clip's poster). */
async function captureFrame(video, ms) {
  const ck = state.check;
  const mine = ++captureSeq;
  capturing = true;
  updateSubmit();
  try {
    const landed = await seekVideo(video, ms);
    if (!landed || mine !== captureSeq || !document.contains(video)) return;
    const blob = await frameToJpeg(video);
    if (mine !== captureSeq || video.src !== ck.clipUrl) return;   // the clip changed under us
    if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
    ck.photo = blob; ck.previewUrl = URL.createObjectURL(blob);
  } catch (e) {
    if (mine === captureSeq) showError(t('error.video_read'));
  } finally {
    if (mine === captureSeq) { capturing = false; updateSubmit(); }
  }
}

function updateSubmit() {
  const submit = $('submit');
  const ck = state.check;
  if (submit) submit.disabled = !(ck.intent && ck.photo) || ck.busy || ck.photoBusy || capturing;
}
function showError(message) {
  const node = $('check-error');   // looked up fresh: the view may have been re-rendered during a decode
  if (node) { node.textContent = message; node.hidden = false; } else state.check.error = message;
}
function clearError() { const node = $('check-error'); if (node) node.hidden = true; }
/** Drops the photo and the clip (and their URLs); the token retires any decode still running. */
function clearMedia(ck) {
  ck.photoToken += 1;
  if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
  if (ck.clipUrl) URL.revokeObjectURL(ck.clipUrl);
  ck.photo = null; ck.previewUrl = null; ck.clip = null; ck.clipUrl = null; ck.clipMs = 0; ck.source = null; ck.photoBusy = false;
  frameMs = null; capturing = false; captureSeq += 1; seekSeq += 1;
}

/** The photo button: the camera, a photo from the library, or a clip from the library. */
function chooseMedia() {
  if (state.check.busy) return;
  const list = el('div', { class: 'sheet-list' });
  const s = sheet({ title: t(state.check.previewUrl || state.check.clipUrl ? 'check.media_replace' : 'check.media_add'), content: list });
  // Closing first, then acting: the picker's input.click() must run inside the tap that chose the row.
  const row = (id, name, text, onclick) => el('button', { type: 'button', id, onclick: () => { s.close(); onclick(); } }, [icon(name), text]);
  // LEAD: camera.js sends a signed-out visitor back to #/check; drop this guard when the camera opens to guests.
  if (state.me) list.appendChild(row('media-camera', 'camera', t('check.open_camera'), () => { cameraReturn.fromCheck = true; navigate('#/camera'); }));
  list.appendChild(row('media-library', 'image', t('check.from_library'), async () => { const file = await pickFile('file'); if (file) takePhotoFile(file); }));
  list.appendChild(row('media-clip', 'clip', t('camera.clip') + ' · ' + t('check.from_library'), async () => { const file = await pickFile('clip-file'); if (file) takeClipFile(file); }));
}

/**
 * A photo from the library (or the camera's fallback): downscaled before it is kept. photoToken guards the race: a second
 * pick, a sign-out or "check another" while the first decode is still running makes the first result land nowhere.
 */
export async function takePhotoFile(file) {
  const ck = state.check;
  clearError();
  clearMedia(ck);
  const token = ck.photoToken;
  busyKind = 'photo'; ck.photoBusy = true;
  renderPhoto(); updateSubmit();
  let blob = null;
  try { blob = await prepareImage(file, MAX_EDGE); } catch (e) { blob = null; }
  // prepareImage hands back the original when it cannot decode it; a file that is not an image is no use to anyone.
  if (blob === file && file.type && !file.type.startsWith('image/')) blob = null;
  if (token !== ck.photoToken) return false;
  ck.photoBusy = false;
  if (blob) { ck.photo = blob; ck.previewUrl = URL.createObjectURL(blob); ck.source = 'library'; }
  else showError(t('error.image_read'));
  renderPhoto(); updateSubmit();
  return !!blob;
}
/** A clip from the library: within the byte cap, readable, within the seconds cap. Then the frame picker, 40% in. */
export async function takeClipFile(file) {
  const ck = state.check;
  clearError();
  clearMedia(ck);
  const token = ck.photoToken;
  busyKind = 'clip'; ck.photoBusy = true;
  renderPhoto(); updateSubmit();
  const maxSeconds = state.config.maxVideoSeconds;
  let problem = null; let ms = 0; let url = null;
  if (file.size > state.config.maxVideoBytes) problem = t('check.clip_too_large', { mb: fmtNumber(Math.round(state.config.maxVideoBytes / (1024 * 1024))) });
  else {
    url = URL.createObjectURL(file);
    try { ms = await readClipDuration(url); } catch (e) { problem = t('error.video_read'); }
    if (!problem && ms > maxSeconds * 1000 + 500) problem = t('check.clip_too_long', { s: seconds(ms), max: fmtNumber(maxSeconds) });
  }
  if (token !== ck.photoToken) { if (url) URL.revokeObjectURL(url); return false; }
  ck.photoBusy = false;
  if (problem) { if (url) URL.revokeObjectURL(url); showError(problem); renderPhoto(); updateSubmit(); return false; }
  ck.clip = file; ck.clipUrl = url; ck.clipMs = Math.round(ms); ck.source = 'library';
  frameMs = preselectMs(ck.clipMs);
  renderPhoto(); updateSubmit();
  return true;
}
/** The camera hands over a photo (a JPEG blob) or a clip (blob + its length); a clip's first frame is preselected. */
export function receiveCapture(capture) {
  const ck = state.check;
  clearMedia(ck);
  ck.error = null;
  if (capture.clip) { ck.clip = capture.clip; ck.clipUrl = URL.createObjectURL(capture.clip); ck.clipMs = Math.round(capture.clipMs || 0); frameMs = 0; }
  else { ck.photo = capture.photo; ck.previewUrl = URL.createObjectURL(capture.photo); }
  ck.source = 'camera';
}
/** The clip's length from its metadata, in ms. Rejects what the browser cannot read. */
function readClipDuration(url) {
  return new Promise((resolve, reject) => {
    const probe = document.createElement('video');
    probe.preload = 'metadata'; probe.muted = true; probe.playsInline = true;
    let settled = false;
    const finish = (ok, value) => { if (settled) return; settled = true; probe.removeAttribute('src'); probe.load(); if (ok) resolve(value); else reject(new Error('video')); };
    probe.addEventListener('error', () => finish(false), { once: true });
    probe.addEventListener('loadedmetadata', () => {
      if (isFinite(probe.duration) && probe.duration > 0) { finish(true, probe.duration * 1000); return; }
      // no duration in the container (a recorded webm): a far seek makes the browser find it
      probe.addEventListener('durationchange', () => { if (isFinite(probe.duration) && probe.duration > 0) finish(true, probe.duration * 1000); });
      setTimeout(() => finish(false), 5000);
      probe.currentTime = 1e101;
    }, { once: true });
    setTimeout(() => finish(false), 15000);
    probe.src = url;
  });
}
function removeClip() {
  clearError();
  clearMedia(state.check);
  renderPhoto(); updateSubmit();
}
/** The upload name tells the server what to expect; the bytes are what it trusts. */
function clipName(blob) { return /mp4|quicktime/i.test(blob.type || '') ? 'clip.mp4' : 'clip.webm'; }

async function submitCheck() {
  const ck = state.check;
  if (!ck.intent || !ck.photo || ck.busy || ck.photoBusy || capturing) return;
  ck.busy = true;
  updateSubmit();
  const root = view();
  root.innerHTML = '';
  root.appendChild(loadingBlock());
  announce(t('loading.line'));
  focusHeading();
  const wasSignedIn = !!state.me;
  try {
    const form = new FormData();
    form.append('intent', ck.intent);
    form.append('occasion', ck.occasion.trim());
    form.append('language', getLocale());
    form.append('image', ck.photo, 'outfit.jpg');
    if (ck.clip) form.append('video', ck.clip, clipName(ck.clip));   // the still stays the judged image; the clip is posted with the look
    state.result = await api('POST', '/api/checks', form);
    // A guest's check: the server named it by the guest cookie; it becomes the account's once the person signs up (the result screen claims it).
    state.result.guest = !wasSignedIn;
    state.resultAnimated = false;
    state.resultPostId = null;
    ck.busy = false;
    if (wasSignedIn) loadMe();   // streak and today's count may have moved
    navigate('#/result');
  } catch (e) {
    ck.busy = false;
    ck.error = e && e.status === 401 && wasSignedIn ? null : (e && e.message ? e.message : t('error.generic'));   // a lost session already re-rendered
    navigate('#/check');
  }
}

/** Back to a fresh check. "Try another photo" keeps the challenge; "Check another" drops it. */
function checkAnother(keepChallenge) {
  const ck = state.check;
  state.result = null; state.resultPostId = null; state.resultAnimated = false;
  clearMedia(ck);
  ck.error = null;
  if (!keepChallenge) ck.challenge = null;
  navigate('#/check');
}

// ---------- result ----------

register('result', async (root) => {
  const result = state.result;
  if (!result) { redirect('#/check'); return; }
  setTopBar({ title: t('result.title') });
  const feedback = result.feedback || {};
  const status = feedback.status || result.status;
  const intent = intentLabel(result.intent);
  const container = el('div', { id: 'result', class: 'stack' });
  root.appendChild(container);

  if (status !== 'ok') {
    const rejected = status === 'rejected';
    if (!state.resultAnimated) announce(t(rejected ? 'result.rejected_title' : 'result.not_outfit_title'));
    state.resultAnimated = true;
    container.appendChild(el('div', { class: 'state' }, [
      el('h1', { text: t(rejected ? 'result.rejected_title' : 'result.not_outfit_title') }),
      el('p', { class: 'lede', style: 'margin-block-start: 12px;', text: (!rejected && feedback.message) || t(rejected ? 'result.rejected_body' : 'result.not_outfit_body') }),
      el('button', { type: 'button', class: 'btn', style: 'margin-block-start: 24px;', text: t('result.try_again'), onclick: () => checkAnother(true) })
    ]));
    return;
  }

  const animate = !state.resultAnimated && !reducedMotion();
  if (!state.resultAnimated) announce(t('a11y.score', { score: fmtNumber(feedback.score) }) + '. ' + feedback.headline);
  state.resultAnimated = true;
  const scoreNode = el('span', { class: 'score', text: animate ? fmtNumber(0) : fmtNumber(feedback.score) });
  const fill = el('div', { class: 'bar-fill', style: animate ? 'inline-size: 0%;' : 'transition: none; inline-size: ' + feedback.intentMatch + '%;' });

  container.appendChild(el('div', {}, [
    el('div', { class: 'hero', role: 'img', 'aria-label': t('a11y.score', { score: fmtNumber(feedback.score) }) }, [
      scoreNode, el('span', { class: 'score-out', 'aria-hidden': 'true', text: t('result.out_of') })
    ]),
    el('h1', { class: 'result-headline', text: feedback.headline, style: 'margin-block-start: 16px;' }),
    feedback.vibe ? el('p', { class: 'vibe', text: feedback.vibe }) : null
  ]));
  // Rubric v2: the three rings, then the accessories read. A check from before v2 has neither and shows neither.
  if (feedback.breakdown) {
    container.appendChild(el('div', { id: 'breakdown' }, [
      el('h2', { text: t('result.breakdown') }),
      el('div', { style: 'margin-block-start: 12px;' }, [breakdownRow(feedback.breakdown)])
    ]));
  }
  if (feedback.accessories) container.appendChild(accessoriesSection(feedback.accessories));
  container.appendChild(el('div', {}, [
    el('div', { class: 'match-label' }, [el('span', { text: t('result.intent_match', { intent }) }), el('span', { text: fmtPercent(feedback.intentMatch / 100) })]),
    el('div', { class: 'bar', role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(feedback.intentMatch), 'aria-label': t('result.intent_match', { intent }) }, [fill])
  ]));
  if (feedback.items && feedback.items.length) {
    container.appendChild(el('div', {}, [
      el('h2', { text: t('result.items') }),
      el('ul', { class: 'items', style: 'margin-block-start: 4px;' }, feedback.items.map((item) => el('li', {}, [
        el('span', { class: 'dot ' + item.verdict, 'aria-hidden': 'true' }),
        el('div', {}, [
          el('div', { class: 'item-name' }, [item.name, el('span', { class: 'item-verdict', text: t('verdict.' + item.verdict) })]),
          item.note ? el('div', { class: 'item-note', text: item.note }) : null
        ])
      ])))
    ]));
  }
  if (feedback.working && feedback.working.length) {
    container.appendChild(el('div', {}, [
      el('h2', { text: t('result.working') }),
      el('ul', { class: 'working', style: 'margin-block-start: 6px;' }, feedback.working.map((line) => el('li', { text: line })))
    ]));
  }
  if (feedback.oneTip) {
    container.appendChild(el('div', {}, [el('h2', { text: t('result.tip') }), el('div', { class: 'tip', style: 'margin-block-start: 8px;' }, [el('p', { text: feedback.oneTip })])]));
  }

  const postArea = el('div');
  container.appendChild(postArea);
  renderPostArea(postArea, result);
  container.appendChild(el('div', { class: 'row' }, [
    shareCardButton(lookFromCheck(result, state.check.previewUrl)),
    el('button', { type: 'button', class: 'btn btn-secondary', onclick: () => shareResult(result) }, [icon('share'), t('result.share')])
  ]));
  container.appendChild(el('button', { type: 'button', class: 'btn btn-ghost', text: t('result.again'), onclick: () => checkAnother(false) }));

  if (animate) {
    requestAnimationFrame(() => { fill.style.inlineSize = feedback.intentMatch + '%'; });
    animateScore(scoreNode, feedback.score);
  }
});

/**
 * The accessories read: the verdict as a pill, the pieces the stylist saw as chips ("No accessories seen." when the list
 * is empty), the one-sentence note, and the one to add as a tip block when the stylist named one.
 */
function accessoriesSection(acc) {
  const verdict = ACCESSORY_VERDICTS.includes(acc.verdict) ? acc.verdict : 'neutral';
  const present = (Array.isArray(acc.present) ? acc.present : []).filter((piece) => typeof piece === 'string' && piece.trim());
  return el('section', { id: 'accessories', 'aria-labelledby': 'accessories-title' }, [
    el('h2', { id: 'accessories-title', text: t('accessories.title') }),
    el('div', { class: 'acc-body', style: 'margin-block-start: 12px;' }, [
      el('span', { class: 'acc-verdict ' + verdict, text: t('accessories.' + verdict) }),
      el('div', { class: 'acc-present' }, [
        el('span', { class: 'lbl', text: t('accessories.present') }),
        ...(present.length ? present.map((piece) => el('span', { class: 'chip', text: piece })) : [el('span', { class: 'none', text: t('accessories.none_seen') })])
      ]),
      acc.note ? el('p', { class: 'acc-note', text: acc.note }) : null,
      acc.addOne ? el('div', { class: 'tip acc-add' }, [el('span', { class: 'lbl', text: t('accessories.add_one') }), el('p', { text: acc.addOne })]) : null
    ])
  ]);
}

/**
 * "Post it" until the check is public, then the link to the look. A guest's check cannot be posted: "Sign up to keep it and
 * post it" takes them to join with #/result as the way back, and once they are signed in the check is claimed and refreshed
 * here, so Post it appears on the same result.
 */
function renderPostArea(area, result) {
  area.innerHTML = '';
  const postId = state.resultPostId || result.postId;
  if (postId) {
    area.appendChild(el('a', { class: 'btn', id: 'post-link', href: '#/post/' + encodeURIComponent(postId) }, [icon('check'), t('result.posted') + ' · ' + t('result.view_post')]));
    return;
  }
  if (result.guest) {
    if (!state.me) {
      area.appendChild(el('button', { type: 'button', class: 'btn', id: 'guest-keep', text: t('guest.keep'), onclick: () => requireSignIn('#/result', true) }));
      return;
    }
    // Signed in since: Post it shows disabled while the claim runs, then for real.
    area.appendChild(el('button', { type: 'button', class: 'btn', id: 'post-open', text: t('result.post'), disabled: true, 'aria-busy': 'true' }));
    claimGuestResult(area, result);
    return;
  }
  area.appendChild(el('button', { type: 'button', class: 'btn', id: 'post-open', text: t('result.post'), onclick: () => openPostSheet(area, result) }));
}

/**
 * The guest's check follows the person into the account: claim (idempotent; loadMe may have done it already), then read the
 * check back so its ownership and postId are the server's. Whatever happens, the result stops being a guest's after this:
 * a claim that did not land shows Post it anyway and the server's own answer says why when it is tapped.
 */
async function claimGuestResult(area, result) {
  if (result.claiming) return;
  result.claiming = true;
  let claimed = 0;
  try {
    claimed = await claimGuestChecks();
    const fresh = await api('GET', '/api/checks/' + encodeURIComponent(result.id));
    if (state.result !== result) return;
    Object.assign(result, fresh);
  } catch (e) {
    if (state.result !== result) return;
  } finally {
    result.claiming = false;
  }
  result.guest = false;
  if (claimed > 0) toast(t('guest.kept'));
  if (document.contains(area)) renderPostArea(area, result);
}

/**
 * The post sheet: caption, an open challenge of the same intent (the one the check was started from is preselected),
 * product links for brands. Posting makes the photo, intent, score, sub-scores and headline public; the tip, the items and
 * the accessories read stay private.
 */
function openPostSheet(area, result) {
  if (!requireSignIn('#/result')) return;
  // Entering a challenge is just its hashtag in the caption; the server links the look while the challenge is open.
  const pending = state.check.challenge;
  const caption = el('textarea', { id: 'caption', maxlength: '140', rows: '3', autocomplete: 'off', placeholder: t('result.caption_placeholder') });
  if (pending && pending.tag) caption.value = '#' + pending.tag + ' ';

  const productRows = [];
  let productsField = null;
  if (isBrand()) {
    const grid = el('div', { class: 'products-grid' });
    for (let i = 0; i < 3; i++) {
      const row = {
        label: el('input', { type: 'text', maxlength: '60', autocomplete: 'off', placeholder: t('result.product_label'), 'aria-label': t('result.product_label') }),
        url: el('input', { type: 'url', maxlength: '500', inputmode: 'url', autocapitalize: 'off', autocomplete: 'off', placeholder: t('result.product_url'), 'aria-label': t('result.product_url') }),
        price: el('input', { type: 'text', maxlength: '20', autocomplete: 'off', placeholder: t('result.product_price'), 'aria-label': t('result.product_price') })
      };
      productRows.push(row);
      grid.appendChild(row.label); grid.appendChild(row.url); grid.appendChild(row.price);
    }
    productsField = el('div', { class: 'field' }, [el('span', { class: 'label', text: t('result.products') }), grid]);
  }

  const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
  const confirm = el('button', { type: 'button', class: 'btn', id: 'post-confirm', text: t('result.confirm_post') });
  const cancel = el('button', { type: 'button', class: 'btn btn-ghost', text: t('common.cancel'), onclick: () => s.close() });
  // "After the tip": the caller's last looks to mark this one as a follow-up of (hidden until they are in; none for a first look).
  const after = afterPicker(result);
  const content = el('div', { class: 'stack' }, [
    el('p', { class: 'muted', text: t('result.post_intro') }),
    el('div', { class: 'field' }, [el('label', { for: 'caption', text: t('result.caption') }), caption, el('span', { class: 'hint', text: t('result.caption_hint') })]),
    after.node,
    productsField,
    error,
    el('div', { class: 'row' }, [confirm, cancel])
  ]);
  const s = sheet({ title: t('result.post_title'), content });

  confirm.addEventListener('click', async () => {
    confirm.disabled = true; error.hidden = true;
    const products = productRows
      .filter((r) => r.label.value.trim() || r.url.value.trim())
      .map((r) => ({ label: r.label.value.trim(), url: r.url.value.trim(), price: r.price.value.trim() || null }));
    try {
      const post = await api('POST', '/api/posts', { checkId: result.id, caption: caption.value, products, beforePostId: after.value() });
      state.resultPostId = post.id;
      result.postId = post.id;
      state.check.challenge = null;
      s.close();
      toast(t('result.posted'));
      renderPostArea(area, result);
      const link = $('post-link');
      if (link) link.focus({ preventScroll: true });
    } catch (e) {
      error.textContent = e && e.message ? e.message : t('error.generic');
      error.hidden = false;
      confirm.disabled = false;
    }
  });

  requestAnimationFrame(() => { if (document.contains(caption)) { caption.focus(); caption.setSelectionRange(caption.value.length, caption.value.length); } });
}

function animateScore(node, target) {
  const start = performance.now();
  let frame = 0;
  const tick = (now) => {
    const progress = Math.min(1, (now - start) / SCORE_COUNT_MS);
    const eased = 1 - Math.pow(1 - progress, 3);
    node.textContent = fmtNumber(Math.round(eased * target));
    if (progress < 1) frame = requestAnimationFrame(tick);
  };
  frame = requestAnimationFrame(tick);
  onLeave(() => { cancelAnimationFrame(frame); node.textContent = fmtNumber(target); });
}

async function shareResult(result) {
  if (state.sharing) return;
  state.sharing = true;
  try {
    const feedback = result.feedback || {};
    const text = t('result.share_text', { score: fmtNumber(feedback.score), intent: intentLabel(result.intent), tip: feedback.oneTip || '' });
    if (navigator.share) {
      try { await navigator.share({ text }); return; }
      catch (e) { if (e && (e.name === 'AbortError' || e.name === 'InvalidStateError')) return; }
    }
    await copyText(text, t('result.copied'));
  } finally { state.sharing = false; }
}
