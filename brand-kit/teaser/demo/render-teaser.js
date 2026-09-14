#!/usr/bin/env node
/*
 * Renders brand-kit/teaser/demo/teaser.html to the 1080x1920 launch teaser.
 *
 * The template exposes window.seek(t); this script walks t = f/FPS, screenshots every frame into a scratch
 * folder outside the repository, encodes with ffmpeg, pulls the two cover frames out of the finished MP4
 * and verifies the result with ffprobe. Nothing animates by itself in the page, so frame N is always the
 * same picture no matter how slow the machine is.
 *
 *   node render-teaser.js                 # frames + mp4 + covers + verify
 *   node render-teaser.js --probe 0,4.5,7.8,11.9   # only those seconds, as PNGs, to look at
 *   node render-teaser.js --keep-frames   # leave the scratch frames in place
 *
 * Playwright comes from tools/e2e (the browser test's copy); the browser is CHROMIUM_PATH or
 * /opt/pw-browsers/chromium. ffmpeg and ffprobe must be on PATH.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const REPO = path.resolve(__dirname, '../../..');
// playwright is the browser test's copy (tools/e2e); a worktree without its node_modules falls back to the
// main checkout, and PLAYWRIGHT_PATH overrides both.
function loadPlaywright() {
  const tries = [process.env.PLAYWRIGHT_PATH, path.join(REPO, 'tools/e2e/node_modules/playwright'),
    '/home/user/NEXOR-PADEL/tools/e2e/node_modules/playwright', 'playwright'].filter(Boolean);
  for (const p of tries) { try { return require(p); } catch (e) { /* next */ } }
  throw new Error('playwright not found; tried:\n  ' + tries.join('\n  '));
}
const { chromium } = loadPlaywright();

const FPS = 30;
const DURATION = 12;                       // seconds
const FRAMES = FPS * DURATION;             // 360
const W = 1080, H = 1920;
const COVER_T = 7.80;                      // the frame the owner gets as the thumbnail: the one tip, up
const COVER_ALT_T = 3.36;                  // the alternative: the score landing, 9/10 in the full ring

const OUT = __dirname;
const SCRATCH = process.env.TEASER_SCRATCH || '/tmp/demo';
const FRAMEDIR = path.join(SCRATCH, 'frames');
const MP4 = path.join(OUT, 'orevosh-teaser-1080x1920.mp4');
const COVER = path.join(OUT, 'orevosh-teaser-cover-1080x1920.png');
const COVER_ALT = path.join(OUT, 'orevosh-teaser-cover-alt-1080x1920.png');

const argv = process.argv.slice(2);
const probeAt = argv.includes('--probe')
  ? String(argv[argv.indexOf('--probe') + 1] || '').split(',').filter(Boolean).map(Number)
  : null;
const keepFrames = argv.includes('--keep-frames');

function sh(cmd, args) {
  return execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 1 << 26 });
}

