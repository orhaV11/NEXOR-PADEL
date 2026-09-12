#!/usr/bin/env node
/*
  Renders the OREVOSH brand kit from the HTML templates in ./templates with Playwright: the logos, the covers, the
  story templates and the store screenshots under /brand-kit, the Open Graph card the app serves
  (src/FitCheck.Api/wwwroot/brand/og-1200x630.png), and the JPEG screens the landing page embeds
  (src/FitCheck.Api/wwwroot/landing/screens/). The templates use the real brand SVGs
  (src/FitCheck.Api/wwwroot/brand/), the tokens of DESIGN.md (templates/base.css), the app screenshots in
  templates/screens/ (cropped to the phone viewport) and the two faces Outfit and Heebo (Google Fonts when the
  machine is online, the OFL copies in templates/fonts/ otherwise), so the kit can be regenerated after a slogan
  change, a new screenshot, or a new format, by running:

      node tools/brand/render-kit.js                 # everything
      node tools/brand/render-kit.js stories store   # one or more categories: logos covers stories store web
      node tools/brand/render-kit.js readme          # only rewrite brand-kit/README.md from the files present

  Needs playwright (npm install in tools/brand, or NODE_PATH=tools/e2e/node_modules) and a Chromium: the one
  Playwright installed, or CHROMIUM_PATH=/path/to/chromium. With sharp installed (optional) the PNGs are
  palette-quantised (about a third of the size, no visible loss on these flat designs); without it they are
  written as Chromium produces them. Set BRAND_OFFLINE=1 to skip the Google Fonts probe.
*/
'use strict';
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');

const ROOT = path.resolve(__dirname, '..', '..');
const TPL = path.join(__dirname, 'templates');
const SCREENS = path.join(TPL, 'screens');
const WWW = path.join(ROOT, 'src', 'FitCheck.Api', 'wwwroot');
const BRAND = path.join(WWW, 'brand');
const OUT = process.env.BRAND_KIT_OUT ? path.resolve(process.env.BRAND_KIT_OUT) : path.join(ROOT, 'brand-kit');

let playwright;
try { playwright = require('playwright'); } catch {
  console.error('playwright is not installed. Run `npm install` in tools/brand, or set NODE_PATH to a folder that has it (tools/e2e/node_modules after `npm install` there).');
  process.exit(1);
}
let sharp = null;
try { sharp = require('sharp'); } catch { /* optional */ }

// ---------- the copy ----------

const COPY = {
  en: {
    slogan: 'Check the look.',
    tagline: 'A stylist in your pocket, and a community that lights it up.',
    cta: 'Check yours. Link in bio.',
    stories: [
      { id: 'check-the-look', title: 'Check the look.', sub: '', shot: '07-check-ready-en.png' },
      { id: 'the-one-tip', title: '7/10 and the one tip', sub: '“Swap the running shoes for plain white leather sneakers.”', shot: '08-result-en.png' },
      { id: 'brands', title: 'Which brands you wear', sub: 'Tag them with @. They feature the looks they love.', shot: '11-featured-en.png' },
    ],
    store: [
      { caption: 'Check the look in 10 seconds', shot: '07-check-ready-en.png' },
      { caption: 'Fit, color, accessories', shot: '08-result-en.png' },
      { caption: 'Post it, light it up', shot: '10-post-en.png' },
      { caption: 'Brands feature the looks they love', shot: '12-brand-community-en.png' },
      { caption: 'Film it in the app', shot: '20-recording-en.png' },
    ],
    web: [
      { id: 'check', shot: '07-check-ready-en.png' },
      { id: 'result', shot: '08-result-en.png' },
      { id: 'look', shot: '11-featured-en.png' },
    ],
    cover: '08-result-en.png',
  },
  he: {
    slogan: 'בודקים את הלוק.',
    tagline: 'סטייליסט בכיס, וקהילה שמדליקה.',
    cta: 'בדקו את שלכם. לינק בביו.',
    stories: [
      { id: 'check-the-look', title: 'בודקים את הלוק.', sub: '', shot: '00-guest-result-he.png' },
      // The Hebrew result screen the browser test captured scored 6/10, so the headline follows the picture.
      { id: 'the-one-tip', title: '6/10 והטיפ האחד', sub: '"שווה להחליף את נעלי הריצה בסניקרס עור לבן פשוט."', shot: '00-guest-result-he.png' },
      { id: 'brands', title: 'אילו מותגים אתם לובשים', sub: 'מתייגים עם @. הם מציגים את הלוקים שהם אוהבים.', shot: '14-tag-he.png' },
    ],
    store: [
      { caption: 'בודקים את הלוק ב-10 שניות', shot: '07-check-ready-en.png' },
      { caption: 'גזרה, צבע, אקססוריז', shot: '00-guest-result-he.png' },
      { caption: 'מפרסמים, מדליקים', shot: '16-home-he.png' },
      { caption: 'מותגים מציגים את הלוקים שהם אוהבים', shot: '14-tag-he.png' },
      { caption: 'מצלמים בתוך האפליקציה', shot: '20-recording-en.png' },
    ],
    web: [
      { id: 'result', shot: '00-guest-result-he.png' },
      { id: 'feed', shot: '16-home-he.png' },
      { id: 'tag', shot: '14-tag-he.png' },
    ],
    cover: '00-guest-result-he.png',
  },
};
const STORE_SIZES = [
  { id: 'iphone-6.7', w: 1284, h: 2778, use: 'App Store, iPhone 6.7" (also accepted for 6.5" and 6.9")' },
  { id: 'android', w: 1080, h: 1920, use: 'Google Play phone screenshot (9:16)' },
];

