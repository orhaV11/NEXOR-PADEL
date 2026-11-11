// Film a group-chat scene (chat.html) as an MP4 the edit engine can cut like any other clip.
//   node src/film/render-chat.mjs <script.json> <he|en> <out.mp4> [--fps 24] [--still 3.2 out.png]
// script.json: see the comment in chat.html; any text may be {"en": .., "he": ..}. "dur" is the length in seconds.
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { spawn } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const HERE = dirname(fileURLToPath(import.meta.url));
const pos = process.argv.slice(2).filter((a) => !a.startsWith("--"));
const opt = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const [scriptPath, lang, outPath] = pos;
const FPS = +opt("--fps", 24);
const script = { ...JSON.parse(readFileSync(scriptPath, "utf8")), lang };

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: 1 });
page.on("pageerror", (e) => console.error("page error:", e.message));
await page.goto(pathToFileURL(join(HERE, "chat.html")).href + "?s=" + encodeURIComponent(JSON.stringify(script)), { waitUntil: "load" });
await page.evaluate(() => window.__ready);

const stillAt = process.argv.indexOf("--still");
if (stillAt > 0) {
  await page.evaluate((t) => window.seek(t), +process.argv[stillAt + 1]);
  await page.screenshot({ path: process.argv[stillAt + 2] });
  await browser.close();
  console.log(`still: ${process.argv[stillAt + 2]}`);
} else {
  const n = Math.round(script.dur * FPS);
  const ff = spawn("ffmpeg", ["-v", "error", "-y", "-f", "image2pipe", "-framerate", String(FPS), "-c:v", "png", "-i", "-",
    "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
    "-c:v", "libx264", "-preset", "slow", "-crf", "12", "-r", String(FPS), outPath], { stdio: ["pipe", "inherit", "inherit"] });
  for (let i = 0; i < n; i++) {
    await page.evaluate((t) => window.seek(t), i / FPS);
    const png = await page.screenshot();
    if (!ff.stdin.write(png)) await new Promise((r) => ff.stdin.once("drain", r));
  }
  ff.stdin.end();
  await new Promise((r) => ff.on("close", r));
  await browser.close();
  console.log(`chat: ${n} frames -> ${outPath}`);
}
