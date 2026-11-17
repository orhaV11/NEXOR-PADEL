// Captions and the closing plate as transparent pictures, set in the project's own fonts by a headless browser.
//   node src/film/toon/text.mjs <spec.json>
// spec: { "out": "work/switch/text", "items": [ { "name": "L1", "kind": "cap", "who": "L", "text": "..." }, { "name": "plate", "kind": "plate" } ] }
import { createRequire } from 'module';
const { chromium } = createRequire(import.meta.url)('playwright');  // the pre-installed browser tooling is found the CommonJS way
import fs from 'fs';
import path from 'path';
import url from 'url';

const here = path.dirname(url.fileURLToPath(import.meta.url));
const root = path.resolve(here, '../../..');
const spec = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const out = path.resolve(root, spec.out);
fs.mkdirSync(out, { recursive: true });
const font = (f) => url.pathToFileURL(path.join(root, 'fonts', f)).href;
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');

const css = `
@font-face { font-family: "Mont"; src: url(${font('montserrat-latin-wght-normal.woff2')}); font-weight: 100 900; }
@font-face { font-family: "Playfair"; src: url(${font('playfair-display-latin-wght-normal.woff2')}); font-weight: 400 900; }
@font-face { font-family: "Assistant"; src: url(${font('assistant-latin-wght-normal.woff2')}); font-weight: 200 800; }
html, body { margin: 0; background: transparent; }
.item { display: inline-block; padding: 30px; }
.cap { width: 900px; text-align: center; font-family: "Mont", sans-serif; font-weight: 800; font-size: 66px; line-height: 1.14; letter-spacing: -0.005em;
  color: #fff; -webkit-text-stroke: 9px #101010; paint-order: stroke fill; text-wrap: balance; filter: drop-shadow(0 4px 3px rgba(0,0,0,.35)); }
.cap.L { color: #fff; }  /* both speak in white: her yellow was lost on her yellow jacket */
.plate { display: inline-flex; flex-direction: column; align-items: center; gap: 16px; filter: drop-shadow(0 8px 12px rgba(0,0,0,.35)); }
.chip { position: relative; padding: 16px 34px 18px 112px; border-radius: 6px; background: #16392b; color: #f3ecdc; font-family: "Playfair", serif; font-weight: 500; font-size: 62px;
  letter-spacing: 0.2em; text-transform: uppercase; outline: 2px solid rgba(201,169,106,.9); outline-offset: -8px; }
.chip::before { content: "V"; position: absolute; left: 32px; top: 50%; width: 56px; height: 56px; margin-top: -30px; border-radius: 50%; border: 2px solid #c9a96a;
  box-shadow: inset 0 0 0 3px #16392b, inset 0 0 0 5px #c9a96a; font-family: "Playfair", serif; font-weight: 600; font-size: 36px; letter-spacing: 0; line-height: 54px; text-align: center; color: #f3ecdc; }
.line { padding: 10px 26px 13px; border-radius: 6px; background: #fbf8f1; color: #16392b; font-family: "Assistant", sans-serif; font-weight: 700; font-size: 44px; letter-spacing: 0.01em;
  border: 2px solid #ddd2bb; }
`;
const html = `<!doctype html><meta charset="utf-8"><style>${css}</style><body>` + spec.items.map((it) => {
  if (it.kind === 'plate') return `<div class="item" id="${it.name}"><div class="plate"><div class="chip">VENUVOY</div><div class="line">one sentence. your sports trip, planned.</div></div></div>`;
  return `<div class="item" id="${it.name}"><div class="cap ${it.who || ''}">${esc(it.text)}</div></div>`;
}).join('<br>') + '</body>';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 900 }, deviceScaleFactor: 1 });
await page.setContent(html);
await page.evaluate(() => document.fonts.ready);
for (const it of spec.items) {
  await page.locator('#' + it.name).screenshot({ path: path.join(out, it.name + '.png'), omitBackground: true });
}
await browser.close();
console.log('wrote', spec.items.length, 'pictures to', out);
