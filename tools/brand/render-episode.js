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
 * Extra switches, for while you are editing:
 *   --probe 0,3.4,8.6,13     write those seconds as PNGs into the scratch folder and stop
 *   --debug-safe             draw the platform's unsafe rectangles over a --probe or a --cover-only
 *   --keep-frames            leave the PNG sequence in the scratch folder
 *   --crf 18                 override the encoder's quality (default 20, 16 for an overlay)
 *
 * Playwright is the browser test's copy in tools/e2e; the browser is CHROMIUM_PATH or
 * /opt/pw-browsers/chromium; ffmpeg and ffprobe come from PATH. Nothing touches the network: the
 * faces are the local OFL copies in tools/brand/templates/fonts and the look is a local file.
 * Scratch frames go to EPISODE_SCRATCH (default <tmp>/orevosh-episode), never into the repository.
 */
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');

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

function fail(msg) {
  console.error('\n  ' + String(msg).split('\n').join('\n  ') + '\n');
  process.exit(1);
}
function sh(cmd, args) { return execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 1 << 26 }); }

/* ------------------------------------------------------------------ the arguments */
function parseArgs(argv) {
  const o = { files: [], all: null, coverOnly: false, overlay: false, probe: null,
    debugSafe: false, keepFrames: false, crf: null };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--all') { o.all = argv[++i] || 'brand-kit/episodes'; }
    else if (a === '--cover-only') o.coverOnly = true;
    else if (a === '--overlay') o.overlay = true;
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
         '  --probe 0,8.6    write those seconds as PNGs into the scratch folder and stop\n' +
         '  --debug-safe     draw the platform\'s unsafe rectangles (probe and cover only)\n' +
         '  --keep-frames    leave the PNG sequence in the scratch folder\n' +
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
    fail(where + ': the photo "' + rel + '" does not exist.\n' +
      'Resolved to: ' + abs + '\n' +
      'Photo paths are relative to the episode JSON. The three supplied looks are at\n' +
      '  ../../tools/brand/templates/photos/look-1-streetwear.jpg\n' +
      '  ../../tools/brand/templates/photos/look-2-camel.jpg\n' +
      '  ../../tools/brand/templates/photos/look-3-pink.jpg');
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
async function openPage(browser, data, label) {
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
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
  return { page, meta: out.meta, crops: out.crops };
}

async function capture(page, meta, frameDir) {
  fs.rmSync(frameDir, { recursive: true, force: true });
  fs.mkdirSync(frameDir, { recursive: true });
  const t0 = Date.now();
  for (let f = 0; f < meta.frames; f++) {
    await page.evaluate(t => window.seek(t), f / FPS);
    await page.screenshot({ path: path.join(frameDir, 'frame-' + String(f).padStart(5, '0') + '.png') });
    if (f % 45 === 0) process.stdout.write('    frame ' + f + '/' + meta.frames + '\n');
  }
  return (Date.now() - t0) / 1000;
}

function encode(frameDir, mp4, crf) {
  /* Silent on purpose: no music is licensed, and both platforms reward a sound picked inside their
     own app at upload. Nothing in an episode depends on audio — every word is on screen. */
  sh('ffmpeg', ['-y', '-loglevel', 'error', '-framerate', String(FPS),
    '-i', path.join(frameDir, 'frame-%05d.png'),
    '-c:v', 'libx264', '-preset', 'slow', '-crf', String(crf),
    '-pix_fmt', 'yuv420p', '-movflags', '+faststart', mp4]);
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
  const label = path.basename(jsonPath) + (opt.overlay ? ' (overlay)' : '');
  const mp4 = path.join(dir, base + '.mp4');
  const cover = path.join(dir, base + '-cover.png');

  const { page, meta, crops } = await openPage(browser, withUrls(d), label);
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
    await page.close();
    return null;
  }

  if (opt.coverOnly) {
    if (opt.debugSafe) await page.evaluate(() => window.__debugSafe(true));
    await page.evaluate(t => window.seek(t), meta.coverAt);
    await page.screenshot({ path: cover });
    await page.close();
    const sz = fs.statSync(cover).size;
    console.log('    ' + path.basename(cover) + ' — 1080x1920, t=' + meta.coverAt.toFixed(2) + 's, ' +
      (sz / 1048576).toFixed(2) + ' MB');
    return { cover: cover };
  }

  const frameDir = path.join(SCRATCH, base + '-frames');
  const secs = await capture(page, meta, frameDir);
  await page.close();
  console.log('    captured ' + meta.frames + ' frames in ' + secs.toFixed(1) + 's');

  /* An overlay is keyed on a phone, and keying is unforgiving of the mosquito noise h264 leaves
     around a hard edge, so it is encoded finer than a normal episode. */
  const crf = opt.crf !== null ? opt.crf : (meta.mode === 'overlay' ? 16 : 20);
  encode(frameDir, mp4, crf);
  /* the cover comes out of the finished file, so it is exactly a frame of the video */
  sh('ffmpeg', ['-y', '-loglevel', 'error', '-ss', String(meta.coverAt), '-i', mp4, '-frames:v', '1', cover]);
  if (!opt.keepFrames) fs.rmSync(frameDir, { recursive: true, force: true });

  const pr = probe(mp4);
  console.log('    ' + path.basename(mp4) + ' — ' + pr.codec + ' ' + pr.w + 'x' + pr.h + ' ' + pr.pix +
    ' ' + pr.rate + ' · ' + pr.duration.toFixed(2) + 's · ' + pr.mb.toFixed(2) + ' MB (crf ' + crf + ')');
  console.log('    ' + path.basename(cover) + ' — 1080x1920, t=' + meta.coverAt.toFixed(2) + 's');

  const bad = [];
  if (pr.w !== W || pr.h !== H) bad.push('it is ' + pr.w + 'x' + pr.h + ', not ' + W + 'x' + H);
  if (pr.codec !== 'h264') bad.push('the codec is ' + pr.codec + ', not h264');
  if (pr.pix !== 'yuv420p') bad.push('the pixel format is ' + pr.pix + ', not yuv420p');
  if (Math.abs(pr.duration - meta.duration) > 0.08) bad.push('it runs ' + pr.duration.toFixed(2) + 's, not ' + meta.duration.toFixed(2) + 's');
  if (pr.mb > MAX_MB) bad.push('it is ' + pr.mb.toFixed(2) + ' MB, over the ' + MAX_MB + ' MB limit');
  if (bad.length) fail(path.basename(mp4) + ' came out wrong:\n  ' + bad.join('\n  '));
  return { mp4: mp4, cover: cover, probe: pr, crops: crops };
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
