// node src/film/s2/endcard.mjs <out.png>   draws the end card (1080x1920) to a PNG
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
const { chromium } = createRequire(import.meta.url)("playwright");
const here = dirname(fileURLToPath(import.meta.url));
const b = await chromium.launch();
const p = await b.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: 1 });
await p.goto(pathToFileURL(join(here, "endcard.html")).href);
await p.evaluate(() => document.fonts.ready);
await p.screenshot({ path: process.argv[2] });
await b.close();
console.log("end card ->", process.argv[2]);
