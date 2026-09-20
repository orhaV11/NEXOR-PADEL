#!/usr/bin/env node
/*
 * OREVOSH — the episode renderer.
 *
 * One command turns a photo plus a verdict into a finished vertical video, and into a chroma-key
 * overlay that drops straight over the owner's own masked footage in CapCut on a phone.
 *
 *   node tools/brand/render-episode.js brand-kit/episodes/001-camel.json
 *   node tools/brand/render-episode.js --all brand-kit/episodes     every .json in the folder
 *   node tools/brand/render-episode.js 001-camel.json --cover-only  just the thumbnail, for a look
 *   node tools/brand/render-episode.js 001-camel.json --overlay     the green-screen version as well
 *
 * It writes <name>.mp4 and <name>-cover.png next to the JSON, prints the duration and the file size,
 * and EXITS NON-ZERO with a readable message when a photo is missing, a score is out of range, a
 * required field is absent, or a line of text does not fit. The fit is measured in the browser at
 * the size it will be rendered: an overflowing tip is the one defect that would put a broken video
 * on the account, so it fails instead of silently overflowing.
 *
 * A fifteen-second episode renders in well under a minute and a half. The browser is asked for each
 * frame over the DevTools protocol and the frame goes straight down a pipe into ffmpeg, which
 * encodes while the browser is already on the next one; nothing is written to disk on the way.
 * Over a photograph a frame is a JPEG at quality 95 (a 1080x1920 PNG cost three quarters of a
 * second to compress, the JPEG a few hundredths, and at 95 neither the brand gradient nor the look
 * bands — checked frame against frame, see brand-kit/episodes/README.md §7); on the chroma field a
 * frame stays PNG, because a flat field compresses fast anyway and the key relies on the green being
 * exactly rgb(0,176,64) in every frame.
 *
 * Extra switches, for while you are editing:
 *   --preview                a rough cut in seconds: 540x960, 15 fps, as <name>-preview.mp4, for
 *                            checking the timing and the words before the real render
 *   --probe 0,3.4,8.6,13     write those seconds as PNGs into the scratch folder and stop
 *   --debug-safe             draw the platform's unsafe rectangles over a --probe or a --cover-only
 *   --keep-frames            also write every frame into the scratch folder
 *   --crf 18                 override the encoder's quality (default 20, 16 for an overlay)
 *
 * The video is encoded as <name>.mp4.part beside its final name and renamed onto it only after
 * ffmpeg has exited 0 and the file has been probed, so a failure half-way never leaves a short,
 * playable <name>.mp4 behind and never replaces the previous good one; the cover is written the
 * same way. A render runs on up to three browser pages (one fewer than the machine has cores;
 * EPISODE_WORKERS=3 forces three), and the count is printed before the first frame is taken.
 *
 * Playwright is the browser test's copy in tools/e2e; the browser is CHROMIUM_PATH or
 * /opt/pw-browsers/chromium; ffmpeg and ffprobe come from PATH, and both are asked for before a
 * browser is launched. Nothing touches the network: the faces are the local OFL copies in
 * tools/brand/templates/fonts and the look is a local file. Scratch frames go to EPISODE_SCRATCH
 * (default <tmp>/orevosh-episode), never into the repository.
 */
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync, spawn } = require('child_process');

const REPO = path.resolve(__dirname, '../..');
const TEMPLATE = path.join(__dirname, 'templates/episode.html');
const W = 1080, H = 1920, FPS = 30;
const MAX_MB = 8;                                  /* keep every episode under 8 MB */
const SCRATCH = process.env.EPISODE_SCRATCH || path.join(os.tmpdir(), 'orevosh-episode');

/* playwright is the browser test's copy (tools/e2e); a worktree without its node_modules falls back
   to the main checkout, and PLAYWRIGHT_PATH overrides both. */
function loadPlaywright() {
  const tries = [process.env.PLAYWRIGHT_PATH, path.join(REPO, 'tools/e2e/node_modules/playwright'),
    '/home/user/NEXOR-PADEL/tools/e2e/node_modules/playwright', 'playwright'].filter(Boolean);
  for (const p of tries) { try { return require(p); } catch (e) { /* next */ } }
  fail('playwright not found; tried:\n  ' + tries.join('\n  '));
}

/* Files being written that have not yet earned their final name: <name>.mp4.part and the cover's
   .part. They are removed on every way out that is not a finished render, so a failure leaves the
   folder as it found it. (A Ctrl-C can still leave a .part behind; it is never mistaken for a video,
   and the next render overwrites it.) */