// ---------- helpers ----------

const fileUrl = (p) => pathToFileURL(p).href;
const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
function fill(template, params) {
  return template
    .replace(/\{\{\{(\w+)\}\}\}/g, (_, k) => (params[k] ?? ''))
    .replace(/\{\{(\w+)\}\}/g, (_, k) => escapeHtml(params[k] ?? ''));
}
const kb = (n) => `${Math.max(1, Math.round(n / 1024))} KB`;

/* A monochrome copy of a brand SVG: the gradients go, the ring, the flame and the letters take one colour, the
   knock-out mask stays so the ring is still broken open by the flame. */
function mono(svg, color) {
  return svg
    .replace(/<linearGradient[\s\S]*?<\/linearGradient>\s*/g, '')
    .replace(/url\(#[\w-]+-(?:ring|fire)\)/g, color)
    .replace(/stroke="#(?:ffffff|0b0b0f)"/g, `stroke="${color}"`)
    .replace(/<\/title>/, ` — monochrome ${color}</title>`);
}

function pngSize(file) {
  const b = fs.readFileSync(file);
  if (b.slice(1, 4).toString() === 'PNG') return `${b.readUInt32BE(16)}×${b.readUInt32BE(20)}`;
  const m = /viewBox="0 0 (\d+) (\d+)"/.exec(b.toString('utf8', 0, 300));
  return m ? `${m[1]}×${m[2]} (vector)` : '';
}

// ---------- the jobs ----------

const jobs = [];
const lang = (l) => ({ lang: l, dir: l === 'he' ? 'rtl' : 'ltr' });
const shot = (name) => fileUrl(path.join(SCREENS, name));
const brand = (name) => fileUrl(path.join(BRAND, name));
const add = (category, file, spec) => jobs.push({ category, file, out: path.join(OUT, category, file), ...spec });

// Logos. The mark on its two grounds and on nothing; the wordmark; the lockup; the avatar; the monochrome pair.
for (const [theme, mark, wordmark] of [['dark', 'mark.svg', 'wordmark.svg'], ['light', 'mark-light.svg', 'wordmark-light.svg']]) {
  const bodyClass = theme === 'light' ? 'light' : '';
  const copy = { slogan: COPY.en.slogan, tagline: COPY.en.tagline, mark: brand(mark), wordmark: brand(wordmark), ...lang('en') };
  add('logos', `mark-1024-${theme}.png`, { template: 'logo.html', w: 1024, h: 1024, lossless: true, params: { ...copy, layout: 'mark', bodyClass },
    use: theme === 'dark' ? 'The mark on the stage. App listings, press, anywhere the ground is dark' : 'The deeper pair on white, for light grounds (documents, light websites)' });
  add('logos', `mark-1024-${theme}-transparent.png`, { template: 'logo.html', w: 1024, h: 1024, lossless: true, transparent: true, params: { ...copy, layout: 'mark', bodyClass: bodyClass + ' transparent' },
    use: theme === 'dark' ? 'The same mark with no ground, to place on dark photos and videos' : 'The light-ground pair with no ground, to place on light photos and paper' });
  add('logos', `wordmark-2000-${theme}.png`, { template: 'logo.html', w: 2000, h: 560, lossless: true, params: { ...copy, layout: 'wordmark', bodyClass },
    use: `The wordmark, 1840 px wide on a ${theme === 'dark' ? 'stage' : 'white'} ground, for headers and decks` });
  add('logos', `lockup-2400x1200-${theme}.png`, { template: 'logo.html', w: 2400, h: 1200, lossless: true, params: { ...copy, layout: 'lockup', bodyClass },
    use: 'Mark, wordmark, slogan and tagline together: press kits, the first slide, a printed card' });
}
add('logos', 'avatar-1024.png', { template: 'logo.html', w: 1024, h: 1024, lossless: true, params: { layout: 'avatar', bodyClass: '', mark: brand('mark.svg'), wordmark: brand('wordmark.svg'), ...lang('en') },
  use: 'Profile picture for Instagram, TikTok, X, Facebook, LinkedIn: the mark centred on the stage, safe for a circular crop' });
for (const [name, color] of [['white', '#ffffff'], ['black', '#000000']]) {
  for (const [src, base] of [['mark.svg', 'mark'], ['wordmark.svg', 'wordmark']]) {
    const svgFile = `${base}-mono-${name}.svg`;
    add('logos', svgFile, { svg: mono(fs.readFileSync(path.join(BRAND, src), 'utf8'), color),
      use: `${base === 'mark' ? 'The mark' : 'The wordmark'} in one colour (${color}) for print, embroidery, engraving, watermarks` });
    const w = base === 'mark' ? 1024 : 2000, h = base === 'mark' ? 1024 : 431;
    add('logos', `${base}-mono-${name}.png`, { template: 'logo.html', w, h, lossless: true, transparent: true, monoSvg: svgFile,
      params: { layout: base === 'mark' ? 'mark' : 'wordmark', bodyClass: 'transparent', ...lang('en') },
      use: `The same, as a transparent PNG ${w} wide` });
  }
}

// Covers.
const coverParams = (l) => ({ slogan: COPY[l].slogan, tagline: COPY[l].tagline, wordmark: brand('wordmark.svg'), shot: shot(COPY[l].cover), ...lang(l) });
add('covers', 'facebook-linkedin-1500x500.png', { template: 'cover.html', w: 1500, h: 500, params: coverParams('en'), use: 'Facebook page cover (shown 820×312 on desktop, the middle 640 px on phones) and a LinkedIn personal banner' });
add('covers', 'linkedin-1584x396.png', { template: 'cover.html', w: 1584, h: 396, params: coverParams('en'), use: 'LinkedIn company page cover (keep the left 20% clear of anything vital: the logo sits there on phones)' });
add('covers', 'play-feature-1024x500.png', { template: 'cover.html', w: 1024, h: 500, params: coverParams('en'), use: 'Google Play feature graphic (required for the listing; shown above the screenshots)' });
add('covers', 'x-header-1500x500.png', { template: 'cover.html', w: 1500, h: 500, params: coverParams('en'), use: 'X / Twitter header (the profile picture covers the bottom-left corner)' });
add('covers', 'og-1200x630.png', { template: 'cover.html', w: 1200, h: 630, params: coverParams('en'), also: [path.join(WWW, 'brand', 'og-1200x630.png')],
  use: 'Open Graph / Twitter card for links to the app; served at /brand/og-1200x630.png' });
add('covers', 'og-1200x630-he.png', { template: 'cover.html', w: 1200, h: 630, params: coverParams('he'), also: [path.join(WWW, 'brand', 'og-1200x630-he.png')],
  use: 'The same in Hebrew, for the Hebrew landing page; served at /brand/og-1200x630-he.png' });

// Stories.
for (const l of ['en', 'he']) {
  COPY[l].stories.forEach((s, i) => add('stories', `story-${i + 1}-${s.id}-${l}.png`, { template: 'story.html', w: 1080, h: 1920,
    params: { title: s.title, sub: s.sub, cta: COPY[l].cta, shot: shot(s.shot), wordmark: brand('wordmark.svg'), ...lang(l) },
    use: ['The slogan with the check screen', 'The result screen and the one tip', 'A look with a brand tag'][i] + ` (${l === 'he' ? 'Hebrew' : 'English'}), Instagram / TikTok story or Reel cover` }));
}

// Store screenshots.
for (const size of STORE_SIZES) {
  for (const l of ['en', 'he']) {
    COPY[l].store.forEach((s, i) => add('store', `${size.id}-0${i + 1}-${l}.png`, { template: 'store.html', w: size.w, h: size.h,
      params: { caption: s.caption, kicker: `0${i + 1} / 05`, shot: shot(s.shot), ...lang(l) },
      use: `${size.use}, ${l === 'he' ? 'Hebrew' : 'English'} listing, screen ${i + 1}: "${s.caption}"` }));
  }
}

// The landing page's screens: JPEG at the phone viewport, under wwwroot so the page works when served.
for (const l of ['en', 'he']) {
  for (const s of COPY[l].web) {
    jobs.push({ category: 'web', file: `${s.id}-${l}.jpg`, out: path.join(WWW, 'landing', 'screens', `${s.id}-${l}.jpg`),
      template: 'shot.html', w: 780, h: 1688, jpeg: true, params: { shot: shot(s.shot) }, use: 'Landing page' });
  }
}

// ---------- rendering ----------

async function main() {
  const only = process.argv.slice(2).filter((a) => !a.startsWith('-'));
  if (only.length === 1 && only[0] === 'readme') { writeReadme(); console.log('brand-kit/README.md written'); return; }
  const selected = jobs.filter((j) => only.length === 0 || only.includes(j.category));
  if (selected.length === 0) { console.error(`Nothing to do. Categories: ${[...new Set(jobs.map((j) => j.category))].join(' ')} (or readme)`); process.exit(1); }

  // The SVG files first: the mono PNGs are rendered from them.
  for (const j of selected.filter((j) => j.svg)) { fs.mkdirSync(path.dirname(j.out), { recursive: true }); fs.writeFileSync(j.out, j.svg); }

  // Google Fonts when reachable, the local copies otherwise: a blocked request must fail fast, not hang the load event.
  let online = !process.env.BRAND_OFFLINE;
  let sharpNote = sharp ? 'quantised with sharp' : 'as rendered (install sharp for smaller files)';
  const tmp = path.join(TPL, `.render-${process.pid}.html`);
  const cleanup = () => { try { fs.rmSync(tmp, { force: true }); } catch { /* nothing */ } };
  process.on('exit', cleanup); process.on('SIGINT', () => { cleanup(); process.exit(130); });

  let browser = null, page = null;
  async function launch() {
    if (browser) { try { await browser.close(); } catch { /* already gone */ } }
    browser = await playwright.chromium.launch({ ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}) });
    const context = await browser.newContext({ deviceScaleFactor: 1, locale: 'en' });
    if (online) { try { await context.request.get('https://fonts.googleapis.com/css2?family=Outfit:wght@700', { timeout: 6000 }); } catch { online = false; } }
    if (!online) await context.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, (route) => route.abort());
    page = await context.newPage();
  }
  await launch();
  console.log(`fonts: ${online ? 'Google Fonts' : 'local copies (templates/fonts)'}; png: ${sharpNote}`);

  async function renderOnce(j) {
    const params = { ...j.params, w: j.w, h: j.h };
    if (j.monoSvg) params[params.layout === 'mark' ? 'mark' : 'wordmark'] = fileUrl(path.join(OUT, 'logos', j.monoSvg));
    fs.writeFileSync(tmp, fill(fs.readFileSync(path.join(TPL, j.template), 'utf8'), params));
    await page.setViewportSize({ width: j.w, height: j.h });
    await page.goto(fileUrl(tmp), { waitUntil: 'load', timeout: 60000 });
    await page.evaluate(async () => {
      await document.fonts.ready;
      await Promise.all([...document.images].map((i) => i.decode().catch(() => {})));
    });
    let buf = await page.screenshot({ type: j.jpeg ? 'jpeg' : 'png', ...(j.jpeg ? { quality: 84 } : {}), omitBackground: !!j.transparent });
    if (!j.jpeg && !j.lossless && sharp) buf = await sharp(buf).png({ palette: true, quality: 92, effort: 8, compressionLevel: 9 }).toBuffer();
    else if (!j.jpeg && sharp) buf = await sharp(buf).png({ compressionLevel: 9, effort: 8 }).toBuffer();
    return buf;
  }

  let total = 0;
  try {
    for (const j of selected.filter((j) => j.template)) {
      let buf = null;
      // A shared machine sometimes takes the browser away mid-run; relaunch and try the file again, three times.
      for (let attempt = 1; ; attempt++) {
        try { buf = await renderOnce(j); break; } catch (e) {
          const gone = /closed|crashed|Target page|disconnected/i.test(String(e && e.message));
          if (!gone || attempt >= 3) throw e;
          console.warn(`  browser went away while rendering ${j.file}; relaunching (${attempt}/3)`);
          await launch();
        }
      }
      fs.mkdirSync(path.dirname(j.out), { recursive: true });
      fs.writeFileSync(j.out, buf);
      for (const extra of j.also || []) { fs.mkdirSync(path.dirname(extra), { recursive: true }); fs.writeFileSync(extra, buf); }
      total += buf.length;
      console.log(`${path.relative(ROOT, j.out)}  ${j.w}×${j.h}  ${kb(buf.length)}`);
    }
  } finally {
    cleanup();
    try { await browser.close(); } catch { /* already gone */ }
  }
  writeReadme();
  console.log(`done: ${selected.length} files, ${kb(total)}`);
}

