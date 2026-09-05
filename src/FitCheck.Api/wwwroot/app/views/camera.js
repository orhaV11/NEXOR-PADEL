// The in-app camera (#/camera, #/camera/clip): a full-bleed viewfinder, the ring as the shutter (tap = photo, hold = clip),
// flip, a 3-second timer, a framing guide, then a preview with Retake / Use it. The capture goes to the check flow through
// check.js's receiveCapture: a photo as the still, a clip whose frame is picked on the check screen like a library clip's.
// When the camera cannot open (refused, or no getUserMedia), the native library picker takes over, through the same intake
// as the check screen's. Every track stops on leave, when the page hides, and before any navigation.
import {
  register, state, t, el, icon, iconButton, announce, toast, onLeave, pickFile, loadPrefs, savePrefs, reducedMotion, redirect, frameToJpeg, fmtNumber
} from '../core.js';
import { receiveCapture, takePhotoFile, takeClipFile, cameraReturn } from './check.js';

const HOLD_MS = 250;        // a press this long starts a clip
const MIN_CLIP_MS = 1000;   // a clip released before this keeps rolling to a second
const GUIDE_MS = 3000;      // the framing guide fades after this; a tap brings it back
const RING_R = 36;          // the 76px shutter ring: r 36, stroke 4
const RING_C = 2 * Math.PI * RING_R;
// The first container MediaRecorder can write, asked for with explicit codecs so isTypeSupported can refuse: H.264 mp4 on
// Safari and new Chrome (plays everywhere), else webm. Never a bare 'video/mp4': Chrome says yes to that and then writes
// VP9 into the mp4, a file iPhones cannot play.
const MIME_TYPES = ['video/mp4;codecs=avc1.42E01E,mp4a.40.2', 'video/mp4;codecs=avc1.42E01E', 'video/webm;codecs=vp9,opus', 'video/webm;codecs=vp8,opus', 'video/webm'];
const WEBM_TYPES = MIME_TYPES.filter((type) => type.startsWith('video/webm'));
const MP4 = /^video\/mp4/i;
const NOT_H264 = /vp0?9|vp8|av01/i;   // codecs an mp4 from this camera must never carry
let warnedCodec = false;