const PARTIALS = new Set();
function discardPartials() {
  for (const p of PARTIALS) { try { fs.rmSync(p, { force: true }); } catch (e) { /* best effort */ } }
  PARTIALS.clear();
}
process.on('exit', discardPartials);

function fail(msg) {
  discardPartials();
  console.error('\n  ' + String(msg).split('\n').join('\n  ') + '\n');
  process.exit(1);
}
function sh(cmd, args) { return execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 1 << 26 }); }

/* ffmpeg and ffprobe are asked for once, before a browser is launched, so a machine without them
   gets one readable line instead of a crash from the spawn half-way through the first render. */
function checkTools() {
  for (const tool of ['ffmpeg', 'ffprobe']) {
    try { execFileSync(tool, ['-version'], { stdio: 'ignore' }); }
    catch (e) {
      fail(tool + ' is not on PATH, and the renderer needs both ffmpeg and ffprobe (they ship together).\n' +
        'Install ffmpeg (apt install ffmpeg, brew install ffmpeg, or a build from ffmpeg.org), or put the\n' +
        'folder it is in on PATH, then run the command again.\n' +
        '(' + String(e.message || e).split('\n')[0] + ')');
    }
  }
}

/* ------------------------------------------------------------------ the arguments */
function parseArgs(argv) {
  const o = { files: [], all: null, coverOnly: false, overlay: false, probe: null,
    debugSafe: false, keepFrames: false, crf: null, preview: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--all') { o.all = argv[++i] || 'brand-kit/episodes'; }
    else if (a === '--cover-only') o.coverOnly = true;
    else if (a === '--overlay') o.overlay = true;
    else if (a === '--preview') o.preview = true;
    else if (a === '--debug-safe') o.debugSafe = true;
    else if (a === '--keep-frames') o.keepFrames = true;
    else if (a === '--probe') o.probe = String(argv[++i] || '').split(',').filter(Boolean).map(Number);
    else if (a === '--crf') o.crf = Number(argv[++i]);
    else if (a === '-h' || a === '--help') { usage(); process.exit(0); }
    else if (a.startsWith('--')) fail('unknown option ' + a + '\n\n' + usageText());
    else o.files.push(a);
  }
  return o;
}
function usageText() {
  return 'node tools/brand/render-episode.js <episode.json> [--overlay] [--cover-only]\n' +
         'node tools/brand/render-episode.js --all brand-kit/episodes\n\n' +
         'Options:\n' +
         '  --overlay        also render the chroma-key version, as <name>-overlay.mp4\n' +
         '  --cover-only     write only <name>-cover.png, no video\n' +
         '  --preview        a rough cut in seconds (540x960, 15 fps) as <name>-preview.mp4, to check\n' +
         '                   the timing and the words; the real render is the same command without it\n' +
         '  --probe 0,8.6    write those seconds as PNGs into the scratch folder and stop\n' +
         '  --debug-safe     draw the platform\'s unsafe rectangles (probe and cover only)\n' +
         '  --keep-frames    also write every frame into the scratch folder\n' +
         '  --crf 18         encoder quality (default 20; 16 for an overlay)';
}
function usage() { console.log('\n' + usageText() + '\n'); }

/* Accept a path, or just the file name of an episode in brand-kit/episodes. */
function resolveJson(arg) {
  const tries = [path.resolve(process.cwd(), arg), path.resolve(REPO, arg),
    path.resolve(REPO, 'brand-kit/episodes', arg), path.resolve(process.cwd(), arg + '.json'),
    path.resolve(REPO, 'brand-kit/episodes', arg + '.json')];
  for (const p of tries) { if (fs.existsSync(p) && fs.statSync(p).isFile()) return p; }
  fail('no episode file at "' + arg + '".\n' +
    'Looked in:\n  ' + tries.join('\n  ') + '\n' +
    'An episode is a .json file; brand-kit/episodes/README.md shows one.');
}

/* ------------------------------------------------------------------ validation */
/* Every message names the file, the field and what to do about it: he edits JSON, never code. */
const VARIANTS = ['verdict', 'versus', 'board', 'overlay'];

