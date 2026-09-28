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
      node tools/brand/render-kit.js stories store   # one or more categories: logos covers stories store social web
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
      { id: 'check-the-look', title: 'Check the look.', sub: '', shot: '07-check-ready-en.png', use: 'The slogan with the check screen' },
      { id: 'the-one-tip', title: '9/10 and the one tip', sub: '“Swap the black tights for sheer brown and the column runs unbroken.”', shot: '08-result-en.png', use: 'The result screen and the one tip' },
      { id: 'brands', title: 'Which brands you wear', sub: 'Tag them with @. They feature the looks they love.', shot: '11-featured-en.png', use: 'A look a brand has just featured' },
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
    cover: '10-post-en.png',
  },
  he: {
    slogan: 'בודקים את הלוק.',
    tagline: 'סטייליסט בכיס, וקהילה שמדליקה.',
    cta: 'בדקו את שלכם. לינק בביו.',
    stories: [
      { id: 'check-the-look', title: 'בודקים את הלוק.', sub: '', shot: '16-home-he.png', use: 'The slogan with the feed' },
      // The Hebrew result is a guest's check of the pink look, 8/10, so the headline and the tip follow that picture.
      { id: 'the-one-tip', title: '8/10 והטיפ האחד', sub: '"שווה להחליף את העניבה המפוספסת בעניבה ורודה חלקה, ולהשאיר את הפסים לחולצה."', shot: '00-guest-result-he.png', use: 'The result screen and the one tip' },
      { id: 'brands', title: 'אילו מותגים אתם לובשים', sub: 'מתייגים עם @. הם מציגים את הלוקים שהם אוהבים.', shot: '14-tag-he.png', use: 'A look with a brand tag' },
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
    cover: '14-tag-he.png',
  },
};
// ---------- the app screens ----------

/* The ten captures in templates/screens/ are the real app (Round 21) at its 390×844 viewport at 2×, 780×1688: the
   three looks of templates/photos/ (the owner's, used with permission) checked, posted and featured through the app's
   own screens, with the stylist's answer written for each photo, so every score, headline, piece and tip on a screen is
   about the clothes in it and a capture goes into its frame as it is. Which look is where:
     look-2-camel       07 the check, 08 its result (9/10), 14 the Hebrew tag page, 13 Explore's look of the week
     look-1-streetwear  10 the look's page, 16 the Hebrew feed (8/10)
     look-3-pink        11 the look NEXOR featured, 12 NEXOR's featured grid, 00 the Hebrew guest result (8/10),
                        20 the in-app camera (Chromium's fake device playing a still of it) */

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

/* The app screen a template shows: the capture, whole, in the box the phone frame gives it (base.css, "the app screen"). */
function screenHtml(file) {
  return `<div class="app"><img class="cap" src="${shot(file)}" alt=""></div>`;
}

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
// A screen with one of the real looks in it must not be palette-quantised: 256 colours band a photograph. Every capture
// has a photograph in it now (a look, a grid of them, the camera's view), so any file that shows a screen stays lossless.
const hasLook = (file) => !!file;
const coverParams = (l) => ({ slogan: COPY[l].slogan, tagline: COPY[l].tagline, wordmark: brand('wordmark.svg'), screen: screenHtml(COPY[l].cover), ...lang(l) });
const coverPhoto = (l) => ({ lossless: hasLook(COPY[l].cover) });
add('covers', 'facebook-linkedin-1500x500.png', { template: 'cover.html', w: 1500, h: 500, ...coverPhoto('en'), params: coverParams('en'), use: 'Facebook page cover (shown 820×312 on desktop, the middle 640 px on phones) and a LinkedIn personal banner' });
add('covers', 'linkedin-1584x396.png', { template: 'cover.html', w: 1584, h: 396, ...coverPhoto('en'), params: coverParams('en'), use: 'LinkedIn company page cover (keep the left 20% clear of anything vital: the logo sits there on phones)' });
add('covers', 'play-feature-1024x500.png', { template: 'cover.html', w: 1024, h: 500, ...coverPhoto('en'), params: coverParams('en'), use: 'Google Play feature graphic (required for the listing; shown above the screenshots)' });
add('covers', 'x-header-1500x500.png', { template: 'cover.html', w: 1500, h: 500, ...coverPhoto('en'), params: coverParams('en'), use: 'X / Twitter header (the profile picture covers the bottom-left corner)' });
add('covers', 'og-1200x630.png', { template: 'cover.html', w: 1200, h: 630, ...coverPhoto('en'), params: coverParams('en'), also: [path.join(WWW, 'brand', 'og-1200x630.png')],
  use: 'Open Graph / Twitter card for links to the app; served at /brand/og-1200x630.png' });
