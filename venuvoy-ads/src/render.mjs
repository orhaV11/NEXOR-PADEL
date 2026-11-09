// Render every ad concept to exact-size PNGs.
//   node src/render.mjs                 # all concepts, all formats
//   node src/render.mjs kickoff story   # filter by concept and/or format (substring match)
import { createRequire } from "node:module";
import { mkdirSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

// Playwright is installed globally here; require() resolves it, a bare ESM import does not.
const { chromium } = createRequire(import.meta.url)("playwright");
const SRC = dirname(fileURLToPath(import.meta.url));
const OUT = join(SRC, "..", "ads");

export const FORMATS = {
  feed: { w: 1080, h: 1350, label: "פיד 4:5" },
  story: { w: 1080, h: 1920, label: "סטורי / ריל 9:16" },
  square: { w: 1080, h: 1080, label: "ריבוע 1:1" },
  landscape: { w: 1200, h: 628, label: "רוחבי 1.91:1" },
};

const [conceptFilter = "", formatFilter = ""] = process.argv.slice(2);
const concepts = readdirSync(join(SRC, "concepts"))
  .filter((f) => f.endsWith(".html") && f.includes(conceptFilter))
  .sort();

const browser = await chromium.launch();
let n = 0;
for (const file of concepts) {
  const name = file.replace(/\.html$/, "");
  mkdirSync(join(OUT, name), { recursive: true });
  for (const [format, { w, h }] of Object.entries(FORMATS)) {
    if (formatFilter && !format.includes(formatFilter)) continue;
    const page = await browser.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: 1 });
    const url = pathToFileURL(join(SRC, "concepts", file)).href + "#" + format;
    await page.goto(url, { waitUntil: "load" });
    await page.evaluate(() => window.__ready);
    const overflow = await page.evaluate(() => {
      // Flag any text box that spills outside the canvas: a cheap guard against clipped copy.
      const ad = document.querySelector(".ad").getBoundingClientRect();
      const bad = [];
      document.querySelectorAll("[data-guard]").forEach((el) => {
        const r = el.getBoundingClientRect();
        if (r.width === 0) return;
        if (r.left < ad.left || r.right > ad.right || r.top < ad.top || r.bottom > ad.bottom)
          bad.push(el.dataset.guard);
        if (el.scrollWidth > el.clientWidth + 1) bad.push(el.dataset.guard + " (overflow-x)");
      });
      return bad;
    });
    const out = join(OUT, name, `${name}_${format}_${w}x${h}.png`);
    await page.screenshot({ path: out, clip: { x: 0, y: 0, width: w, height: h } });
    console.log(`${overflow.length ? "WARN" : "ok  "} ${out.replace(OUT + "/", "")}${overflow.length ? "  overflow: " + overflow.join(", ") : ""}`);
    await page.close();
    n++;
  }
}
await browser.close();
console.log(`${n} images`);