// ---------- the README of the kit ----------

function writeReadme() {
  const lines = [];
  lines.push('# OREVOSH brand kit', '',
    'Everything needed to show OREVOSH outside the app: the logos, the covers for social pages and the stores, the story',
    'templates and the store screenshots, rendered from `tools/brand/templates/` by `tools/brand/render-kit.js` with the',
    'real brand SVGs (`src/FitCheck.Api/wwwroot/brand/`), the tokens of `DESIGN.md` and the app screenshots the browser',
    'test takes. The slogan is **Check the look.** / **בודקים את הלוק.** with the tagline *A stylist in your pocket, and a',
    'community that lights it up.* / *סטייליסט בכיס, וקהילה שמדליקה.* (`MARKETING.md` has the runners-up; the copy lives',
    'at the top of `render-kit.js`, so changing it and re-running regenerates every file).', '',
    '## Regenerate', '',
    '```bash', 'cd tools/brand', 'npm install                      # playwright, and sharp for smaller PNGs', 'npx playwright install chromium  # once; or CHROMIUM_PATH=/path/to/chromium',
    'node render-kit.js               # everything; or: node render-kit.js stories store', '```', '',
    'The templates ask Google Fonts for Outfit and Heebo and fall back to the OFL copies in `templates/fonts/` when the',
    'machine is offline, so a run anywhere gives the real faces. This folder is not under `wwwroot` and is not served; the',
    'two files the app serves are copied out by the script: `/brand/og-1200x630.png` (`og-1200x630-he.png` for the Hebrew',
    'landing page) for link previews, and `/landing/screens/*.jpg` for the landing page.', '',
    '## Before the store listings', '',
    '- **Replace the screens.** The screenshots come from the browser test (`tools/e2e`): the outfit photo is the test\'s',
    '  synthetic image, the camera shows Chromium\'s fake device (green), and the check and camera screens exist in English',
    '  only, so the Hebrew store set uses them for screens 1 and 5. Take real captures on a phone (1290×2796 on an iPhone',
    '  with a 6.7" screen; any 9:16 Android phone), drop them into `tools/brand/templates/screens/`, point the `COPY` table',
    '  in `render-kit.js` at them, and re-run. Apple rejects listings whose screenshots do not show the app as shipped.',
    '- **Sizes.** Apple asks for 1284×2778 or 1290×2796 for the 6.7" slot (the files here are 1284×2778, accepted for 6.5"',
    '  and 6.9" too); Google Play accepts 16:9 or 9:16 between 320 and 3840 px and wants the 1024×500 feature graphic.',
    '- **Safe zones.** Facebook shows the cover at 820×312 on desktop and the middle 640 px on phones; LinkedIn puts the',
    '  page logo over the bottom-left of the company cover; X puts the profile picture over the bottom-left of the header;',
    '  Instagram covers the top and bottom 250 px of a story with its own chrome. The layouts keep the wordmark, the slogan',
    '  and the phone inside those, but check once on a phone after uploading.', '');
  const groups = new Map();
  for (const j of jobs.filter((j) => j.category !== 'web')) { if (!groups.has(j.category)) groups.set(j.category, []); groups.get(j.category).push(j); }
  const titles = { logos: 'logos/', covers: 'covers/', stories: 'stories/', store: 'store/' };
  let bytes = 0;
  for (const [cat, list] of groups) {
    lines.push(`## ${titles[cat]}`, '', '| file | pixels | size | use |', '|---|---|---|---|');
    for (const j of list) {
      if (!fs.existsSync(j.out)) continue;
      const size = fs.statSync(j.out).size; bytes += size;
      lines.push(`| \`${j.file}\` | ${pngSize(j.out)} | ${kb(size)} | ${j.use} |`);
    }
    lines.push('');
  }
  lines.push(`Total: ${(bytes / 1048576).toFixed(1)} MB.`, '');
  fs.writeFileSync(path.join(OUT, 'README.md'), lines.join('\n'));
}

main().catch((e) => { console.error(e); process.exit(1); });