add('covers', 'og-1200x630-he.png', { template: 'cover.html', w: 1200, h: 630, ...coverPhoto('he'), params: coverParams('he'), also: [path.join(WWW, 'brand', 'og-1200x630-he.png')],
  use: 'The same in Hebrew, for the Hebrew landing page; served at /brand/og-1200x630-he.png' });

// Stories.
for (const l of ['en', 'he']) {
  COPY[l].stories.forEach((s, i) => add('stories', `story-${i + 1}-${s.id}-${l}.png`, { template: 'story.html', w: 1080, h: 1920, lossless: hasLook(s.shot),
    params: { title: s.title, sub: s.sub, cta: COPY[l].cta, screen: screenHtml(s.shot), wordmark: brand('wordmark.svg'), ...lang(l) },
    use: `${s.use} (${l === 'he' ? 'Hebrew' : 'English'}), Instagram / TikTok story or Reel cover` }));
}

// Store screenshots.
for (const size of STORE_SIZES) {
  for (const l of ['en', 'he']) {
    COPY[l].store.forEach((s, i) => add('store', `${size.id}-0${i + 1}-${l}.png`, { template: 'store.html', w: size.w, h: size.h, lossless: hasLook(s.shot),
      params: { caption: s.caption, kicker: `0${i + 1} / 05`, screen: screenHtml(s.shot), ...lang(l) },
      use: `${size.use}, ${l === 'he' ? 'Hebrew' : 'English'} listing, screen ${i + 1}: "${s.caption}"` }));
  }
}

// The landing page's screens: JPEG at the phone viewport, under wwwroot so the page works when served.
for (const l of ['en', 'he']) {
  for (const s of COPY[l].web) {
    jobs.push({ category: 'web', file: `${s.id}-${l}.jpg`, out: path.join(WWW, 'landing', 'screens', `${s.id}-${l}.jpg`),
      template: 'shot.html', w: 780, h: 1688, jpeg: true, params: { screen: screenHtml(s.shot) }, use: 'Landing page' });
  }
}

// ---------- the social pack ----------

/* The visual pack for the two accounts (templates/social.html): the profile pictures and the sheet they were
   judged on, the nine-tile grid opener, the first three posts, the TikTok covers and their safe-area guide, the
   link story and the face-reveal card. English first — the account is international and the host never speaks, so
   every word is on-screen text in English; the Hebrew files are the Israeli launch track, same layouts, RTL.
   The follower number on the face-reveal card is not here: it is FACE_REVEAL_TARGET at the top of
   templates/social.html, so one line changes both cards. */

// Which candidate ships as the account's picture. Change it, re-run, and the two avatar files follow.
const AVATAR_PICK = 'c';

const SOCIAL = {
  en: {
    postCta: 'Check yours at orevosh.com.',
    grid: { caps: 'Fit · Color · Accessories', cta: 'orevosh.com', line: 'Fire is the only reaction.' },
    posts: [
      { id: 'what-it-is', title: 'Check the look.', sub: 'Ten seconds. A score out of 10, the breakdown, and the one tip.', shot: '07-check-ready-en.png', use: 'what the app is' },
      { id: 'the-one-tip', title: '9/10 and the one tip', sub: '“Swap the black tights for sheer brown and the column runs unbroken.”', shot: '08-result-en.png', use: 'the result and the one tip' },
      { id: 'brands', title: 'Tag the brands you wear', sub: 'They see it. They feature the looks they love.', shot: '11-featured-en.png', use: 'the brands' },
    ],
    covers: [
      { title: 'Check the look.', sub: '', shot: '07-check-ready-en.png', use: 'the slogan over the check screen' },
      { title: '9/10 and the one tip', sub: 'The score, the breakdown, one thing to change.', shot: '08-result-en.png', use: 'the verdict' },
    ],
    link: { title: 'Link in bio', sub: 'Tap the name at the top.' },
    reveal: { kicker: 'Face reveal', sub: 'Followers. Then the mask comes off.', cta: 'Follow to be there.' },
  },
  he: {
    postCta: 'בודקים את שלכם ב־orevosh.com.',
    posts: [
      { id: 'what-it-is', title: 'בודקים את הלוק.', sub: 'עשר שניות. ציון מתוך 10, הפירוט, והטיפ האחד.', shot: '16-home-he.png', use: 'what the app is' },
      // The Hebrew result is the pink look at 8/10, so the headline and the tip follow the picture (as in stories).
      { id: 'the-one-tip', title: '8/10 והטיפ האחד', sub: '"שווה להחליף את העניבה המפוספסת בעניבה ורודה חלקה, ולהשאיר את הפסים לחולצה."', shot: '00-guest-result-he.png', use: 'the result and the one tip' },
      { id: 'brands', title: 'אילו מותגים אתם לובשים', sub: 'הם רואים. הם מציגים את הלוקים שהם אוהבים.', shot: '14-tag-he.png', use: 'the brands' },
    ],
    covers: [
      { title: 'בודקים את הלוק.', sub: '', shot: '16-home-he.png', use: 'the slogan over the feed' },
      { title: '8/10 והטיפ האחד', sub: 'הציון, הפירוט, ודבר אחד לשנות.', shot: '00-guest-result-he.png', use: 'the verdict' },
    ],
    link: { title: 'לינק בביו', sub: 'לוחצים על השם למעלה.' },
    reveal: { kicker: 'חשיפת פנים', sub: 'עוקבים. ואז המסכה יורדת.', cta: 'עוקבים כדי להיות שם.' },
  },
};