function need(where, obj, key, what) {
  if (obj[key] === undefined || obj[key] === null || obj[key] === '') {
    fail(where + ': "' + key + '" is missing. ' + what);
  }
  return obj[key];
}
function checkScore(where, v, key) {
  if (typeof v !== 'number' || !isFinite(v) || Math.round(v) !== v || v < 0 || v > 10) {
    fail(where + ': "' + key + '" is ' + JSON.stringify(v) +
      '. A score is a whole number from 0 to 10.');
  }
  return v;
}
function checkPhoto(where, jsonDir, rel) {
  const abs = path.resolve(jsonDir, rel);
  if (!fs.existsSync(abs)) {
    /* the hint is counted from the JSON's own folder, exactly as the path in the JSON is: from
       brand-kit/episodes it is ../../tools/…, from brand-kit/episodes/week-1 it is ../../../tools/…;
       a JSON outside the repository gets the absolute folder, which is the only readable answer there */
    const photosDir = path.join(REPO, 'tools/brand/templates/photos');
    const up = path.relative(REPO, jsonDir);
    const inside = up === '' || (!up.startsWith('..') && !path.isAbsolute(up));
    const photos = (inside ? (path.relative(jsonDir, photosDir) || '.') : photosDir).split(path.sep).join('/');
    fail(where + ': the photo "' + rel + '" does not exist.\n' +
      'Resolved to: ' + abs + '\n' +
      'Photo paths are relative to the episode JSON (this one is in ' + jsonDir + ').\n' +
      'From there the three supplied looks are at\n' +
      '  ' + photos + '/look-1-streetwear.jpg\n' +
      '  ' + photos + '/look-2-camel.jpg\n' +
      '  ' + photos + '/look-3-pink.jpg');
  }
  return abs;
}
function checkFocus(where, f) {
  if (f === undefined) return { x: 0.5, y: 0.5 };
  if (typeof f !== 'object' || typeof f.x !== 'number' || typeof f.y !== 'number' ||
      f.x < 0 || f.x > 1 || f.y < 0 || f.y > 1) {
    fail(where + ': "focus" must look like {"x": 0.5, "y": 0.35}, both between 0 and 1.');
  }
  return { x: f.x, y: f.y };
}

function loadEpisode(jsonPath, forceOverlay) {
  const name = path.basename(jsonPath);
  let raw;
  try { raw = fs.readFileSync(jsonPath, 'utf8'); }
  catch (e) { fail('cannot read ' + jsonPath + ': ' + e.message); }
  let d;
  try { d = JSON.parse(raw); }
  catch (e) { fail(name + ' is not valid JSON: ' + e.message + '\nA missing comma or a smart quote is the usual cause.'); }

  const where = name;
  const variant = need(where, d, 'variant', 'It is one of: ' + VARIANTS.join(', ') + '.');
  if (!VARIANTS.includes(variant)) {
    fail(where + ': "variant" is "' + variant + '". It is one of: ' + VARIANTS.join(', ') + '.');
  }
  d.lang = d.lang || 'en';
  if (d.lang !== 'en' && d.lang !== 'he') {
    fail(where + ': "lang" is "' + d.lang + '". It is "en" (the default) or "he".');
  }
  d.overlay = !!forceOverlay;
  const mode = (variant === 'overlay' || d.overlay) ? 'overlay' : 'photo';
  const dir = path.dirname(jsonPath);
  const needsPhoto = mode === 'photo';

  const shape = variant === 'versus' ? 'versus' : variant === 'board' ? 'board' : 'verdict';

  if (shape === 'verdict') {
    need(where, d, 'intent', 'It is the short caps word over the look, e.g. "DATE".');
    checkScore(where, need(where, d, 'score', 'It is the score out of 10.'), 'score');
    need(where, d, 'headline', 'It is the stylist\'s one-line verdict.');
    const b = need(where, d, 'breakdown', 'It looks like {"fit": 9, "color": 9, "accessories": 8}.');
    ['fit', 'color', 'accessories'].forEach(k => {
      if (b[k] === undefined) fail(where + ': "breakdown.' + k + '" is missing. All three of fit, color and accessories are needed.');
      checkScore(where, b[k], 'breakdown.' + k);
    });
    need(where, d, 'tip', 'It is the one tip — the longest thing on screen and the reason the post works.');
    if (needsPhoto) d._photoAbs = checkPhoto(where, dir, need(where, d, 'photo', 'It is the path to the look, relative to this file.'));
    d._focus = checkFocus(where, d.focus);
  } else if (shape === 'versus') {
    ['photoA', 'photoB'].forEach(k => {
      const s = need(where, d, k, 'It looks like {"photo": "...", "label": "A", "score": 8}.');
      if (typeof s !== 'object') fail(where + ': "' + k + '" must be an object with a photo, a label and a score.');
      if (!s.label) s.label = k === 'photoA' ? 'A' : 'B';
      checkScore(where, need(where + ' > ' + k, s, 'score', 'It is that look\'s score out of 10.'), k + '.score');
      if (needsPhoto) s._photoAbs = checkPhoto(where + ' > ' + k, dir, need(where + ' > ' + k, s, 'photo', 'It is the path to that look.'));
      s._focus = checkFocus(where + ' > ' + k, s.focus);
    });
  } else {
    const looks = need(where, d, 'looks', 'It is an array of exactly three looks, BEST FIRST: looks[0] is rank 1.');
    if (!Array.isArray(looks) || looks.length !== 3) {
      fail(where + ': "looks" holds ' + (Array.isArray(looks) ? looks.length : 'not an array') +
        '. The board takes exactly three, best first: looks[0] is rank 1, and the cut counts down 3, 2, 1.');
    }
    looks.forEach((L, i) => {
      const w2 = where + ' > looks[' + i + '] (rank ' + (i + 1) + ')';
      need(w2, L, 'intent', 'It is the short caps word for that look, e.g. "PARTY".');
      checkScore(w2, need(w2, L, 'score', 'It is that look\'s score out of 10.'), 'score');
      if (needsPhoto) L._photoAbs = checkPhoto(w2, dir, need(w2, L, 'photo', 'It is the path to that look.'));
      L._focus = checkFocus(w2, L.focus);
    });
  }

  if (d.cover_at !== undefined && (typeof d.cover_at !== 'number' || d.cover_at < 0)) {
    fail(where + ': "cover_at" must be a number of seconds inside the episode.');
  }
  if (d.credit !== undefined && typeof d.credit !== 'string') {
    fail(where + ': "credit" must be a line of text, e.g. "@handle".');
  }
  return d;
}

