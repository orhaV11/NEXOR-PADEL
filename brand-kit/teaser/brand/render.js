#!/usr/bin/env node
/*
 * OREVOSH launch teaser — "Light it up". Renders teaser.html to a 1080x1920 MP4 and a cover PNG.
 *
 *   node render.js                 the whole thing: frames, MP4, cover, ffprobe check
 *   node render.js --debug         one frame with the TikTok/Instagram safe area drawn over it
 *   node render.js --probe 3.2,8.3 single frames at those seconds, to look at while editing
 *   node render.js --frames-only   stop after the PNG sequence
 *
 * The page draws every frame from window.seek(t), so the exporter only ever asks for a time and
 * takes a picture: no CSS animation is in flight while the shutter is open, and frame N is always
 * identical. Scratch frames go to /tmp/brand-real/frames (TEASER_SCRATCH moves them), never into
 * the repository.
 *
 * The teaser is silent by design: no music is licensed for it, and TikTok and Instagram both
 * reward a sound picked inside their own app, so the owner adds one at upload.
 */
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const { chromium } = require("/home/user/NEXOR-PADEL/tools/e2e/node_modules/playwright");

const HERE = __dirname;
const PAGE = "file://" + path.join(HERE, "teaser.html");
const WORK = process.env.TEASER_SCRATCH || "/tmp/brand-real";
const FRAMES = path.join(WORK, "frames");
const OUT = path.join(HERE, "light-it-up-1080x1920.mp4");
const COVER = path.join(HERE, "light-it-up-cover-1080x1920.png");

const W = 1080, H = 1920, FPS = 30, DUR = 10.0;
const N = Math.round(FPS * DUR);
const COVER_T = 9.52;            /* the end card, fully in and before the fade: the thumbnail */

const EXE = "/opt/pw-browsers/chromium";
const argv = process.argv.slice(2);
const has = f => argv.includes(f);
const flag = f => { const i = argv.indexOf(f); return i >= 0 ? argv[i + 1] : null; };

function ff(args) { return execFileSync("ffmpeg", args, { stdio: ["ignore", "pipe", "pipe"] }); }
function probe(file) {
  return execFileSync("ffprobe", ["-v", "error", "-show_entries",
    "stream=width,height,codec_name,pix_fmt,r_frame_rate,nb_frames:format=duration,size",
    "-of", "default=noprint_wrappers=1", file], { encoding: "utf8" });
}

(async () => {
  const browser = await chromium.launch({ executablePath: EXE, args: ["--force-color-profile=srgb"] });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
  await page.goto(PAGE, { waitUntil: "load" });
  await page.waitForFunction(() => window.__ready === true, null, { timeout: 30000 });
  /* every real capture decoded before the first shutter, or frame 0 renders an empty box */
  await page.waitForFunction(() => Array.from(document.images).every(i => i.complete && i.naturalWidth > 0),
    null, { timeout: 30000 });

  if (has("--debug")) {
    fs.mkdirSync(WORK, { recursive: true });
    /* 3.10 is the widest frame of the score stamp, 8.14 the widest of the medal: the two moments
       where something briefly overshoots its resting size and could cross the line */
    for (const t of [0.30, 2.30, 3.10, 3.30, 5.10, 7.20, 8.14, 8.30, 9.40]) {
      await page.evaluate(tt => { window.seek(tt); window.__debugSafe(true); }, t);
      await page.screenshot({ path: path.join(WORK, "safe-" + String(t).replace(".", "_") + ".png") });
    }
    await page.evaluate(() => window.__debugSafe(false));
    await browser.close();
    console.log("debug safe-area frames in " + WORK);
    return;
  }

  if (has("--probe")) {
    fs.mkdirSync(WORK, { recursive: true });
    for (const raw of (flag("--probe") || "").split(",")) {
      const t = Number(raw);
      if (!isFinite(t)) continue;
      await page.evaluate(tt => window.seek(tt), t);
      await page.screenshot({ path: path.join(WORK, "probe-" + raw.replace(".", "_") + ".png") });
    }
    await browser.close();
    console.log("probe frames in " + WORK);
    return;
  }

  fs.rmSync(FRAMES, { recursive: true, force: true });
  fs.mkdirSync(FRAMES, { recursive: true });
  for (let f = 0; f < N; f++) {
    await page.evaluate(t => window.seek(t), f / FPS);
    await page.screenshot({ path: path.join(FRAMES, "frame-" + String(f).padStart(5, "0") + ".png") });
    if (f % 30 === 0) process.stdout.write("  frame " + f + "/" + N + "\r");
  }
  process.stdout.write("  frame " + N + "/" + N + "\n");

  /* the cover, straight from the page at full quality */
  await page.evaluate(t => window.seek(t), COVER_T);
  await page.screenshot({ path: COVER });
  await browser.close();

  if (has("--frames-only")) { console.log("frames in " + FRAMES); return; }

  ff(["-y", "-framerate", String(FPS), "-i", path.join(FRAMES, "frame-%05d.png"),
      "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-pix_fmt", "yuv420p",
      "-movflags", "+faststart", OUT]);

  console.log(probe(OUT));
  console.log("mp4   " + OUT + "  " + (fs.statSync(OUT).size / 1048576).toFixed(2) + " MB");
  console.log("cover " + COVER + "  " + (fs.statSync(COVER).size / 1024).toFixed(0) + " KB");
  console.log("silent by design: pick a sound inside TikTok / Instagram at upload.");
})().catch(e => { console.error(e); process.exit(1); });