const dataSvg = (svg) => `data:image/svg+xml;base64,${Buffer.from(svg, 'utf8').toString('base64')}`;
const markSvg = fs.readFileSync(path.join(BRAND, 'mark.svg'), 'utf8');
const socialOut = (file) => fileUrl(path.join(OUT, 'social', file));

/* The three profile pictures. `fit` is the share of the tile the mark's own ink circle fills — measured, not the
   512 box: see the note in social.html. A is the reference, B changes only the ground, C only the size. */
const AVATARS = [
  { id: 'a', fit: '.78', ground: 'ground-stage', mark: dataSvg(markSvg),
    use: 'Candidate A "tight": the mark in colour on the stage, its ink circle at 78% of the tile, optically centred — 119 px of margin inside the circular crop' },
  { id: 'b', fit: '.78', ground: 'ground-grad', mark: dataSvg(mono(markSvg, '#ffffff')),
    use: 'Candidate B "gradient ground": the mark in white on the deeper lilac→rose pair, at A\'s size, so the pair only tests the ground — a light tile in a dark feed' },
  { id: 'c', fit: '.88', ground: 'ground-stage', mark: dataSvg(markSvg),
    use: 'Candidate C "ring bleed": the same mark on the stage at 88%, the ring\'s stroke 65 px from the crop\'s edge' },
];
const avatarJob = (a) => ({ template: 'social.html', w: 1080, h: 1080, lossless: true,
  params: { layout: 'avatar', bodyClass: a.ground, fit: a.fit, mark: a.mark, ...lang('en') } });
for (const a of AVATARS) add('social', `avatar-${a.id}.png`, { ...avatarJob(a), use: a.use });
add('social', 'avatar-test.png', { template: 'social.html', w: 1440, h: 1016, lossless: true,
  params: { layout: 'avatartest', bodyClass: '', a: socialOut('avatar-a.png'), b: socialOut('avatar-b.png'), c: socialOut('avatar-c.png'), ...lang('en') },
  use: 'The test the pick was made on: the three files circle-cropped at 150 / 56 / 40 / 32 px on a dark row and on white, and the 32 px crop magnified 4× underneath. Not for posting' });
const winner = AVATARS.find((a) => a.id === AVATAR_PICK);
add('social', 'instagram-avatar-1080.png', { ...avatarJob(winner), use: `The Instagram profile picture: candidate ${AVATAR_PICK.toUpperCase()} (AVATAR_PICK in render-kit.js). Upload at 1080; Instagram stores 320 and shows 150 / 56 / 32` });
add('social', 'tiktok-avatar-1080.png', { ...avatarJob(winner), use: `The TikTok profile picture: the same file (TikTok wants at least 200×200 and crops it to a circle too)` });

/* The grid opener: one 3240×3240 composition, cut into nine tiles. Each tile slides the same composition, so the
   nine files reassemble seamlessly, and the four corners carry a detail of their own so no tile is a wasted post. */
const GRID_TILES = ['the mark, small', 'the three things the stylist reads', 'a score ring',
  'the wordmark\'s own ring and flame', 'the wordmark and the slogan', 'the end of the wordmark',
  'the tagline', 'the CTA', 'the house line on reactions'];