/* file:// URLs, so the render works with the network unplugged. */
function toUrl(abs) { return 'file://' + abs.split(path.sep).join('/'); }
function withUrls(d) {
  const c = JSON.parse(JSON.stringify(d, (k, v) => (k.startsWith('_') ? undefined : v)));
  c.overlay = d.overlay;
  if (d._photoAbs) { c._photoUrl = toUrl(d._photoAbs); c._focus = d._focus; }
  if (d.photoA) { c.photoA._photoUrl = d.photoA._photoAbs ? toUrl(d.photoA._photoAbs) : null; c.photoA._focus = d.photoA._focus; }
  if (d.photoB) { c.photoB._photoUrl = d.photoB._photoAbs ? toUrl(d.photoB._photoAbs) : null; c.photoB._focus = d.photoB._focus; }
  if (d.looks) d.looks.forEach((L, i) => { c.looks[i]._photoUrl = L._photoAbs ? toUrl(L._photoAbs) : null; c.looks[i]._focus = L._focus; });
  return c;
}

/* ------------------------------------------------------------------ the render */
/* A page in a context of its own (a context is a renderer process, which is what lets several of
   them photograph at once). The page is always 1080x1920: the template's own size, so every fit is
   measured at the size the real render uses; a --preview is scaled at capture, not at layout. */
async function openPage(browser, data, label) {
  const ctx = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  await page.goto(toUrl(TEMPLATE), { waitUntil: 'load' });
  let out;
  try { out = await page.evaluate(d => window.__build(d), data); }
  catch (e) { fail(label + ': the template could not lay this episode out.\n' + e.message); }
  if (errors.length) fail(label + ': the template threw.\n' + errors.join('\n'));

  if (out.missing.length) {
    fail(label + ': the look did not load.\n  ' + out.missing.join('\n  ') +
      '\nCheck the path in the JSON; it is relative to the JSON file.');
  }
  if (!out.faces.outfit || !out.faces.heebo) {
    fail(label + ': the local fonts did not load (' + JSON.stringify(out.faces) + ').\n' +
      'They live in tools/brand/templates/fonts and must sit beside episode.css.');
  }
  if (out.problems.length) {
    const lines = out.problems.map(pr =>
      '  ' + pr.what + ' does not fit: ' + pr.got + ' in a ' + pr.max + ' box\n' +
      '    "' + pr.text + '"\n' +
      '    ' + pr.hint);
    fail(label + ': text does not fit, so nothing was rendered.\n' + lines.join('\n') +
      '\nShorten the line in the JSON and run it again.');
  }
  return { page, ctx, meta: out.meta, crops: out.crops };
}

/* ------------------------------------------------------------------ the pipeline */
/* Two plans. The real one: 30 fps, full size, x264 at a slow preset so the file stays small at the
   given crf. The preview: 15 fps, halved to 540x960 by ffmpeg on the way in (a capture costs the same
   at any size, so the page is photographed as it is), the fastest preset there is, and a coarser
   JPEG — done in seconds, for checking the timing and the words, never for uploading. */
