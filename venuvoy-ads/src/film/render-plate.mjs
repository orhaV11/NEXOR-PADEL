// Render the app demo of the bumper (src/spots/bumper.html?plate=1) as PNG frames with alpha, on the film's own
// clock, so it can be laid over live footage: the sentence is typed, the finished sentence holds, the plan arrives.
//   node src/film/render-plate.mjs <en|he> <out_dir> [--fps 24] [--type 0.75] [--hold 0.6] [--plan 2.05]
// Prints the frame counts of the three parts. Page times: typing 0.1-1.30, finished sentence at 1.32, plan 1.33-3.4.
import { createRequire } from "node:module";
import { mkdirSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const HERE = dirname(fileURLToPath(import.meta.url));
const pos = process.argv.slice(2).filter((a) => !a.startsWith("--"));
const opt = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? +process.argv[i + 1] : d; };
const [lang, outDir] = [pos[0] || "en", pos[1]];
const FPS = opt("--fps", 24), TYPE = opt("--type", 0.75), HOLD = opt("--hold", 0.6), PLAN = opt("--plan", 2.05);
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });

const nType = Math.round(TYPE * FPS), nHold = Math.round(HOLD * FPS), nPlan = Math.round(PLAN * FPS);
const times = [];
for (let i = 0; i < nType; i++) times.push(0.1 + (1.2 * i) / (nType - 1));       // typing, sped up to fit
for (let i = 0; i < nHold; i++) times.push(1.32);                                   // the finished sentence, held
for (let i = 0; i < nPlan; i++) times.push(1.335 + ((3.4 - 1.335) * i) / (nPlan - 1)); // the plan arrives

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: 1 });
page.on("pageerror", (e) => console.error("page error:", e.message));
await page.goto(pathToFileURL(join(HERE, "..", "spots", "bumper.html")).href + `?lang=${lang}&plate=1#story`, { waitUntil: "load" });
await page.evaluate(() => window.__ready);
for (let i = 0; i < times.length; i++) {
  await page.evaluate(([t, k]) => {
    window.seek(t);
    // the phone rises into frame over the first five frames
    const p = Math.min(1, (k + 1) / 5), e = 1 - Math.pow(1 - p, 3);
    const demo = document.getElementById("demo");
    demo.style.opacity = e;
    demo.style.translate = `0 ${(1 - e) * 140}px`;
    if (t >= 1.32) document.getElementById("caret").style.opacity = 0;
  }, [times[i], i]);
  await page.screenshot({ path: join(outDir, `${String(i).padStart(5, "0")}.png`), omitBackground: true });
}
await browser.close();
console.log(JSON.stringify({ frames: times.length, type: nType, hold: nHold, plan: nPlan, fps: FPS }));