for (let i = 0; i < 9; i++) {
  add('social', `grid-${i + 1}.png`, { template: 'social.html', w: 1080, h: 1080, lossless: true,
    params: { layout: 'grid', bodyClass: '', col: i % 3, row: Math.floor(i / 3), mark: brand('mark.svg'), wordmark: brand('wordmark.svg'),
      slogan: COPY.en.slogan, tagline: COPY.en.tagline, ...SOCIAL.en.grid, ...lang('en') },
    use: `Grid opener ${i + 1} of 9, ${['top', 'middle', 'bottom'][Math.floor(i / 3)]} row ${['start', 'middle', 'end'][i % 3]}: ${GRID_TILES[i]}` });
}
add('social', 'grid-preview.png', { template: 'social.html', w: 1080, h: 1200, lossless: true,
  params: { layout: 'gridpreview', bodyClass: '', ...Object.fromEntries([...Array(9)].map((_, i) => [`t${i + 1}`, socialOut(`grid-${i + 1}.png`)])), ...lang('en') },
  use: 'The nine tiles assembled, with the upload order under them. Not for posting' });

// The first three posts, 1080×1350 (the tallest Instagram shows whole in the feed).
for (const l of ['en', 'he']) {
  SOCIAL[l].posts.forEach((p, i) => add('social', `post-${i + 1}-${p.id}-${l}.png`, { template: 'social.html', w: 1080, h: 1350, lossless: hasLook(p.shot),
    params: { layout: 'post', bodyClass: 'stage', title: p.title, sub: p.sub, cta: SOCIAL[l].postCta, wordmark: brand('wordmark.svg'), screen: screenHtml(p.shot), ...lang(l) },
    use: `First post ${i + 1} (${l === 'he' ? 'Hebrew' : 'English'}): ${p.use}` }));
}

/* The TikTok covers and the guide to the two strips TikTok takes. Both are drawn with physical padding, so the
   Hebrew files keep their words out of the same physical strips as the English ones. */
for (const l of ['en', 'he']) {
  SOCIAL[l].covers.forEach((c, i) => add('social', `tiktok-cover-${i + 1}-${l}.png`, { template: 'social.html', w: 1080, h: 1920, lossless: hasLook(c.shot),
    params: { layout: 'cover', bodyClass: 'stage', title: c.title, sub: c.sub, wordmark: brand('wordmark.svg'), screen: screenHtml(c.shot), ...lang(l) },
    use: `TikTok cover ${i + 1} (${l === 'he' ? 'Hebrew' : 'English'}): ${c.use}. Everything readable is out of the bottom 320 px and the right 180 px` }));
}
add('social', 'tiktok-safe-area.png', { template: 'social.html', w: 1080, h: 1920, lossless: true,
  params: { layout: 'safearea', bodyClass: 'stage', title: SOCIAL.en.covers[0].title, sub: SOCIAL.en.covers[0].sub, wordmark: brand('wordmark.svg'), screen: screenHtml(SOCIAL.en.covers[0].shot), ...lang('en') },
  use: 'The same cover with TikTok\'s two unsafe strips drawn on it: what to keep clear when filming and when making a new cover. Not for posting' });

