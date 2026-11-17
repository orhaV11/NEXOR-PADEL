// Words for still posts (carousels, covers, the profile picture) as transparent pictures, set in the project's fonts.
//   node src/film/toon/cards.mjs <spec.json>
// spec: { "out": "work/stills/text", "items": [
//   { "name": "a1", "kind": "say", "who": "L" | "T", "text": "...", "size": 76, "width": 900 },     a spoken line (hers white, his teal)
//   { "name": "t1", "kind": "title", "text": "THE SWITCH", "size": 96, "width": 900 },              a cover title
//   { "name": "c1", "kind": "card", "head": "TIMO, by Luna", "text": "...", "width": 860 },         an ivory card with a heading
//   { "name": "chip", "kind": "chip" }, { "name": "plate", "kind": "plate" } ] }                    the brand chip alone, or with its line
import { createRequire } from 'module';
const { chromium } = createRequire(import.meta.url)('playwright');
import fs from 'fs';
import path from 'path';
import url from 'url';

const here = path.dirname(url.fileURLToPath(import.meta.url));
const root = path.resolve(here, '../../..');
const spec = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const out = path.resolve(root, spec.out);
fs.mkdirSync(out, { recursive: true });
const font = (f) => url.pathToFileURL(path.join(root, 'fonts', f)).href;
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/\n/g, '<br>');

const css = `
@font-face { font-family: "Mont"; src: url(${font('montserrat-latin-wght-normal.woff2')}); font-weight: 100 900; }
@font-face { font-family: "Playfair"; src: url(${font('playfair-display-latin-wght-normal.woff2')}); font-weight: 400 900; }
@font-face { font-family: "Assistant"; src: url(${font('assistant-latin-wght-normal.woff2')}); font-weight: 200 800; }
html, body { margin: 0; background: transparent; }
.item { display: inline-block; padding: 30px; }
.say { text-align: center; font-family: "Mont", sans-serif; font-weight: 800; line-height: 1.14; letter-spacing: -0.01em; color: #fff;
  -webkit-text-stroke: 10px #101010; paint-order: stroke fill; text-wrap: balance; filter: drop-shadow(0 5px 4px rgba(0,0,0,.35)); }
.say.T { color: #7fe0d0; }
.title { text-align: center; font-family: "Mont", sans-serif; font-weight: 900; line-height: 1.02; letter-spacing: 0.01em; color: #fff; text-transform: uppercase;
  -webkit-text-stroke: 14px #101010; paint-order: stroke fill; text-wrap: balance; filter: drop-shadow(0 6px 5px rgba(0,0,0,.4)); }
.card { box-sizing: border-box; padding: 34px 44px 40px; border-radius: 10px; background: #fbf8f1; color: #26221c; border: 2px solid #ddd2bb; outline: 2px solid rgba(184,149,90,.8); outline-offset: -12px;
  font-family: "Assistant", sans-serif; font-weight: 600; font-size: 46px; line-height: 1.22; filter: drop-shadow(0 10px 14px rgba(0,0,0,.3)); }
.card .head { font-family: "Playfair", serif; font-weight: 600; font-size: 44px; letter-spacing: 0.14em; text-transform: uppercase; color: #16392b; margin-bottom: 18px; }
.plate { display: inline-flex; flex-direction: column; align-items: center; gap: 16px; filter: drop-shadow(0 8px 12px rgba(0,0,0,.35)); }
.chip { position: relative; padding: 16px 34px 18px 112px; border-radius: 6px; background: #16392b; color: #f3ecdc; font-family: "Playfair", serif; font-weight: 500; font-size: 62px;
  letter-spacing: 0.2em; text-transform: uppercase; outline: 2px solid rgba(201,169,106,.9); outline-offset: -8px; }
.chip::before { content: "V"; position: absolute; left: 32px; top: 50%; width: 56px; height: 56px; margin-top: -30px; border-radius: 50%; border: 2px solid #c9a96a;
  box-shadow: inset 0 0 0 3px #16392b, inset 0 0 0 5px #c9a96a; font-family: "Playfair", serif; font-weight: 600; font-size: 36px; letter-spacing: 0; line-height: 54px; text-align: center; color: #f3ecdc; }
.line { padding: 10px 26px 13px; border-radius: 6px; background: #fbf8f1; color: #16392b; font-family: "Assistant", sans-serif; font-weight: 700; font-size: 44px; letter-spacing: 0.01em; border: 2px solid #ddd2bb; }
`;
const html = `<!doctype html><meta charset="utf-8"><style>${css}</style><body>` + spec.items.map((it) => {
  const w = `width:${it.width || 900}px;`;
  if (it.kind === 'plate') return `<div class="item" id="${it.name}"><div class="plate"><div class="chip">VENUVOY</div><div class="line">one sentence. your sports trip, planned.</div></div></div>`;
  if (it.kind === 'chip') return `<div class="item" id="${it.name}"><div class="plate"><div class="chip">VENUVOY</div></div></div>`;
  if (it.kind === 'card') return `<div class="item" id="${it.name}"><div class="card" style="${w}">${it.head ? `<div class="head">${esc(it.head)}</div>` : ''}${esc(it.text)}</div></div>`;
  if (it.kind === 'title') return `<div class="item" id="${it.name}"><div class="title" style="${w}font-size:${it.size || 96}px">${esc(it.text)}</div></div>`;
  return `<div class="item" id="${it.name}"><div class="say ${it.who || ''}" style="${w}font-size:${it.size || 76}px">${esc(it.text)}</div></div>`;
}).join('<br>') + '</body>';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1300, height: 900 }, deviceScaleFactor: 1 });
const file = path.join(out, '_cards.html');
fs.writeFileSync(file, html);
await page.goto(url.pathToFileURL(file).href);
await page.evaluate(() => document.fonts.ready);
const loaded = await page.evaluate(async () => {
  const want = ['800 76px "Mont"', '500 62px "Playfair"', '700 44px "Assistant"'];
  await Promise.all(want.map((f) => document.fonts.load(f)));
  return want.map((f) => [f, document.fonts.check(f)]);
});
if (loaded.some(([, ok]) => !ok)) { console.error('fonts did not load:', JSON.stringify(loaded)); process.exit(1); }
for (const it of spec.items) await page.locator('#' + it.name).screenshot({ path: path.join(out, it.name + '.png'), omitBackground: true });
await browser.close();
console.log('wrote', spec.items.length, 'pictures to', out);