function plan(meta, opt) {
  if (opt.preview) {
    return { fps: 15, scale: 0.5, format: 'jpeg', quality: 80, preset: 'ultrafast', crf: 26, tag: '-preview' };
  }
  /* Over a photograph a JPEG at 95; on the chroma field a PNG, because the field is flat (so PNG is
     fast on it) and because the key wants the green at exactly rgb(0,176,64), which lossless keeps.
     An overlay is keyed on a phone, and keying is unforgiving of the mosquito noise h264 leaves
     around a hard edge, so it is also encoded finer than a normal episode. */
  const flat = meta.mode === 'overlay';
  return { fps: FPS, scale: 1, format: flat ? 'png' : 'jpeg', quality: 95, preset: 'slow',
    crf: opt.crf !== null ? opt.crf : (flat ? 16 : 20), tag: '' };
}

/* ffmpeg reading frames from its stdin. Silent on purpose: no music is licensed, and both platforms
   reward a sound picked inside their own app at upload. Nothing in an episode depends on audio —
   every word is on screen. */
function startEncoder(part, pl) {
  /* the output is <name>.mp4.part, so the muxer is named rather than read off the extension */
  const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error',
    '-f', 'image2pipe', '-framerate', String(pl.fps), '-c:v', pl.format === 'jpeg' ? 'mjpeg' : 'png', '-i', 'pipe:0',
    ...(pl.scale !== 1 ? ['-vf', 'scale=' + Math.round(W * pl.scale) + ':' + Math.round(H * pl.scale) + ':flags=area'] : []),
    '-c:v', 'libx264', '-preset', pl.preset, '-crf', String(pl.crf),
    '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-f', 'mp4', part], { stdio: ['pipe', 'ignore', 'pipe'] });
  let err = '', exited = null, settle;
  const done = new Promise(resolve => { settle = resolve; });
  ff.stderr.on('data', d => { err += d; });
  ff.stdin.on('error', () => { /* EPIPE when ffmpeg stops early; the exit code tells the story */ });
  ff.on('close', (code, signal) => {
    if (exited === null) exited = code === null ? -1 : code;
    settle({ code: code, signal: signal, err: err });
  });
  /* a spawn that fails outright (ffmpeg gone between the check and now, or not executable) raises
     'error' rather than 'close'; unhandled, it would throw out of the render instead of failing it */
  ff.on('error', e => {
    if (exited === null) exited = -1;
    settle({ code: null, signal: null, err: err + 'ffmpeg could not be run: ' + e.message });
  });
  return {
    done: done,
    /* back-pressure: wait for the pipe to drain rather than piling frames up in memory, and stop
       waiting if ffmpeg has gone away */
    write: buf => new Promise((resolve, reject) => {
      if (exited !== null) return reject(new Error('ffmpeg stopped early'));
      if (ff.stdin.write(buf)) return resolve();
      const onDrain = () => { ff.off('close', onClose); ff.off('error', onClose); resolve(); };
      const onClose = () => { ff.stdin.off('drain', onDrain); ff.off('close', onClose); ff.off('error', onClose); reject(new Error('ffmpeg stopped early')); };
      ff.stdin.once('drain', onDrain);
      ff.once('close', onClose);
      ff.once('error', onClose);
    }),
    end: () => ff.stdin.end(),
    /* once the render has failed there is nothing worth finishing: no faststart pass, no flush */
    abort: () => { if (exited === null) ff.kill('SIGKILL'); }
  };
}

/* How many pages photograph the episode at once. One capture costs about 65 ms whatever the format
   (the time is the compositor handing over the surface, not the encoding), but pages in separate
   contexts are separate renderer processes and three of them run at nearly three times the pace.
   One is kept back for ffmpeg. EPISODE_WORKERS overrides it. */
const WORKERS = Math.max(1, Math.min(3, Number(process.env.EPISODE_WORKERS) ||
  (os.cpus().length - 1)));
const LOOKAHEAD = 24;   /* frames a worker may run ahead of the writer: bounds the memory the pipe holds */

/* a one-shot latch: wait() resolves at the next fire() */
function latch() {
  let waiters = [];
  return { wait: () => new Promise(r => waiters.push(r)), fire: () => { const w = waiters; waiters = []; w.forEach(r => r()); } };
}

/* Seek, capture, pipe. Every frame is a DevTools Page.captureScreenshot straight off the compositor's
   surface — what page.screenshot does underneath, minus its per-call preparation — as a JPEG or a
   PNG as the plan says. `page` is the page the episode was validated on; the other workers are fresh
   contexts that build the same data, and each takes every K-th frame. A writer sends the frames to
   ffmpeg in order with back-pressure, so the encoder runs while the pages are on the next frames and
   nothing is written to disk on the way (unless --keep-frames asks for a copy). */