// The "link in bio" story, and the face-reveal card the masked host promises.
for (const l of ['en', 'he']) {
  add('social', `story-link-${l}.png`, { template: 'social.html', w: 1080, h: 1920, lossless: true,
    params: { layout: 'link', bodyClass: 'stage', title: SOCIAL[l].link.title, sub: SOCIAL[l].link.sub, tagline: COPY[l].tagline, wordmark: brand('wordmark.svg'), ...lang(l) },
    use: `"Link in bio" story (${l === 'he' ? 'Hebrew' : 'English'}): a big arrow up at the profile chip, which sits at the start edge in that direction` });
  add('social', `face-reveal-${l}.png`, { template: 'social.html', w: 1080, h: 1920, lossless: true,
    params: { layout: 'reveal', bodyClass: 'stage', kicker: SOCIAL[l].reveal.kicker, sub: SOCIAL[l].reveal.sub, cta: SOCIAL[l].reveal.cta, wordmark: brand('wordmark.svg'), ...lang(l) },
    use: `The face-reveal promise (${l === 'he' ? 'Hebrew' : 'English'}): the target is the biggest thing on the card. Change FACE_REVEAL_TARGET in templates/social.html to move it` });
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
    // lossless: re-deflated only. No `effort` here: in sharp it switches palette mode on, which is the quantising this branch exists to avoid.
    else if (!j.jpeg && sharp) buf = await sharp(buf).png({ compressionLevel: 9, adaptiveFiltering: true }).toBuffer();
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
    'real brand SVGs (`src/FitCheck.Api/wwwroot/brand/`), the tokens of `DESIGN.md` (the plum stage of Round 21) and ten',
    'screenshots of the real app with the three real looks (`templates/photos/`, the owner\'s, used with permission)',
    'checked, posted and featured in it. The slogan is **Check the look.** / **בודקים את הלוק.** with the tagline',
    '*A stylist in your pocket, and a community that lights it up.* / *סטייליסט בכיס, וקהילה שמדליקה.*',
    '(`MARKETING.md` has the runners-up; the copy lives at the top of `render-kit.js`, so changing it and re-running',
    'regenerates every file).', '',
    '## Regenerate', '',
    '```bash', 'cd tools/brand', 'npm install                      # playwright, and sharp for smaller PNGs (never the ones with a photo in them)', 'npx playwright install chromium  # once; or CHROMIUM_PATH=/path/to/chromium',
    'node render-kit.js               # everything; or: node render-kit.js stories store', '```', '',
    // The episodes are the other renderer's; this note is here so a re-run of this script keeps it in the README.
    'The videos are not part of `render-kit.js`. They have their own script, run **from the repository root**:', '',
    '```bash',
    'node tools/brand/render-episode.js brand-kit/episodes/001-camel.json   # one episode, about half a minute',
    'node tools/brand/render-episode.js 001-camel.json --preview            # a 10-second low-res pass to check timing and copy first',
    'node tools/brand/render-episode.js --all brand-kit/episodes            # every episode in that folder (not its subfolders)',
    'node tools/brand/render-episode.js --all brand-kit/episodes/week-1     # the first week\'s five posts',
    'node tools/brand/render-episode.js 001-camel.json --cover-only         # just the thumbnail, seconds',
    'node tools/brand/render-episode.js --list-checks orevosh-or-20260927.json   # the scored checks in a Settings export, with ids',
    '```', '',
    '`episodes/` is the account\'s weekly material (`CONTENT.md` is the manual): a 1080x1920 MP4 per JSON file, in five',
    'variants — `verdict`, `versus`, `board`, `overlay` (the same beats on a flat chroma green so a filmed clip can be',
    'keyed in behind it), and `before-after`, which takes two of the app\'s own checks — the export from Settings, or two',
    'saved `/api/checks/<id>` answers — and never a retyped number. Every end card carries the address, `orevosh.com`.',
    '`teaser/` holds the two launch films, which are cut by hand and not regenerated from a JSON (they still say *coming*',
    '*soon*; a follow-up). `node render-kit.js` rewrites this README from its file list, so this paragraph lives in',
    '`writeReadme` in the script.', '',
    'The templates ask Google Fonts for Outfit and Heebo and fall back to the OFL copies in `templates/fonts/` when the',
    'machine is offline, so a run anywhere gives the real faces. This folder is not under `wwwroot` and is not served; the',
    'two files the app serves are copied out by the script: `/brand/og-1200x630.png` (`og-1200x630-he.png` for the Hebrew',
    'landing page) for link previews, and `/landing/screens/*.jpg` for the landing page.', '',
    '## Before the store listings', '',
    '- **Replace the screens.** The ten screenshots are the real app (Round 21) at the phone viewport, taken in a browser',
    '  against a local server: the people, the brand, the counts and the timestamps are a test run\'s, the stylist is the',
    '  test stub answering each of the three looks with a verdict written for that photo, and the camera is Chromium\'s',
    '  fake device playing a still of the pink look. The check and camera screens exist in English only, so the Hebrew',
    '  store set uses them for screens 1 and 5. Take real captures on a',
    '  phone (1290×2796 on an iPhone with a 6.7" screen; any 9:16 Android phone), drop them into',
    '  `tools/brand/templates/screens/`, point the `COPY` table in `render-kit.js` at them, and re-run. Apple rejects',
    '  listings whose screenshots do not show the app as shipped.',
    '- **The looks.** `look-2-camel` is the check, its result (9/10), the Hebrew tag page and Explore\'s look of the week;',
    '  `look-1-streetwear` is the look\'s page and the Hebrew feed (8/10); `look-3-pink` is the look NEXOR featured, the',
    '  Hebrew guest result (8/10) and the camera. The app crops an outfit to 4:5 from the centre, so each gives something',
    '  up on a card: the camel look its face and the boots at the hem, the grey one its sneakers, the pink one the top of',
    '  the fedora and the shoes.',
    '- **Sizes.** Apple asks for 1284×2778 or 1290×2796 for the 6.7" slot (the files here are 1284×2778, accepted for 6.5"',
    '  and 6.9" too); Google Play accepts 16:9 or 9:16 between 320 and 3840 px and wants the 1024×500 feature graphic.',
    '- **Safe zones.** Facebook shows the cover at 820×312 on desktop and the middle 640 px on phones; LinkedIn puts the',
    '  page logo over the bottom-left of the company cover; X puts the profile picture over the bottom-left of the header;',
    '  Instagram covers the top and bottom 250 px of a story with its own chrome. The layouts keep the wordmark, the slogan',
    '  and the phone inside those, but check once on a phone after uploading.', '');
  const groups = new Map();
  for (const j of jobs.filter((j) => j.category !== 'web')) { if (!groups.has(j.category)) groups.set(j.category, []); groups.get(j.category).push(j); }
  const titles = { logos: 'logos/', covers: 'covers/', stories: 'stories/', store: 'store/', social: 'social/' };
  /* The one category with rules of its own: an upload order, two safe strips and a number in the template. */
  const prose = {
    social: [
      'The pack for the two accounts, English first: the account is international and the host never speaks, so every',
      'word is on-screen text in English and the Hebrew files are the Israeli launch track (`SOCIAL` at the top of the',
      'social block in `render-kit.js`, same layouts, RTL).', '',
      '- **The profile picture.** `instagram-avatar-1080.png` and `tiktok-avatar-1080.png` are the same file, candidate',
      `  ${AVATAR_PICK.toUpperCase()} (\`AVATAR_PICK\` in \`render-kit.js\`). Both apps crop to a circle, so the mark is sized and hung by its own ink`,
      '  circle — measured at (304.25, 234.5) r 251.184 in the 512 box, up and right of the box\'s centre because the flame',
      '  flies out of the ring\'s top-right — not by the square it is drawn in. `avatar-test.png` is the sheet the pick was',
      '  made on: the three candidates circle-cropped at 150 / 56 / 40 / 32 px on a dark row and on white, and the 32 px',
      '  crop magnified 4× under them. `avatar-a/b/c.png` are kept so the choice can be re-made by eye.', '',
      '- **The grid opener.** `grid-1.png` … `grid-9.png` are one 3240×3240 composition cut into nine.',
      '  **Upload `grid-9` first, then 8, 7 … down to `grid-1`**: a profile stacks newest first, so uploaded in that order',
      '  they land 1 2 3 across the top row and the wordmark reads straight across the middle. `grid-preview.png` shows',
      '  the assembly. Every tile also stands alone — the corners carry the mark, a score ring, the tagline and the line',
      '  about fire — so none of the nine is a wasted post.', '',
      '- **TikTok\'s safe area.** TikTok covers the bottom 320 px with the caption, the handle, the sound bar and the',
      '  buttons, and the right 180 px with the action rail. The covers, the link story and the face-reveal card keep every',
      '  word and the logo inside a 700 px column centred on the canvas (x 190 → 890) with 210 px of air on top and 392 px',
      '  at the bottom, in physical padding, so the Hebrew files clear the same strips as the English ones.',
      '  `tiktok-safe-area.png` draws those two strips over a real cover — it is a guide, not a post.', '',
      '- **The face reveal.** The follower target is one constant, `FACE_REVEAL_TARGET`, at the top of',
      '  `templates/social.html`. Change that line and re-run `node render-kit.js social` to re-cut both cards at a',
      '  different number. Announce the number up front and keep to it.', '',
      '- Instagram posts are 1080×1350, the tallest it shows whole in the feed; a square crop of one of them loses the CTA,',
      '  so post them as they are.', '',
    ],
  };
  let bytes = 0;
  for (const [cat, list] of groups) {
    lines.push(`## ${titles[cat]}`, '');
    if (prose[cat]) lines.push(...prose[cat]);
    lines.push('| file | pixels | size | use |', '|---|---|---|---|');
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

// Required from elsewhere (a check of one screen, say), the file only offers its pieces and renders nothing.
if (require.main === module) main().catch((e) => { console.error(e); process.exit(1); });
module.exports = { COPY, screenHtml, jobs };
