// Render a campaign spot: frame-accurate seek(t), motion blur from sub-frames, the page's own score
// (window.SCORE + window.CUES -> src/audio/<script>.py), mastering, and a 4:5 crop for the feed.
//   node src/render-spot.mjs anthem en                       # ads/10-anthem/venuvoy-anthem-en_9x16_1080x1920.mp4 (+ _4x5)
//   node src/render-spot.mjs anthem he --stills 0,1.2,2.6    # review stills only
//   node src/render-spot.mjs anthem en --from 4 --to 8       # a quick partial preview (scratch file)
//   --blur N   sub-frames averaged per output frame (default 2: a 180-degree shutter); 1 turns it off
import { createRequire } from "node:module";
import { spawn, spawnSync } from "node:child_process";
import { mkdirSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const SRC = dirname(fileURLToPath(import.meta.url));
const ADS = join(SRC, "..", "ads");
const FOLDERS = { anthem: "10-anthem", chaos: "11-chaos", sports: "12-sports", bumper: "13-bumper" };
const arg = (n, d) => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : d; };
const pos = process.argv.slice(2).filter((a, i, all) => !a.startsWith("--") && !all[i - 1]?.startsWith("--"));
const [name, lang = "en"] = pos;
if (!FOLDERS[name]) throw new Error(`unknown spot ${name}`);
const BLUR = +arg("--blur", 2), FPS = 30;
const STILLS = arg("--stills", null);
const FROM = arg("--from", null), TO = arg("--to", null);
const NOAUDIO = process.argv.includes("--no-audio");
const run = (cmd, args) => { const r = spawnSync(cmd, args.map(String), { stdio: "inherit" }); if (r.status !== 0) throw new Error(`${cmd} failed`); };

const outDir = join(ADS, FOLDERS[name]);
mkdirSync(outDir, { recursive: true });
const base = `venuvoy-${name}-${lang}`;

const browser = await chromium.launch({ args: ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"] });
const page = await browser.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: 1 });
page.on("pageerror", (e) => console.error("page error:", e.message));
await page.goto(pathToFileURL(join(SRC, "spots", `${name}.html`)).href + `?lang=${lang}#story`, { waitUntil: "load" });
await page.evaluate(() => window.__ready);
const dur = await page.evaluate(() => window.DURATION);

if (STILLS) {
  const dir = join(outDir, "stills");
  mkdirSync(dir, { recursive: true });
  for (const t of STILLS.split(",").map(Number)) {
    await page.evaluate((t) => window.seek(t), t);
    await page.screenshot({ path: join(dir, `${lang}-${t.toFixed(2)}.jpg`), type: "jpeg", quality: 82 });
  }
  console.log(`stills -> ${dir}`);
  await browser.close();
  process.exit(0);
}

const t0 = FROM ? +FROM : 0, t1 = TO ? +TO : dur;
const partial = FROM || TO;
const tmp = join(tmpdir(), `vv-spot-${name}-${lang}-${process.pid}`);
mkdirSync(tmp, { recursive: true });
const silent = join(tmp, "video.mp4");
// output frame k averages its own BLUR sub-frames: inputs k*BLUR .. k*BLUR+BLUR-1, so keep the last of each group
const vf = BLUR > 1 ? ["-vf", `tmix=frames=${BLUR}:weights='${Array(BLUR).fill(1).join(" ")}',select='eq(mod(n,${BLUR}),${BLUR - 1})',setpts=N/${FPS}/TB`] : [];
const ff = spawn("ffmpeg", ["-y", "-loglevel", "error", "-f", "image2pipe", "-framerate", String(FPS * BLUR), "-c:v", "mjpeg", "-i", "-", ...vf,
  "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", "-profile:v", "high", "-r", String(FPS), silent], { stdio: ["pipe", "inherit", "inherit"] });
const frames = Math.round((t1 - t0) * FPS) * BLUR;
let last = null, want0 = BLUR;
const started = Date.now();
for (let i = 0; i < frames; i++) {
  // the sub-frames of output frame k sample [k/30 - 1/60, k/30]: a 180-degree shutter
  const j = i % BLUR, t = t0 + (Math.floor(i / BLUR) - (BLUR > 1 ? (0.5 * (BLUR - 1 - j)) / (BLUR - 1) : 0)) / FPS;
  // a page can ask for fewer sub-frames where nothing moves fast (window.BLUR_AT): the last one is reused
  const want = j === 0 ? Math.min(BLUR, await page.evaluate((t) => (window.BLUR_AT ? window.BLUR_AT(t) : 99), t + 0.5 / FPS)) : want0;
  if (j === 0) want0 = want;
  let buf;
  if (j > 0 && want < 2) buf = last;
  else {
    await page.evaluate((t) => window.seek(Math.max(0, t)), want < 2 ? t + (0.5 * (BLUR - 1)) / (BLUR - 1 || 1) / FPS : t);
    buf = await page.screenshot({ type: "jpeg", quality: 95 });
  }
  last = buf;
  if (!ff.stdin.write(buf)) await new Promise((r) => ff.stdin.once("drain", r));
  if (i % (FPS * BLUR * 2) === 0) process.stdout.write(`  ${name}-${lang} ${(t).toFixed(1)}s / ${t1}s  (${((Date.now() - started) / 1000).toFixed(0)}s elapsed)\n`);
}
ff.stdin.end();
await new Promise((r, j) => ff.on("close", (c) => (c === 0 ? r() : j(new Error("ffmpeg " + c)))));
const { cues, score } = await page.evaluate(() => ({ cues: window.CUES || [], score: window.SCORE || null }));
await browser.close();

const out = partial ? join(tmp, `${base}_preview.mp4`) : join(outDir, `${base}_9x16_1080x1920.mp4`);
if (NOAUDIO || !score) {
  renameSync(silent, out);
} else {
  writeFileSync(join(tmp, "cues.json"), JSON.stringify(cues));
  run("python3", [join(SRC, "audio", score.script), join(tmp, "raw.wav"), "--dur", dur, "--cues", join(tmp, "cues.json"), "--lang", lang, ...(score.args || [])]);
  run("python3", [join(SRC, "audio", "master.py"), join(tmp, "raw.wav"), join(tmp, "music.wav")]);
  const music = join(tmp, "music.wav");
  const audioIn = partial ? ["-ss", String(t0), "-t", String(t1 - t0), "-i", music] : ["-i", music];
  run("ffmpeg", ["-y", "-loglevel", "error", "-i", silent, ...audioIn, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
    "-t", t1 - t0, "-movflags", "+faststart", out]);
}
if (!partial) {
  // 4:5 for the feed: the spots keep their key content between y 285 and 1635, so a centre crop holds it
  run("ffmpeg", ["-y", "-loglevel", "error", "-i", out, "-vf", "crop=1080:1350:0:285", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
    "-pix_fmt", "yuv420p", "-c:a", "copy", "-movflags", "+faststart", join(outDir, `${base}_4x5_1080x1350.mp4`)]);
  rmSync(tmp, { recursive: true, force: true });
  console.log(`spot: ${out} (+ 4:5)`);
} else {
  console.log(`preview: ${out}`);
}