// The camera's own stylesheet. The shell (masthead, dock) steps out for this route; the stage is fixed and black.
const CSS = `
html[data-route="camera"] header.top, html[data-route="camera"] nav.tabbar { display: none; }
html[data-route="camera"] body { padding-block-end: 0; }
html[data-route="camera"] main { max-inline-size: none; min-block-size: 0; }
html[data-route="camera"] .view { padding: 0; }
.cam { position: fixed; inset: 0; z-index: 7; background: #000; color: #fff; overflow: hidden; -webkit-user-select: none; user-select: none; }
.cam-stage { position: absolute; inset: 0; background: #000; }
.cam-stage video, .cam-stage img { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; object-fit: cover; background: #000; }
.cam-stage.mirror > video { transform: scaleX(-1); }
.cam-stage.fit video, .cam-stage.fit img { object-fit: contain; }
.cam-flash { position: absolute; inset: 0; z-index: 4; background: #fff; opacity: 0; pointer-events: none; }
.cam-flash.on { animation: cam-flash 120ms ease-out; }
@keyframes cam-flash { 0% { opacity: 0.9; } 100% { opacity: 0; } }
/* the framing guide: a dotted rounded frame for a full-length look, dimmed edges, the hint under it */
.cam-guide { position: absolute; inset: 0; z-index: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; padding-block: calc(60px + var(--safe-t)) calc(206px + var(--safe-b)); padding-inline: 32px; pointer-events: none; transition: opacity 400ms ease; }
.cam-guide.hide { opacity: 0; }
.cam-guide .box { flex: none; block-size: min(62vh, 460px, 100%); max-block-size: calc(100% - 60px); aspect-ratio: 4 / 7; border: 2px dashed rgba(255, 255, 255, 0.78); border-radius: 30px; box-shadow: 0 0 0 200vmax rgba(0, 0, 0, 0.16); }
.cam-guide p { text-align: center; font-size: 14px; line-height: 1.4; font-weight: 500; color: #fff; text-shadow: 0 1px 8px rgba(0, 0, 0, 0.7); }
html[data-route="camera"] .toast { inset-block-end: calc(236px + var(--safe-b)); }   /* above the shutter, not on it: the dock it normally clears is hidden here */
.cam-top { position: absolute; inset-inline: 0; inset-block-start: 0; z-index: 3; display: flex; justify-content: space-between; align-items: center; padding-block: calc(8px + var(--safe-t)) 8px; padding-inline: 8px; }
.cam-top .end { display: flex; gap: 2px; }
.cam .icon-btn { color: #fff; background: rgba(0, 0, 0, 0.35); }
.cam .icon-btn:active { background: rgba(0, 0, 0, 0.6); }
.cam .icon-btn[aria-pressed="true"] { background: var(--accent); color: var(--accent-ink); }
.cam .icon-btn:disabled { opacity: 0.4; }
/* recording: a red dot and the seconds, under the top bar */
.cam-rec { position: absolute; inset-inline: 0; inset-block-start: calc(64px + var(--safe-t)); z-index: 3; display: flex; justify-content: center; pointer-events: none; }
.cam-rec span { display: inline-flex; align-items: center; gap: 8px; background: rgba(0, 0, 0, 0.55); border-radius: var(--pill); padding-block: 6px; padding-inline: 12px; font-size: 14px; font-weight: 600; }
.cam-rec i { inline-size: 10px; block-size: 10px; border-radius: 50%; background: #ff3b30; animation: cam-blink 1s steps(2, start) infinite; }
@keyframes cam-blink { to { opacity: 0.35; } }
.cam-count { position: absolute; inset: 0; z-index: 3; display: grid; place-items: center; font: 800 168px/1 var(--font-display); color: #fff; text-shadow: 0 6px 40px rgba(0, 0, 0, 0.65); pointer-events: none; direction: ltr; }
/* the bottom: mode pills, the shutter ring, a note; a soft dark fade so the white disc reads on a bright scene */
.cam-bottom { position: absolute; inset-inline: 0; inset-block-end: 0; z-index: 3; display: flex; flex-direction: column; align-items: center; gap: 12px; padding-block: 44px calc(18px + var(--safe-b)); background: linear-gradient(transparent, rgba(0, 0, 0, 0.55)); }
.cam-modes { display: flex; gap: 6px; }
.cam-modes button { min-block-size: 36px; min-inline-size: 72px; padding-inline: 14px; border: 0; border-radius: var(--pill); background: rgba(0, 0, 0, 0.55); color: #fff; font-weight: 700; font-size: 13px; }   /* the idle pill stays ≥ 4.5:1 on a bright scene */
.cam-modes button[aria-pressed="true"] { background: #fff; color: #000; }
.cam-modes button:disabled { opacity: 0.4; }
.cam-shutter { position: relative; inline-size: 88px; block-size: 88px; padding: 0; border: 0; border-radius: 50%; background: transparent; display: grid; place-items: center; touch-action: none; -webkit-touch-callout: none; }
.cam-shutter:disabled { opacity: 0.4; }
.cam-shutter svg { position: absolute; inset: 6px; inline-size: 76px; block-size: 76px; overflow: visible; transition: transform 160ms ease; }
.cam-shutter .disc { position: relative; inline-size: 58px; block-size: 58px; border-radius: 50%; background: #fff; transition: transform 140ms ease, border-radius 160ms ease, background 160ms ease; }
.cam-shutter:active .disc { transform: scale(0.92); }
.cam-shutter .track, .cam-shutter .progress { visibility: hidden; }
/* while a clip rolls: the disc turns into a red stop square and the ring draws itself clockwise toward the cap */
.cam-shutter.rec .ring { visibility: hidden; }
.cam-shutter.rec .track, .cam-shutter.rec .progress { visibility: visible; }
.cam-shutter.rec svg { transform: scale(1.1); }
.cam-shutter.rec .disc { background: #ff3b30; border-radius: 10px; transform: scale(0.55); }
.cam-note { display: flex; flex-direction: column; gap: 2px; min-block-size: 20px; padding-inline: 24px; text-align: center; font-size: 12.5px; line-height: 1.4; color: rgba(255, 255, 255, 0.78); text-shadow: 0 1px 6px rgba(0, 0, 0, 0.6); }
/* the preview: the capture on black (as it will be kept), Retake and Use it along the bottom */
.cam-preview { position: absolute; inset: 0; z-index: 5; background: #000; }
.cam-preview img, .cam-preview video { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; object-fit: contain; background: #000; }
.cam-preview-actions { position: absolute; inset-inline: 16px; inset-block-end: calc(18px + var(--safe-b)); display: flex; gap: 10px; }
.cam-preview-actions .btn { flex: 1; min-inline-size: 0; }
.cam-preview-actions .btn-secondary { color: #fff; border-color: rgba(255, 255, 255, 0.55); background: rgba(0, 0, 0, 0.4); }
/* starting, refused, unsupported: a message in the middle, the library as the way through */
.cam-state { position: absolute; inset: 0; z-index: 2; display: grid; place-items: center; padding: 24px; text-align: center; }
.cam-state .box { display: flex; flex-direction: column; align-items: center; gap: 16px; max-inline-size: 320px; }
.cam-state .box > .icon svg { inline-size: 40px; block-size: 40px; color: var(--accent); }
.cam-state p { font-size: 15px; line-height: 1.5; color: rgba(255, 255, 255, 0.85); }
.cam-state .btn { min-inline-size: 200px; }
@media (prefers-reduced-motion: reduce) { .cam-guide { transition: none; } .cam-rec i { animation: none; } }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

function svgEl(tag, attrs, children) {
  const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
  for (const [key, value] of Object.entries(attrs || {})) node.setAttribute(key, String(value));
  for (const child of children || []) node.appendChild(child);
  return node;
}
let ringSeq = 0;
/** The shutter ring: the gradient ring at rest; while recording a dim track and the gradient arc drawing clockwise from the top. */
function shutterRing() {
  const id = 'cam-grad-' + (++ringSeq);
  const circle = (cls, extra) => svgEl('circle', { class: cls, cx: 38, cy: 38, r: RING_R, fill: 'none', 'stroke-width': 4, ...extra });
  const progress = circle('progress', { stroke: 'url(#' + id + ')', 'stroke-linecap': 'round', 'stroke-dasharray': RING_C, 'stroke-dashoffset': RING_C, transform: 'rotate(-90 38 38)' });
  const svg = svgEl('svg', { viewBox: '0 0 76 76', 'aria-hidden': 'true', focusable: 'false' }, [
    svgEl('defs', {}, [svgEl('linearGradient', { id, x1: 0, y1: 0, x2: 1, y2: 1 }, [svgEl('stop', { offset: 0, 'stop-color': '#b39dff' }), svgEl('stop', { offset: 1, 'stop-color': '#ff8fb1' })])]),
    circle('ring', { stroke: 'url(#' + id + ')' }),
    circle('track', { stroke: 'rgba(255,255,255,0.28)' }),
    progress
  ]);
  return { svg, progress };
}

register('camera', async (root, params) => {
  ensureStyle();
  if (!state.me) { redirect('#/check'); return; }
  const cam = mountCamera(root, params.mode === 'clip' ? 'clip' : 'photo');
  onLeave(cam.destroy);
  await cam.open();
});

function mountCamera(root, initialMode) {
  const prefs = loadPrefs();
  let facing = prefs.cameraFacing === 'user' ? 'user' : 'environment';
  let mode = initialMode;
  let phase = 'starting';       // starting | live | countdown | recording | stopping | preview | blocked
  let stream = null; let mirrored = false; let hasAudio = false;
  let openSeq = 0;              // only the newest open() keeps the stream it asked for; older ones stop theirs on arrival
  let recorder = null; let chunks = []; let recStart = 0; let recFrame = 0; let recStopTimer = 0; let capTimer = 0; let recordedMs = 0; let lastShownSecond = -1;
  let holdTimer = 0; let countdownTimer = 0; let guideTimer = 0;
  let pressed = false; let pressStartedClip = false; let pointerHandled = false;
  let timerOn = false; let destroyed = false; let manyCameras = false;
  let capture = null;           // { kind: 'photo' | 'clip', blob, url, ms }

  // ---- the elements ----
  const video = el('video', { autoplay: true, muted: true, playsinline: true, 'webkit-playsinline': true, 'aria-hidden': 'true' });
  video.muted = true;
  const stage = el('div', { class: 'cam-stage', onclick: () => { if (phase === 'live') showGuide(); } }, [video]);
  const flash = el('div', { class: 'cam-flash', 'aria-hidden': 'true' });
  // The dotted frame is decorative; the hint under it stays in the accessibility tree (the box fades, the words remain).
  const guide = el('div', { class: 'cam-guide hide' }, [el('div', { class: 'box', 'aria-hidden': 'true' }), el('p', { text: t('camera.guide') })]);
  const count = el('div', { class: 'cam-count', 'aria-hidden': 'true', hidden: true });
  const closeBtn = iconButton('x', t('camera.close'), leave);
  const timerBtn = iconButton('timer', t('camera.timer'), () => setTimer(!timerOn), { 'aria-pressed': 'false' });
  const flipBtn = iconButton('flip', t('camera.flip'), flip, { hidden: true });
  const top = el('div', { class: 'cam-top' }, [closeBtn, el('div', { class: 'end' }, [timerBtn, flipBtn])]);
  const recText = el('b', { text: '' });
  const rec = el('div', { class: 'cam-rec', hidden: true }, [el('span', {}, [el('i', { 'aria-hidden': 'true' }), recText])]);
  const { svg: ring, progress } = shutterRing();
  const shutter = el('button', { type: 'button', class: 'cam-shutter', 'aria-label': t('camera.shutter'), disabled: true }, [ring, el('span', { class: 'disc', 'aria-hidden': 'true' })]);
  const modes = el('div', { class: 'cam-modes', role: 'group', 'aria-label': t('camera.title') }, ['photo', 'clip'].map((m) => el('button', {
    type: 'button', 'data-mode': m, 'aria-pressed': String(m === mode), text: t('camera.' + m), onclick: () => setMode(m)
  })));
  const note = el('div', { class: 'cam-note' });
  const bottom = el('div', { class: 'cam-bottom' }, [modes, shutter, note]);
  const preview = el('div', { class: 'cam-preview', hidden: true });
  const stateLayer = el('div', { class: 'cam-state', hidden: true });
  const cam = el('div', { class: 'cam' }, [
    el('h1', { class: 'sr-only', text: t('camera.title') }),
    stage, guide, flash, count, top, rec, bottom, preview, stateLayer
  ]);
  root.appendChild(cam);
  if (typeof MediaRecorder === 'undefined') modes.hidden = true;   // photos only where clips cannot be recorded
  setMode(mode);

  // ---- the stream ----
  async function open() {
    if (destroyed) return;
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { blocked('camera.unsupported'); return; }
    const seq = ++openSeq;
    setPhase('starting');
    let next;
    try { next = await getStream(); }
    catch (e) {
      if (destroyed || seq !== openSeq) return;   // a newer open() owns the screen now
      blocked(e && (e.name === 'NotAllowedError' || e.name === 'SecurityError' || e.name === 'PermissionDeniedError') ? 'camera.denied' : 'camera.unsupported');
      return;
    }
    // A stream nobody wants any more is stopped on the spot, or the camera light stays on: an older open() resolving late,
    // the view gone, a preview showing, or the page hidden (visibilitychange reopens).
    if (destroyed || seq !== openSeq || phase === 'preview' || document.hidden) { for (const track of next.getTracks()) track.stop(); return; }
    stopStream();
    stream = next;
    const track = stream.getVideoTracks()[0];
    const settings = track && track.getSettings ? track.getSettings() : {};
    mirrored = (settings.facingMode || 'user') === 'user';   // a webcam with no facing reported faces the person
    hasAudio = stream.getAudioTracks().length > 0;
    stage.classList.toggle('mirror', mirrored);
    video.srcObject = stream;
    try { await video.play(); } catch (e) { /* muted autoplay is allowed; a refusal still shows frames on most browsers */ }
    if (destroyed || seq !== openSeq) { if (stream === next) stopStream(); return; }
    setPhase('live');
    paintNote();
    showGuide();
    countCameras();
  }
  async function getStream() {
    const videoConstraints = { facingMode: { ideal: facing }, width: { ideal: 1920 }, height: { ideal: 1080 } };
    try { return await navigator.mediaDevices.getUserMedia({ video: videoConstraints, audio: true }); }
    catch (e) {
      // No microphone, or one that was refused: the clip is silent and the camera still opens. A refused camera fails again here.
      if (e && ['NotAllowedError', 'NotFoundError', 'NotReadableError', 'OverconstrainedError', 'SecurityError', 'AbortError'].includes(e.name)) {
        return await navigator.mediaDevices.getUserMedia({ video: videoConstraints, audio: false });
      }
      throw e;
    }
  }
  function stopStream() {
    if (stream) for (const track of stream.getTracks()) track.stop();
    stream = null;
    video.srcObject = null;
  }
  async function countCameras() {
    if (!navigator.mediaDevices.enumerateDevices) return;
    try {
      const devices = await navigator.mediaDevices.enumerateDevices();
      manyCameras = devices.filter((d) => d.kind === 'videoinput').length > 1;
      if (!destroyed) flipBtn.hidden = !manyCameras || phase === 'blocked';
    } catch (e) { /* the flip stays hidden */ }
  }
  async function flip() {
    if (phase !== 'live') return;
    facing = facing === 'user' ? 'environment' : 'user';
    savePrefs({ cameraFacing: facing });
    stopStream();
    await open();
  }

  // ---- phases and chrome ----
  function setPhase(next) {
    phase = next;
    cam.dataset.phase = next;
    const live = next === 'live' || next === 'countdown' || next === 'recording';
    const busy = next === 'recording' || next === 'stopping';   // stopping: the recorder is flushing its last chunk; a second tap would cut the clip
    shutter.disabled = !live;
    top.hidden = next === 'preview';
    bottom.hidden = next === 'preview' || next === 'blocked';
    rec.hidden = !busy;
    preview.hidden = next !== 'preview';
    stateLayer.hidden = next !== 'starting' && next !== 'blocked';
    if (next === 'starting') stateLayer.replaceChildren(el('div', { class: 'box' }, [el('span', { class: 'loading-mark', 'aria-hidden': 'true', style: 'margin: 0;' }), el('p', { text: t('camera.starting') })]));
    for (const button of modes.children) button.disabled = busy || next === 'countdown';
    timerBtn.hidden = next === 'blocked';
    timerBtn.disabled = busy;
    flipBtn.hidden = next === 'blocked' || !manyCameras;
    flipBtn.disabled = next !== 'live';
    if (next !== 'live') hideGuide();
  }
  function blocked(key) {
    stopStream();
    setPhase('blocked');
    const library = el('button', { type: 'button', class: 'btn', text: t('camera.library'), onclick: () => fromLibrary(library) });
    stateLayer.replaceChildren(el('div', { class: 'box' }, [icon('camera'), el('p', { text: t(key) }), modes.hidden ? null : modes, library]));
    announce(t(key));   // the message is on screen; the live region says it to assistive tech, which the state layer alone would not
  }
  function setMode(next) {
    mode = next;
    for (const button of modes.children) button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
    shutter.setAttribute('aria-label', t(mode === 'clip' ? 'camera.record' : 'camera.shutter'));
    paintNote();
  }
  function setTimer(on) {
    timerOn = on;
    timerBtn.setAttribute('aria-pressed', String(on));
    timerBtn.setAttribute('aria-label', t(on ? 'camera.timer_on' : 'camera.timer'));
    announce(t(on ? 'camera.timer_on' : 'camera.timer'));   // the lit button is the visible state; the live region says it
  }
  function paintNote() {
    note.innerHTML = '';
    if (mode === 'clip' && !modes.hidden) note.appendChild(el('span', { text: t('camera.clip_max', { s: fmtNumber(state.config.maxVideoSeconds) }) }));
    if (mirrored && stream) note.appendChild(el('span', { text: t('camera.mirror_note') }));
  }
  function showGuide() {
    clearTimeout(guideTimer);
    guide.classList.remove('hide');
    guideTimer = setTimeout(() => guide.classList.add('hide'), GUIDE_MS);
  }
  function hideGuide() { clearTimeout(guideTimer); guideTimer = 0; guide.classList.add('hide'); }

  // ---- the shutter: tap = photo (clip mode: start a hands-free clip), hold = clip while held, any release stops ----
  shutter.addEventListener('contextmenu', (event) => event.preventDefault());
  shutter.addEventListener('pointerdown', (event) => {
    if (event.button !== 0 || shutter.disabled) return;
    event.preventDefault();
    pointerHandled = true;
    if (phase === 'countdown') { cancelCountdown(); return; }
    pressed = true; pressStartedClip = false;
    try { shutter.setPointerCapture(event.pointerId); } catch (e) { /* a mouse without capture still works */ }
    if (phase === 'live' && canRecord()) {
      holdTimer = setTimeout(() => { holdTimer = 0; if (pressed && phase === 'live') { pressStartedClip = true; startRecording(); } }, HOLD_MS);
    }
  });
  const release = () => {
    setTimeout(() => { pointerHandled = false; }, 0);   // the click, when one follows, is dispatched before this runs
    if (!pressed) return;
    pressed = false;
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; }
    if (phase === 'recording') { stopRecording(false); return; }   // a held clip let go, or the tap that ends a hands-free one
    if (phase === 'live' && !pressStartedClip) tap();
  };
  shutter.addEventListener('pointerup', release);
  shutter.addEventListener('pointercancel', release);
  // Keyboard and assistive tech arrive as a click with no pointer sequence before it.
  shutter.addEventListener('click', (event) => {
    event.preventDefault();
    if (pointerHandled) { pointerHandled = false; return; }
    if (phase === 'recording') stopRecording(false);
    else if (phase === 'countdown') cancelCountdown();
    else if (phase === 'live') tap();
  });
  function tap() {
    const action = mode === 'clip' && canRecord() ? startRecording : takePhoto;
    if (timerOn) startCountdown(action); else action();
  }
  const canRecord = () => typeof MediaRecorder !== 'undefined' && !!stream;
  const maxMs = () => Math.max(1000, (state.config.maxVideoSeconds || 30) * 1000);

  function startCountdown(then) {
    setPhase('countdown');
    let n = 3;
    count.hidden = false; count.textContent = fmtNumber(n);
    announce(fmtNumber(n));
    const step = () => {
      n -= 1;
      if (n <= 0) { countdownTimer = 0; count.hidden = true; setPhase('live'); then(); return; }
      count.textContent = fmtNumber(n);
      announce(fmtNumber(n));
      countdownTimer = setTimeout(step, 1000);
    };
    countdownTimer = setTimeout(step, 1000);
  }
  function cancelCountdown() {
    clearTimeout(countdownTimer); countdownTimer = 0;
    count.hidden = true;
    if (phase === 'countdown') setPhase('live');
  }

  async function takePhoto() {
    if (phase !== 'live' || !video.videoWidth) return;
    if (!reducedMotion()) { flash.classList.remove('on'); void flash.offsetWidth; flash.classList.add('on'); }
    let blob;
    try { blob = await frameToJpeg(video); } catch (e) { if (!destroyed) toast(t('error.image_read')); return; }
    if (destroyed || phase !== 'live') return;
    showPreview({ kind: 'photo', blob, url: URL.createObjectURL(blob) });
  }

  function pickMime(types) {
    for (const type of types) { try { if (MediaRecorder.isTypeSupported(type)) return type; } catch (e) { /* next */ } }
    return '';
  }
  /** A started recorder on the stream, or null when the browser cannot record. Its events count only while it is the current one. */
  function makeRecorder(type) {
    let r;
    try { r = new MediaRecorder(stream, type ? { mimeType: type, videoBitsPerSecond: 4000000 } : undefined); }
    catch (e) { return null; }
    r.addEventListener('dataavailable', (event) => { if (r === recorder && event.data && event.data.size) chunks.push(event.data); });
    r.addEventListener('stop', () => { if (r === recorder) onRecorded(); });
    r.addEventListener('error', () => { if (r === recorder && phase === 'recording') stopRecording(true); });
    try { r.start(250); } catch (e) { return null; }
    return r;
  }
  /** The container of a recorded clip: 'video/mp4' (H.264 by construction, what check.js names clip.mp4) or 'video/webm'. */
  const containerOf = (mime) => (MP4.test(mime || pickMime(MIME_TYPES)) ? 'video/mp4' : 'video/webm');
  function startRecording() {
    if (phase !== 'live' || !canRecord()) return;
    chunks = []; recordedMs = 0; lastShownSecond = -1;
    let next = makeRecorder(pickMime(MIME_TYPES));
    if (next && MP4.test(next.mimeType || '') && NOT_H264.test(next.mimeType)) {
      // The browser agreed to H.264 and is writing VP9 (or VP8, AV1) into the mp4 anyway: that file does not play on iPhones.
      // Start over in webm; a browser with no webm either cannot make a clip that travels, so it makes none.
      try { next.stop(); } catch (e) { /* never really started */ }
      const webm = pickMime(WEBM_TYPES);
      if (!warnedCodec) { warnedCodec = true; console.warn('camera: MediaRecorder chose ' + next.mimeType + '; ' + (webm ? 'recording ' + webm + ' instead' : 'no webm to fall back to')); }
      next = webm ? makeRecorder(webm) : null;
    }
    if (!next) { recorder = null; toast(t('camera.unsupported')); return; }
    recorder = next;
    recStart = performance.now();
    setPhase('recording');
    shutter.classList.add('rec');
    shutter.setAttribute('aria-label', t('camera.stop'));
    tickRecording();
    capTimer = setTimeout(() => stopRecording(true), maxMs() + 100);   // the ring's rAF loop enforces the cap; this stands in when frames are throttled
    announce(t('camera.recording', { s: fmtNumber(0) }));
  }
  function tickRecording() {
    const elapsed = performance.now() - recStart;
    const max = maxMs();
    progress.setAttribute('stroke-dashoffset', String(RING_C * (1 - Math.min(1, elapsed / max))));
    const second = Math.floor(elapsed / 1000);
    if (second !== lastShownSecond) { lastShownSecond = second; recText.textContent = t('camera.recording', { s: fmtNumber(second) }); }
    if (elapsed >= max) { stopRecording(true); return; }
    recFrame = requestAnimationFrame(tickRecording);
  }
  function clearRecTimers() {
    clearTimeout(recStopTimer); recStopTimer = 0;
    clearTimeout(capTimer); capTimer = 0;
    cancelAnimationFrame(recFrame); recFrame = 0;
  }
  function stopRecording(force) {
    if (phase !== 'recording' || !recorder) return;   // 'stopping' lands here too: the recorder is flushing, and a second stop would truncate the clip
    const elapsed = performance.now() - recStart;
    if (!force && elapsed < MIN_CLIP_MS) {   // too short to be a clip: keep rolling to a second, then stop
      if (!recStopTimer) recStopTimer = setTimeout(() => { recStopTimer = 0; stopRecording(true); }, MIN_CLIP_MS - elapsed);
      return;
    }
    clearRecTimers();
    recordedMs = Math.round(elapsed);
    setPhase('stopping');   // the shutter is off until the 'stop' event has been handled
    if (recorder.state === 'inactive') { onRecorded(); return; }
    try { recorder.stop(); } catch (e) { onRecorded(); }
  }
  /** The one end of a recording: after stop(), or when the recorder stopped on its own (a track ended, an error). */
  function onRecorded() {
    if (!recorder) return;   // already handled (a stop event after a direct call, or a recorder stopped on the way out)
    clearRecTimers();
    // A recorder that stopped by itself never went through stopRecording: the clock is the duration then.
    const ms = Math.min(recordedMs || Math.round(performance.now() - recStart), maxMs());
    const blob = new Blob(chunks, { type: containerOf(recorder.mimeType) });
    recorder = null; chunks = [];
    shutter.classList.remove('rec');
    progress.setAttribute('stroke-dashoffset', String(RING_C));
    setMode(mode);   // the shutter's label back from "Stop"
    if (destroyed) return;
    announce(t('camera.stop'));
    if (!blob.size) { setPhase('live'); showGuide(); toast(t('error.video_read')); return; }
    showPreview({ kind: 'clip', blob, url: URL.createObjectURL(blob), ms });
  }

  // ---- the preview ----
  function showPreview(next) {
    capture = next;
    setPhase('preview');
    const media = next.kind === 'photo'
      ? el('img', { src: next.url, alt: t('a11y.photo_preview') })
      : el('video', { src: next.url, autoplay: true, muted: true, loop: true, playsinline: true, 'webkit-playsinline': true, 'aria-label': t('a11y.photo_preview') });
    if (media.tagName === 'VIDEO') { media.muted = true; media.loop = true; }
    const use = el('button', { type: 'button', class: 'btn', id: 'cam-use', text: t('camera.use'), onclick: useCapture });
    const retake = el('button', { type: 'button', class: 'btn btn-secondary', id: 'cam-retake', text: t('camera.retake'), onclick: retakeCapture });
    preview.replaceChildren(media, el('div', { class: 'cam-preview-actions' }, [retake, use]));
    if (media.tagName === 'VIDEO') { const p = media.play(); if (p && p.catch) p.catch(() => {}); }
    requestAnimationFrame(() => { if (!destroyed && phase === 'preview') use.focus({ preventScroll: true }); });
  }
  function dropCapture() {
    const c = capture; capture = null;
    if (c) URL.revokeObjectURL(c.url);
    preview.replaceChildren();
  }
  async function retakeCapture() {
    dropCapture();
    if (stream) { setPhase('live'); showGuide(); } else await open();
  }
  function useCapture() {
    if (!capture) return;
    const c = capture; capture = null;
    preview.replaceChildren();
    if (c.kind === 'photo') receiveCapture({ photo: c.blob }); else receiveCapture({ clip: c.blob, clipMs: c.ms });
    URL.revokeObjectURL(c.url);   // receiveCapture made its own URL
    leave();
  }

  // ---- the library, when the camera cannot open ----
  async function fromLibrary(button) {
    const wantClip = mode === 'clip' && !modes.hidden;
    const file = await pickFile(wantClip ? 'clip-file' : 'file');
    if (!file || destroyed) return;
    button.disabled = true;
    await (wantClip ? takeClipFile(file) : takePhotoFile(file));   // a problem lands in state.check.error; the check screen shows it
    if (destroyed) return;
    leave();
  }

  // ---- leaving: the tracks stop before the navigation, not after it ----
  function leave() {
    destroy();
    const back = cameraReturn.fromCheck;
    cameraReturn.fromCheck = false;
    if (back && history.length > 1) history.back(); else redirect('#/check');
  }
  const onVisibility = () => {
    if (document.hidden) {
      if (phase === 'recording') stopRecording(true);
      if (phase === 'countdown') cancelCountdown();
      stopStream();
      if (phase === 'live') setPhase('starting');
    } else if (!destroyed && phase === 'starting') open();
  };
  document.addEventListener('visibilitychange', onVisibility);
  window.addEventListener('pagehide', stopStream);
  function destroy() {
    destroyed = true;
    clearTimeout(holdTimer); clearTimeout(countdownTimer); clearTimeout(guideTimer);
    clearRecTimers();
    if (recorder && recorder.state !== 'inactive') { try { recorder.stop(); } catch (e) { /* already gone */ } }
    recorder = null;
    stopStream();
    dropCapture();
    document.removeEventListener('visibilitychange', onVisibility);
    window.removeEventListener('pagehide', stopStream);
  }

  return { open, destroy };
}
