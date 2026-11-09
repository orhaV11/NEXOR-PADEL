// Render a reel frame by frame (deterministic seek(t)) and encode H.264 with ffmpeg.
//   node src/render-video.mjs                                   # Hebrew reel (src/video/reel.html)
//   node src/render-video.mjs --page video-en/reel-en.html --out 08-reel-en/venuvoy-reel-en_9x16_1080x1920.mp4
//   node src/render-video.mjs --page video-en/reel-en.html --stills 0.5,2,4.4   # review stills only
//   add --audio path/to/track.wav to mux a soundtrack (AAC 192k)
import { createRequire } from "node:module";
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const SRC = dirname(fileURLToPath(import.meta.url));
const ADS = join(SRC, "..", "ads");

const arg = (name, dflt) => {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : dflt;
};
const PAGE = arg("--page", "video/reel.html");
const OUTREL = arg("--out", "07-reel/venuvoy-reel_9x16_1080x1920.mp4");
const STILLS = arg("--stills", null);
const AUDIO = arg("--audio", null);
/** --score: compose the soundtrack from the page's own window.CUES (music stops on a stinger at --outro), master it, mux it. */
const SCORE = process.argv.includes("--score");
const OUTRO = arg("--outro", "28");
const FPS = 30;
const W = 1080, H = 1920;

const out = join(ADS, OUTREL);
mkdirSync(dirname(out), { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
const [pagePath, query] = PAGE.split("?");
await page.goto(pathToFileURL(join(SRC, pagePath)).href + (query ? "?" + query : "") + "#story", { waitUntil: "load" });
await page.evaluate(() => window.__ready);
const duration = await page.evaluate(() => window.DURATION);

if (STILLS) {
  const dir = join(dirname(out), "stills");
  mkdirSync(dir, { recursive: true });
  for (const t of STILLS.split(",").map(Number)) {
    await page.evaluate((t) => window.seek(t), t);
    await page.screenshot({ path: join(dir, `still-${t.toFixed(2)}.jpg`), type: "jpeg", quality: 80 });
  }
  console.log(`stills -> ${dir}`);
} else {
  let audio = AUDIO;
  if (SCORE) {
    const cues = await page.evaluate(() => window.CUES || []);
    const tmp = join(tmpdir(), "vv-score");
    mkdirSync(tmp, { recursive: true });
    writeFileSync(join(tmp, "cues.json"), JSON.stringify(cues));
    const raw = join(tmp, "raw.wav");
    const r1 = spawnSync("python3", [join(SRC, "audio", "compose.py"), raw, "--cues", join(tmp, "cues.json"), "--outro", OUTRO], { stdio: "inherit" });
    if (r1.status !== 0) throw new Error("compose failed");
    audio = out.replace(/\.mp4$/, "_music.wav");
    const r2 = spawnSync("python3", [join(SRC, "audio", "master.py"), raw, audio], { stdio: "inherit" });
    if (r2.status !== 0) throw new Error("master failed");
  }
  const silent = audio ? out.replace(/\.mp4$/, ".video.mp4") : out;
  const ff = spawn("ffmpeg", [
    "-y", "-loglevel", "error",
    "-f", "image2pipe", "-framerate", String(FPS), "-c:v", "mjpeg", "-i", "-",
    "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p",
    "-profile:v", "high", "-movflags", "+faststart", "-r", String(FPS), silent,
  ], { stdio: ["pipe", "inherit", "inherit"] });
  const frames = Math.round(duration * FPS);
  for (let i = 0; i < frames; i++) {
    await page.evaluate((t) => window.seek(t), i / FPS);
    const buf = await page.screenshot({ type: "jpeg", quality: 95 });
    if (!ff.stdin.write(buf)) await new Promise((r) => ff.stdin.once("drain", r));
    if (i % 120 === 0) process.stdout.write(`frame ${i}/${frames}\n`);
  }
  ff.stdin.end();
  await new Promise((r, j) => ff.on("close", (c) => (c === 0 ? r() : j(new Error("ffmpeg " + c)))));
  if (audio) {
    await new Promise((r, j) => {
      const mux = spawn("ffmpeg", ["-y", "-loglevel", "error", "-i", silent, "-i", audio, "-map", "0:v", "-map", "1:a",
        "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", out], { stdio: "inherit" });
      mux.on("close", (c) => (c === 0 ? r() : j(new Error("mux " + c))));
    });
  }
  if (audio) spawnSync("rm", ["-f", silent]);
  console.log(`video: ${out} (${frames} frames)`);
}
await browser.close();
