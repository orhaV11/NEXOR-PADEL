// Render the transparent title/subtitle layer of "Operation: Away Day" as PNG frames with alpha.
//   node src/heist/render-overlay.mjs <timeline.json> <out_dir> [--fps 24]
// timeline.json: { dur, cards: [{ t0, t1, name, what }], subs: [{ t0, t1, text }] }
import { createRequire } from "node:module";
import { mkdirSync, readFileSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const HERE = dirname(fileURLToPath(import.meta.url));
const [tlPath, outDir] = process.argv.slice(2).filter((a) => !a.startsWith("--"));
const fpsArg = process.argv.indexOf("--fps");
const FPS = fpsArg > 0 ? +process.argv[fpsArg + 1] : 24;
const tl = JSON.parse(readFileSync(tlPath, "utf8"));
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: 1 });
page.on("pageerror", (e) => console.error("page error:", e.message));
await page.goto(pathToFileURL(join(HERE, "overlay.html")).href + "?tl=" + encodeURIComponent(JSON.stringify(tl)), { waitUntil: "load" });
await page.evaluate(() => window.__ready);
const n = Math.round(tl.dur * FPS);
for (let i = 0; i < n; i++) {
  await page.evaluate((t) => window.seek(t), i / FPS);
  await page.screenshot({ path: join(outDir, `${String(i).padStart(5, "0")}.png`), omitBackground: true });
}
await browser.close();
console.log(`overlay: ${n} frames -> ${outDir}`);