async function renderVideo(browser, page, data, label, meta, part, pl, keepDir) {
  const mp4 = part.replace(/\.part$/, '');
  const frames = Math.round(meta.duration * pl.fps);
  if (keepDir) { fs.rmSync(keepDir, { recursive: true, force: true }); fs.mkdirSync(keepDir, { recursive: true }); }
  const ext = pl.format === 'jpeg' ? '.jpg' : '.png';
  const shot = { format: pl.format, fromSurface: true, captureBeyondViewport: false };
  if (pl.format === 'jpeg') shot.quality = pl.quality; else shot.optimizeForSpeed = true;

  /* the pool: the validated page first, then the extra contexts */
  const t0 = Date.now();
  const workers = [{ page: page, ctx: null }];
  const extra = Math.min(WORKERS, frames) - 1;
  const opened = await Promise.all(Array.from({ length: extra }, async () => {
    const r = await openPage(browser, data, label);
    return { page: r.page, ctx: r.ctx };
  }));
  workers.push(...opened);
  for (const w of workers) w.cdp = await w.page.context().newCDPSession(w.page);
  const grab = async (w, t) => {
    await w.page.evaluate(tt => window.seek(tt), t);
    const r = await w.cdp.send('Page.captureScreenshot', shot);
    return Buffer.from(r.data, 'base64');
  };

  /* Every worker must photograph frame 0 byte for byte the same, or the cut would flicker between
     two renderings of the same picture. It never should differ — same files, same fonts, same
     flags — but if it does, the slow way is the right way and the pool drops to one page. */
  if (workers.length > 1) {
    const first = await Promise.all(workers.map(w => grab(w, 0)));
    const agree = first.every(b => b.equals(first[0]));
    if (!agree) {
      console.log('    the extra pages did not render frame 0 identically; rendering on one page');
      for (const w of workers.slice(1)) await w.ctx.close();
      workers.length = 1;
    }
  }
  const K = workers.length;
  console.log('    photographing on ' + K + (K === 1 ? ' page' : ' pages') + ', encoding to ' + path.basename(part));

  const enc = startEncoder(part, pl);
  const slots = new Map();
  let next = 0, failed = null;
  const arrived = latch(), advanced = latch();

  const worker = async (w, k) => {
    for (let f = k; f < frames && !failed; f += K) {
      while (f - next > LOOKAHEAD && !failed) await advanced.wait();
      if (failed) break;
      try { slots.set(f, await grab(w, f / pl.fps)); }
      catch (e) { failed = failed || e; }
      arrived.fire();
    }
    arrived.fire();
  };
  const writer = async () => {
    try {
      while (next < frames && !failed) {
        while (!slots.has(next) && !failed) await arrived.wait();
        if (failed) break;
        const buf = slots.get(next);
        slots.delete(next);
        if (keepDir) fs.writeFileSync(path.join(keepDir, 'frame-' + String(next).padStart(5, '0') + ext), buf);
        await enc.write(buf);
        if (next % 90 === 0) process.stdout.write('    frame ' + next + '/' + frames + '\n');
        next++;
        advanced.fire();
      }
    } catch (e) { failed = failed || e; }
    advanced.fire(); arrived.fire();
  };
  await Promise.all([writer(), ...workers.map(worker)]);
  if (failed) enc.abort(); else enc.end();
  const r = await enc.done;
  for (const w of workers) {
    await w.cdp.detach().catch(() => null);
    if (w.ctx) await w.ctx.close().catch(() => null);
  }
  if (r.code !== 0 || failed) {
    /* the .part is removed by fail(); the previous <name>.mp4, if there is one, was never touched */
    const detail = [r.err.trim()];
    if (failed && !(r.err.trim() && failed.message === 'ffmpeg stopped early')) detail.push(failed.stack || failed.message);
    fail(path.basename(mp4) + ': the render failed' + (r.code ? ' (ffmpeg exit ' + r.code + ')' : '') +
      ', so nothing was written' + (fs.existsSync(mp4) ? ' and the existing ' + path.basename(mp4) + ' is as it was' : '') + '.\n' +
      detail.filter(Boolean).join('\n'));
  }
  return { frames: frames, secs: (Date.now() - t0) / 1000, workers: K };
}

function probe(mp4) {
  const j = JSON.parse(sh('ffprobe', ['-v', 'error', '-show_entries',
    'stream=codec_name,width,height,pix_fmt,r_frame_rate,nb_frames:format=duration,size',
    '-of', 'json', mp4]));
  const v = j.streams[0], f = j.format;
  return { codec: v.codec_name, w: v.width, h: v.height, pix: v.pix_fmt, rate: v.r_frame_rate,
    frames: +v.nb_frames, duration: +f.duration, mb: +f.size / 1048576 };
}