async function openPage(browser) {
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
  await page.goto('file://' + path.join(__dirname, 'teaser.html'), { waitUntil: 'load' });
  // the faces of DESIGN.md §2 and every capture must be in before the first frame, or the export
  // starts on fallback type and empty photo blocks
  await page.evaluate(async () => {
    await document.fonts.ready;
    await Promise.all(Array.from(document.images).map(i => (i.complete ? null : i.decode().catch(() => null))));
  });
  const missing = await page.evaluate(() =>
    Array.from(document.images).filter(i => !i.complete || (i.naturalWidth === 0 && !i.src.endsWith('.svg'))).map(i => i.src));
  if (missing.length) throw new Error('assets did not load:\n  ' + missing.join('\n  '));
  const faces = await page.evaluate(() => ({
    outfit: document.fonts.check('800 64px Outfit'),
    heebo: document.fonts.check('700 20px Heebo')
  }));
  if (!faces.outfit || !faces.heebo) throw new Error('local fonts did not load: ' + JSON.stringify(faces));
  return page;
}

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.CHROMIUM_PATH || '/opt/pw-browsers/chromium',
    args: ['--force-device-scale-factor=1', '--hide-scrollbars', '--disable-lcd-text']
  });
  const page = await openPage(browser);

  if (probeAt) {
    fs.mkdirSync(SCRATCH, { recursive: true });
    for (const t of probeAt) {
      await page.evaluate((tt) => window.seek(tt), t);
      const p = path.join(SCRATCH, 'probe-' + t.toFixed(2).replace('.', '_') + '.png');
      await page.screenshot({ path: p });
      console.log('probe', t + 's ->', p);
    }
    await browser.close();
    return;
  }

  fs.rmSync(FRAMEDIR, { recursive: true, force: true });
  fs.mkdirSync(FRAMEDIR, { recursive: true });
  const t0 = Date.now();
  for (let f = 0; f < FRAMES; f++) {
    await page.evaluate((t) => window.seek(t), f / FPS);
    await page.screenshot({ path: path.join(FRAMEDIR, 'frame-' + String(f).padStart(5, '0') + '.png') });
    if (f % 60 === 0) process.stdout.write('  frame ' + f + '/' + FRAMES + '\n');
  }
  await browser.close();
  console.log('captured ' + FRAMES + ' frames in ' + ((Date.now() - t0) / 1000).toFixed(1) + 's');

  // silent on purpose: no music is licensed, and TikTok and Instagram reward a sound added in the app
  sh('ffmpeg', ['-y', '-loglevel', 'error', '-framerate', String(FPS),
    '-i', path.join(FRAMEDIR, 'frame-%05d.png'),
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '20', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', MP4]);

  // both covers come out of the finished file, so each is exactly a frame of the video: the owner picks
  // between the one tip and the score landing as the thumbnail
  sh('ffmpeg', ['-y', '-loglevel', 'error', '-ss', String(COVER_T), '-i', MP4, '-frames:v', '1', COVER]);
  sh('ffmpeg', ['-y', '-loglevel', 'error', '-ss', String(COVER_ALT_T), '-i', MP4, '-frames:v', '1', COVER_ALT]);

  if (!keepFrames) fs.rmSync(FRAMEDIR, { recursive: true, force: true });

  const probe = JSON.parse(sh('ffprobe', ['-v', 'error', '-show_entries',
    'stream=codec_name,width,height,pix_fmt,r_frame_rate,nb_frames:format=duration,size',
    '-of', 'json', MP4]));
  const v = probe.streams[0], fmt = probe.format;
  const mb = (+fmt.size / 1048576);
  console.log('\n' + path.basename(MP4) + ': ' + v.codec_name + ' ' + v.width + 'x' + v.height + ' ' +
    v.pix_fmt + ' ' + v.r_frame_rate + ' ' + v.nb_frames + ' frames ' +
    (+fmt.duration).toFixed(2) + 's ' + mb.toFixed(2) + ' MB');
  for (const [p, t] of [[COVER, COVER_T], [COVER_ALT, COVER_ALT_T]]) {
    console.log(path.basename(p) + ': ' + sh('ffprobe', ['-v', 'error', '-show_entries',
      'stream=width,height', '-of', 'csv=p=0:s=x', p]).trim() + ' (t=' + t + 's)');
  }

  const bad = [];
  if (v.width !== W || v.height !== H) bad.push('size');
  if (v.codec_name !== 'h264') bad.push('codec');
  if (v.pix_fmt !== 'yuv420p') bad.push('pix_fmt');
  if (Math.abs(+fmt.duration - DURATION) > 0.08) bad.push('duration');
  if (mb > 12) bad.push('size > 12 MB');
  if (bad.length) { console.error('FAILED: ' + bad.join(', ')); process.exit(1); }
  console.log('ok');
})().catch((e) => { console.error(e); process.exit(1); });
