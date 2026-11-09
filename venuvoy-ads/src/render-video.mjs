// Render the 9:16 reel frame by frame (deterministic seek(t)) and encode H.264 with ffmpeg.
//   node src/render-video.mjs            # full reel
//   node src/render-video.mjs --stills   # a few stills for review, no video
import { createRequire } from "node:module";
import { spawn } from "node:child_process";
import { mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const SRC = dirname(fileURLToPath(import.meta.url));
const OUT = join(SRC, "..", "ads", "07-reel");
mkdirSync(OUT, { recursive: true });

const FPS = 30;
const W = 1080, H = 1920;
const stills = process.argv.includes("--stills");

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
await page.goto(pathToFileURL(join(SRC, "video", "reel.html")).href + "#story", { waitUntil: "load" });
await page.evaluate(() => window.__ready);
const duration = await page.evaluate(() => window.DURATION);

if (stills) {
  const times = [0.2, 0.6, 1.9, 3.3, 4.9, 6.4, 8.6, 10.2, 11.4, 13.3, 14.6, 16.5];
  for (const t of times) {
    await page.evaluate((t) => window.seek(t), t);
    await page.screenshot({ path: join(OUT, `still-${t.toFixed(1)}.jpg`), type: "jpeg", quality: 80 });
  }
  console.log(`stills: ${times.length}`);
} else {
  const out = join(OUT, "venuvoy-reel_9x16_1080x1920.mp4");
  const ff = spawn("ffmpeg", [
    "-y", "-loglevel", "error",
    "-f", "image2pipe", "-framerate", String(FPS), "-c:v", "mjpeg", "-i", "-",
    "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p",
    "-profile:v", "high", "-movflags", "+faststart", "-r", String(FPS), out,
  ], { stdio: ["pipe", "inherit", "inherit"] });
  const frames = Math.round(duration * FPS);
  for (let i = 0; i < frames; i++) {
    await page.evaluate((t) => window.seek(t), i / FPS);
    const buf = await page.screenshot({ type: "jpeg", quality: 95 });
    if (!ff.stdin.write(buf)) await new Promise((r) => ff.stdin.once("drain", r));
    if (i % 60 === 0) process.stdout.write(`frame ${i}/${frames}\n`);
  }
  ff.stdin.end();
  await new Promise((r, j) => ff.on("close", (c) => (c === 0 ? r() : j(new Error("ffmpeg " + c)))));
  console.log(`video: ${out} (${frames} frames)`);
}
await browser.close();