/* What a cover-fit actually kept of each photograph, in the photograph's own pixels. Printed on every
   run, because "the crop lost the shoes" is the kind of thing nobody notices until it is posted. */
function cropReport(crops) {
  const seen = new Map();
  for (const c of crops) {
    if (!c.w || !c.h || !c.box[0] || !c.box[1]) continue;
    const name = decodeURIComponent(c.src.split('/').pop());
    const key = name + '@' + c.box.join('x');
    if (seen.has(key)) continue;
    const s = Math.max(c.box[0] / c.w, c.box[1] / c.h);
    const vw = Math.min(c.w, Math.round(c.box[0] / s)), vh = Math.min(c.h, Math.round(c.box[1] / s));
    let how;
    if (vw >= c.w - 1 && vh >= c.h - 1) how = 'the whole photograph';
    else if (vh >= c.h - 1) how = vw + ' of its ' + c.w + ' columns — the sides are trimmed, the full height is kept';
    else how = vh + ' of its ' + c.h + ' rows — the top and bottom are trimmed, so check the head and the shoes';
    seen.set(key, '    ' + name + ' ' + c.w + '\u00d7' + c.h + ' into ' + c.box.join('\u00d7') + ': ' + how);
  }
  return Array.from(seen.values());
}

async function renderOne(browser, ep, opt) {
  const d = ep.data, jsonPath = ep.path;
  const dir = path.dirname(jsonPath);
  const base = path.basename(jsonPath, '.json') + (opt.overlay ? '-overlay' : '');
  const label = path.basename(jsonPath) + (opt.overlay ? ' (overlay)' : '') + (opt.preview ? ' (preview)' : '');
  const cover = path.join(dir, base + '-cover.png');

  const { page, ctx, meta, crops } = await openPage(browser, withUrls(d), label);
  const pl = plan(meta, opt);
  const mp4 = path.join(dir, base + pl.tag + '.mp4');
  const w = W * pl.scale, h = H * pl.scale;
  console.log('  ' + label + ' — ' + meta.variant + (meta.mode === 'overlay' ? ' on chroma green' : '') +
    ', ' + meta.dir + ', ' + meta.duration.toFixed(2) + 's, ' + meta.frames + ' frames');
  cropReport(crops).forEach(l => console.log(l));
  if (meta.coverAt >= meta.duration) {
    fail(label + ': "cover_at" is ' + meta.coverAt + 's, but this episode is only ' +
      meta.duration.toFixed(2) + 's long. Pick a second inside it.');
  }

  if (opt.probe) {
    fs.mkdirSync(SCRATCH, { recursive: true });
    if (opt.debugSafe) await page.evaluate(() => window.__debugSafe(true));
    for (const t of opt.probe) {
      await page.evaluate(tt => window.seek(tt), t);
      const p2 = path.join(SCRATCH, base + '-probe-' + t.toFixed(2).replace('.', '_') + '.png');
      await page.screenshot({ path: p2 });
      console.log('    probe ' + t + 's -> ' + p2);
    }
    await page.close(); await ctx.close();
    return null;
  }

  if (opt.coverOnly) {
    if (opt.debugSafe) await page.evaluate(() => window.__debugSafe(true));
    await page.evaluate(t => window.seek(t), meta.coverAt);
    await page.screenshot({ path: cover });
    await page.close(); await ctx.close();
    const sz = fs.statSync(cover).size;
    console.log('    ' + path.basename(cover) + ' — 1080x1920, t=' + meta.coverAt.toFixed(2) + 's, ' +
      (sz / 1048576).toFixed(2) + ' MB');
    return { cover: cover };
  }

  /* Everything is written under a .part name and takes its final name only after the probe below
     has passed: a failure anywhere in between leaves no short, playable .mp4 under the final name
     and never replaces the previous good render or its cover. */
  const part = mp4 + '.part', coverPart = cover + '.part';
  PARTIALS.add(part);
  const keepDir = opt.keepFrames ? path.join(SCRATCH, base + pl.tag + '-frames') : null;
  const run = await renderVideo(browser, page, withUrls(d), label, meta, part, pl, keepDir);
  await page.close(); await ctx.close();
  console.log('    ' + run.frames + ' frames captured and encoded in ' + run.secs.toFixed(1) + 's on ' +
    run.workers + (run.workers === 1 ? ' page' : ' pages') + (keepDir ? ', kept in ' + keepDir : ''));

  /* the cover comes out of the finished file, so it is exactly a frame of the video */
  if (!opt.preview) {
    PARTIALS.add(coverPart);
    sh('ffmpeg', ['-y', '-loglevel', 'error', '-ss', String(meta.coverAt), '-i', part, '-frames:v', '1',
      '-c:v', 'png', '-update', '1', '-f', 'image2', coverPart]);
  }

  const pr = probe(part);
  console.log('    ' + path.basename(mp4) + ' — ' + pr.codec + ' ' + pr.w + 'x' + pr.h + ' ' + pr.pix +
    ' ' + pr.rate + ' · ' + pr.duration.toFixed(2) + 's · ' + pr.mb.toFixed(2) + ' MB (crf ' + pl.crf + ')');
  if (opt.preview) {
    console.log('    a preview: for the timing and the words only. Run the same command without --preview for the real one.');
  } else {
    console.log('    ' + path.basename(cover) + ' — 1080x1920, t=' + meta.coverAt.toFixed(2) + 's');
  }

  const bad = [];
  if (pr.w !== w || pr.h !== h) bad.push('it is ' + pr.w + 'x' + pr.h + ', not ' + w + 'x' + h);
  if (pr.codec !== 'h264') bad.push('the codec is ' + pr.codec + ', not h264');
  if (pr.pix !== 'yuv420p') bad.push('the pixel format is ' + pr.pix + ', not yuv420p');
  if (Math.abs(pr.duration - meta.duration) > 0.08) bad.push('it runs ' + pr.duration.toFixed(2) + 's, not ' + meta.duration.toFixed(2) + 's');
  if (pr.mb > MAX_MB) bad.push('it is ' + pr.mb.toFixed(2) + ' MB, over the ' + MAX_MB + ' MB limit');
  if (bad.length) fail(path.basename(mp4) + ' came out wrong, so it was not written:\n  ' + bad.join('\n  '));

  /* only now the final names */
  fs.renameSync(part, mp4); PARTIALS.delete(part);
  if (!opt.preview) { fs.renameSync(coverPart, cover); PARTIALS.delete(coverPart); }
  return { mp4: mp4, cover: opt.preview ? null : cover, probe: pr, crops: crops };
}

/* ------------------------------------------------------------------ main */
(async () => {
  const opt = parseArgs(process.argv.slice(2));
  let files = [];
  if (opt.all) {
    const dir = fs.existsSync(path.resolve(process.cwd(), opt.all))
      ? path.resolve(process.cwd(), opt.all) : path.resolve(REPO, opt.all);
    if (!fs.existsSync(dir)) fail('no folder at "' + opt.all + '".');
    files = fs.readdirSync(dir).filter(f => f.endsWith('.json')).sort().map(f => path.join(dir, f));
    if (!files.length) fail('no .json episodes in ' + dir + '.');
  } else if (opt.files.length) {
    files = opt.files.map(resolveJson);
  } else {
    fail('nothing to render.\n\n' + usageText());
  }

  /* Everything that can be judged from the files is judged before a browser is started: a typo in
     the tenth episode should not cost a browser launch and nine renders. */
  const episodes = files.map(f => ({ path: f, data: loadEpisode(f, opt.overlay) }));

  /* a video needs the encoder; a cover or a probe is a screenshot and needs neither */
  const encodes = !opt.coverOnly && !opt.probe;
  if (encodes) {
    checkTools();
    const cores = os.cpus().length;
    console.log('\n  ' + WORKERS + (WORKERS === 1 ? ' browser page' : ' browser pages') + ' per render (' +
      cores + (cores === 1 ? ' core' : ' cores') +
      (process.env.EPISODE_WORKERS ? ', EPISODE_WORKERS=' + process.env.EPISODE_WORKERS : '; one fewer than the cores, three at most') + ')');
  }

  const { chromium } = loadPlaywright();
  const browser = await chromium.launch({
    executablePath: process.env.CHROMIUM_PATH || '/opt/pw-browsers/chromium',
    args: ['--force-device-scale-factor=1', '--hide-scrollbars', '--disable-lcd-text',
      '--allow-file-access-from-files']
  });
  const done = [];
  try {
    for (const ep of episodes) {
      console.log('');
      const r = await renderOne(browser, ep, opt);
      if (r) done.push(r);
    }
  } finally {
    await browser.close();
  }
  if (done.length > 1) {
    console.log('\n  ' + done.length + ' episodes, ' +
      done.filter(r => r.probe).reduce((s, r) => s + r.probe.mb, 0).toFixed(2) + ' MB in total');
  }
  console.log('\n  ok\n');
})().catch(e => fail(e && e.stack ? e.stack : String(e)));
