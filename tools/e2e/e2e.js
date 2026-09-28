// Browser smoke test for OREVOSH: the real client and the real API in a phone-sized Chromium, with only the
// Anthropic API stubbed (stub_anthropic.py).
// Run from this folder: npm install && node e2e.js   (after `dotnet build` at the repository root).
//
// Three people: Noa (a person, English), NEXOR (a brand, English) and Dan (mostly browsing, Hebrew). The run covers:
// browsing signed out in Hebrew and RTL, signup and the welcome screen (interests, brands), settings (brand mode,
// avatar upload), a check with the client-side downscale, posting with #tags and @mentions, the post page with tagged
// accounts and comments, mention and featured notifications, a brand featuring a look, the brand's Community and
// Featured tabs, Explore (trending tags, brands, top looks, search, tag page), the For you feed, double-tap to fire,
// a challenge from brief to winner, the PWA manifest and service worker, photo privacy, and deleting a look and an account.
const { chromium } = require('playwright');
const { spawn, execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const http = require('http');
const assert = require('assert');

const ROOT = path.resolve(__dirname);
const API_PORT = 5088;
const STUB_PORT = 5099;
const DATA = path.join(ROOT, 'data');
const SHOTS = path.join(ROOT, 'shots');
const REPO = path.resolve(__dirname, '../../src/FitCheck.Api');
const DB = path.join(DATA, 'e2e.db');

fs.rmSync(DATA, { recursive: true, force: true });
fs.rmSync(SHOTS, { recursive: true, force: true });
fs.mkdirSync(DATA, { recursive: true });
fs.mkdirSync(SHOTS, { recursive: true });

function get(url, headers) {
  return new Promise((resolve, reject) => {
    http.get(url, { headers }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) }));
    }).on('error', reject);
  });
}
const getJson = async (url) => JSON.parse((await get(url)).body.toString('utf8'));

async function waitFor(url, tries = 60) {
  for (let i = 0; i < tries; i++) {
    try { const r = await get(url); if (r.status < 500) return; } catch (e) { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error('server did not start: ' + url);
}

const procs = [];
function start(cmd, args, env, log) {
  const out = fs.openSync(log, 'w');
  const p = spawn(cmd, args, { env: { ...process.env, ...env }, stdio: ['ignore', out, out] });
  procs.push(p);
  return p;
}

const base = `http://127.0.0.1:${API_PORT}`;
const API_ENV = {
  ASPNETCORE_URLS: `http://127.0.0.1:${API_PORT}`,
  ANTHROPIC_API_KEY: 'stub-key-not-real',
  Anthropic__BaseUrl: `http://127.0.0.1:${STUB_PORT}`,
  ConnectionStrings__Default: `Data Source=${DB}`,
  Storage__Root: path.join(DATA, 'storage'),
  Email__Host: 'log',
  // Round 20: the prompt cache on, so every check and comparison is asserted to carry the breakpoint (and no compose).
  Anthropic__PromptCache: '5m',
  Plans__FreeChecksPerDay: '5',
  // Round 20: the morning push is offered by this server (so /api/config publishes it and Settings draws its switch), but
  // push itself has no keys here and the browser blocks service workers, so nothing is ever sent: the run proves the
  // client half and the never-spend rule; the sender is TomorrowMorningTests' to prove.
  Plans__TomorrowMorningPush: 'true',
  // Round 20: the stub names three pieces per language on every check, so the free slice is two to make the Pro moment
  // ("your wardrobe has 3 pieces; Pro lets the stylist see all of them") reachable by one free account in one check.
  // Nothing else in the run reads the number: noa is Pro by then, and no assertion quotes the "first {free}" text.
  Plans__WardrobeNamesToStylist: '2',
  // Round 19: the forecast comes from the stub too (GET /v1/forecast), so Tomorrow dresses for a weather nobody dialled.
  Weather__BaseUrl: `http://127.0.0.1:${STUB_PORT}`,
  Board__NewAccountDays: '0',
  Board__MinChecksToCount: '1',
  Board__CacheSeconds: '0',
  // Round 20: a sponsor of the week, so the board carries one and the moderator's read-only card on #/admin has something
  // to say. The link is a bare host on purpose: the card proves the https normalisation the server does at start.
  Board__Sponsor__Name: 'NEXOR',
  Board__Sponsor__Handle: 'nexor',
  Board__Sponsor__PrizeText: 'A jacket from the new drop',
  Board__Sponsor__Url: 'nexor.example',
  Email__From: 'OREVOSH <noreply@example.test>',
};
let apiProc = null;
let apiLog = path.join(DATA, 'api.log');
/** Round 20: the billing leg needs Stripe keys the rest of the run must not have, so the API is stopped and started again with more environment. */
async function restartApi(extraEnv, log) {
  const old = apiProc;
  const gone = new Promise((resolve) => old.once('exit', resolve));
  old.kill('SIGTERM');
  await gone;
  // Wait until the port really refuses, or the new process would lose the bind and the run would hit the dying one.
  for (let i = 0; i < 60; i++) {
    try { await get(`${base}/api/config`); } catch (e) { break; }
    await new Promise((r) => setTimeout(r, 250));
  }
  apiLog = log;
  apiProc = start('dotnet', ['run', '--no-build', '--project', REPO], { ...API_ENV, ...extraEnv }, log);
  await waitFor(`${base}/api/config`);
}
const consoleErrors = [];
const consoleWarnings = [];
const failedUrls = [];
const expected = [];          // "METHOD /path -> status" entries that a step deliberately provokes
const noContent = new Set();
const pages = {};
let step = 'boot';

// Round 20: `extra.userAgent` makes the context another app's webview (the in-app-browser note), and `extra.standalone`
// makes it the installed app as core.js sees it (navigator.standalone, the home-screen launch header).
async function person(browser, name, locale, extra = {}) {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale, serviceWorkers: 'block', ...(extra.userAgent ? { userAgent: extra.userAgent } : {}) });
  if (extra.standalone) await context.addInitScript(() => Object.defineProperty(navigator, 'standalone', { get: () => true }));
  await context.grantPermissions(['camera', 'microphone'], { origin: base });
  const page = await context.newPage();
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(`[${step} ${name}] ` + m.text()); if (m.type() === 'warning') consoleWarnings.push(m.text()); });
  page.on('pageerror', (e) => consoleErrors.push(`[${step} ${name}] pageerror: ` + e.message));
  page.on('requestfailed', (r) => failedUrls.push(`[${step} ${name}] ${r.method()} ${r.url()} -> ${r.failure() && r.failure().errorText}`));
  page.on('response', (r) => {
    if (r.status() === 204) noContent.add(r.request().method() + ' ' + r.url());
    if (r.status() >= 400) failedUrls.push(`[${step} ${name}] ${r.request().method()} ${r.url().replace(base, '')} -> ${r.status()}`);
  });
  page.on('dialog', (d) => d.accept());
  pages[name] = page;
  return page;
}

const settled = '#view h1, #view .card, #view .empty, #view .notice, #view .grid, #view .sheet, #view .person, #view form';
async function go(page, hash) {
  if (!page.url().startsWith(base)) {
    await page.goto(base + '/' + hash);
  } else if (await page.evaluate(() => location.hash) === hash) {
    // Same route again: a hashchange re-renders it (what tapping the active tab does for a tab route).
    await page.evaluate(() => { window.dispatchEvent(new HashChangeEvent('hashchange')); });
  } else {
    await page.evaluate((h) => { location.hash = h; }, hash);
  }
  await page.waitForFunction((h) => location.hash === h || (h === '#/' && location.hash === ''), hash);
  await page.waitForSelector(settled, { timeout: 15000 });
}
const hash = (page) => page.evaluate(() => location.hash);
const text = (page, sel) => page.textContent(sel).then((s) => (s || '').trim());
const count = async (page, sel) => (await page.$$(sel)).length;
const me = async (page) => { const r = await page.request.get(base + '/api/auth/me'); return r.ok() ? await r.json() : null; };
/** Extra environment for the maintenance commands; the billing leg (Round 20) sets the Stripe keys here so --stripe-check reads them. */
let maintenanceEnv = {};
/** Runs one of the app's maintenance commands (--admin, --unadmin, --backup) against the test database, as an owner would on the box. */
function maintenance(...args) {
  return execFileSync('dotnet', ['run', '--no-build', '--project', REPO, '--', ...args], {
    env: { ...process.env, ConnectionStrings__Default: `Data Source=${DB}`, Storage__Root: path.join(DATA, 'storage'), ANTHROPIC_API_KEY: 'stub-key-not-real', ...maintenanceEnv }
  }).toString();
}
/** The pilot metrics are for moderators; read them through a promoted person's session. */
const metricsAs = async (page) => { const r = await page.request.get(base + '/api/metrics/pilot'); assert.strictEqual(r.status(), 200, 'metrics as a moderator'); return r.json(); };

async function signup(page, handle, password) {
  await go(page, '#/signup');
  await page.waitForSelector('#a-handle');
  await page.fill('#a-handle', handle);
  await page.fill('#a-password', password);
  await page.fill('#a-dob', '1990-01-01');
  await page.click('#a-submit');
  await page.waitForFunction(() => location.hash === '#/welcome');
  await page.waitForSelector(settled);
}

function makeJpeg(page, w, h) {
  return page.evaluate(([w, h]) => {
    const c = document.createElement('canvas'); c.width = w; c.height = h;
    const g = c.getContext('2d');
    g.fillStyle = '#e8e2d6'; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 400; i++) { g.fillStyle = `hsl(${(i * 37) % 360} 40% ${30 + (i % 50)}%)`; g.fillRect((i * 97) % (w - 100), (i * 131) % (h - 100), 120, 160); }
    return c.toDataURL('image/jpeg', 0.92).split(',')[1];
  }, [w, h]).then((b64) => Buffer.from(b64, 'base64'));
}

/** The photo button opens a file chooser (pickFile clicks the hidden input); answer it with a JPEG buffer. */
async function choosePhoto(page, buttonSelector, buffer, name) {
  // The check screen's photo button opens the media sheet (camera / library / clip); the library row is the picker.
  if (buttonSelector === '#photo') {
    await page.click('#photo');
    await page.waitForSelector('#media-library');
    buttonSelector = '#media-library';
  } else if (buttonSelector === '#cmp-slot-a' || buttonSelector === '#cmp-slot-b') {
    // Round 20: a compare slot opens its own sheet (camera / library); the library row is the picker.
    await page.click(buttonSelector);
    await page.waitForSelector('#cmp-media-library');
    buttonSelector = '#cmp-media-library';
  }
  const [chooser] = await Promise.all([page.waitForEvent('filechooser'), page.click(buttonSelector)]);
  if (await page.$('.sheet')) await page.waitForFunction(() => !document.querySelector('.sheet'));
  await chooser.setFiles({ name: name || 'outfit.jpg', mimeType: 'image/jpeg', buffer });
}

async function runCheck(page, opts) {
  await go(page, '#/check');
  await page.waitForSelector('#photo');
  if (opts.intent) await page.click(`.chip[data-intent=${opts.intent}]`);
  await choosePhoto(page, '#photo', opts.buffer);
  await page.waitForSelector('#photo img');
  await page.waitForFunction(() => !document.getElementById('submit').disabled);
  if (opts.occasion) await page.fill('#occasion', opts.occasion);
  if (opts.beforeSubmit) await opts.beforeSubmit();
  await page.click('#submit');
  await page.waitForSelector('#result .score', { timeout: 30000 });
  await page.waitForFunction((s) => document.querySelector('#result .score').textContent === s, String(opts.score), { timeout: 5000 });
}

async function postIt(page, opts) {
  await page.click('#post-open');
  await page.waitForSelector('#post-confirm');
  if (opts.caption) await page.fill('#caption', opts.caption);
  if (opts.products) {
    const inputs = await page.$$('.products-grid input');
    assert.strictEqual(inputs.length, 9, 'three product rows for a brand');
    for (const [i, p] of opts.products.entries()) {
      await inputs[i * 3].fill(p.label);
      await inputs[i * 3 + 1].fill(p.url);
      if (p.price) await inputs[i * 3 + 2].fill(p.price);
    }
  }
  await page.click('#post-confirm');
  await page.waitForSelector('#post-link');
  return (await page.getAttribute('#post-link', 'href')).replace('#/post/', '');
}

// Step 0, before anything is spawned: every client module is parsed on its own. A module the browser cannot parse takes
// the whole app down with it — the page stays blank and the first real step dies of a selector timeout thirty seconds
// later, naming nothing. This names the file and the line. `node --check` reads .js as a script, so each one is copied to
// a .mjs first; that is the only way to get import/export parsed without a dependency.
function checkClientModules() {
  const root = path.join(REPO, 'wwwroot');
  const files = [];
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.js')) files.push(full);
    }
  })(root);

  const scratch = path.join(DATA, 'parse.mjs');
  const broken = [];
  for (const file of files) {
    fs.copyFileSync(file, scratch);
    try {
      execFileSync(process.execPath, ['--check', scratch], { stdio: ['ignore', 'ignore', 'pipe'] });
    } catch (e) {
      const said = (e.stderr || '').toString().split('\n').find((l) => l.includes('Error:')) || 'did not parse';
      broken.push(path.relative(root, file) + ' — ' + said.trim());
    }
  }
  fs.rmSync(scratch, { force: true });
  assert.strictEqual(broken.length, 0, 'client modules the browser cannot parse:\n  ' + broken.join('\n  '));

  // Node and Chromium parse things an iPhone does not, and a regex literal an engine cannot parse is a SYNTAX error in
  // the module holding it — the app never starts, on that phone, with no message. Node cannot catch these for us, so
  // they are read out of the source. The version beside each one is the Safari that learned it; the oldest iPhone worth
  // supporting is the oldest one still sold or still getting security fixes, which is well below 16.4.
  const TOO_NEW = [
    [/\(\?<[=!]/, 'a regex lookbehind — Safari 16.4; an iPhone 7 stops at iOS 15'],
    [/\/[dgimsuy]*v[dgimsuy]*(?=[;,)\s.])/, "a regex v flag — Safari 17"],
    [/^\s*static\s*\{/m, 'a static initialisation block — Safari 16.4'],
  ];
  const tooNew = [];
  for (const file of files) {
    if (file.includes(path.sep + 'vendor' + path.sep)) continue;   // third-party, shipped as published
    const source = fs.readFileSync(file, 'utf8');
    for (const [pattern, why] of TOO_NEW) {
      const line = source.split('\n').findIndex((l) => pattern.test(l));
      if (line >= 0) tooNew.push(path.relative(root, file) + ':' + (line + 1) + ' uses ' + why);
    }
  }
  assert.strictEqual(tooNew.length, 0, 'client code an older iPhone cannot parse (the app would open blank there):\n  ' + tooNew.join('\n  '));

  for (const file of files.concat(fs.readdirSync(path.join(root, 'i18n')).map((f) => path.join(root, 'i18n', f)))) {
    if (!file.endsWith('.json')) continue;
    try { JSON.parse(fs.readFileSync(file, 'utf8')); } catch (e) { assert.fail(path.relative(root, file) + ' is not JSON: ' + e.message); }
  }
  return files.length;
}

(async () => {
  console.log('0. client modules parse: ' + checkClientModules() + ' files');
  start('python3', [path.join(ROOT, 'stub_anthropic.py'), String(STUB_PORT), base], {}, path.join(DATA, 'stub.log'));
  apiProc = start('dotnet', ['run', '--no-build', '--project', REPO], API_ENV, apiLog);

  await waitFor(`http://127.0.0.1:${STUB_PORT}/`);
  await waitFor(`${base}/api/metrics/pilot`);
  const stubState = await getJson(`http://127.0.0.1:${STUB_PORT}/`);
  assert.deepStrictEqual(stubState, [], `port ${STUB_PORT} is served by a stale stub with ${stubState.length} recorded requests; kill it first`);

  // A fake camera and microphone stand in for the phone's, so the in-app camera and the clip recorder run for real.
  const browser = await chromium.launch({ ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}), args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream'] });
  const noa = await person(browser, 'noa', 'en-US');
  const brand = await person(browser, 'brand', 'en-US');
  const dan = await person(browser, 'dan', 'he-IL');
  const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, name + '.png'), fullPage: true });

  step = '1';
  // 1. PWA surface and a signed-out visitor with a Hebrew browser: the app opens in ENGLISH whatever the phone is set to
  // (Round 16 - the audience does not share one language, so the front door is the one most of them have), offers that
  // browser its own language once, and becomes Hebrew and RTL when the offer is taken. Then: wordmark, empty feed,
  // Explore, sign-in prompts.
  const manifest = await get(`${base}/manifest.webmanifest`);
  assert.strictEqual(manifest.status, 200);
  assert.strictEqual(JSON.parse(manifest.body.toString()).name, 'OREVOSH');
  assert.strictEqual((await get(`${base}/sw.js`)).status, 200);
  assert.strictEqual((await get(`${base}/icons/icon-192.png`)).headers['content-type'], 'image/png');
  expected.push('GET /api/auth/me -> 401');
  await dan.goto(base + '/');
  await dan.waitForSelector(settled);
  // English first, on a he-IL browser, with nothing saved.
  assert.strictEqual(await dan.getAttribute('html', 'lang'), 'en');
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'ltr');
  assert.strictEqual(await text(dan, '.tab[data-tab=home] span'), 'Home');
  // The offer is written in the language it offers, and carries that language's direction, not the app's.
  await dan.waitForSelector('#lang-offer');
  assert.strictEqual(await dan.getAttribute('#lang-offer', 'dir'), 'rtl');
  assert.strictEqual(await text(dan, '#lang-offer-yes'), 'עברית');
  await shot(dan, '01a-language-offer');
  await dan.click('#lang-offer-yes');
  await dan.waitForSelector('html[lang=he]');
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'rtl');
  assert.strictEqual(await dan.$('#lang-offer'), null, 'the offer goes once it is answered');
  assert.strictEqual(await text(dan, '.wordmark'), 'OREVOSH');
  assert.strictEqual(await dan.getAttribute('meta[name="apple-mobile-web-app-capable"]', 'content'), 'yes');
  assert.strictEqual(await text(dan, '.tab[data-tab=home] span'), 'בית');
  // And it is not asked again: the choice is saved, so a reload comes straight back in Hebrew.
  await dan.reload();
  await dan.waitForSelector(settled);
  assert.strictEqual(await dan.getAttribute('html', 'lang'), 'he');
  assert.strictEqual(await dan.$('#lang-offer'), null, 'answered once, never asked again');
  assert.strictEqual(await text(dan, '.tab[data-tab=explore] span'), 'גילוי');
  assert.strictEqual(await text(dan, '#top-auth'), 'הצטרפות');
  await dan.waitForSelector('#view .empty');
  await dan.waitForSelector('#feed-empty #feed-empty-check');   // Round 12: an empty feed tells the newcomer the one thing to do
  await shot(dan, '01-home-empty-he');
  await go(dan, '#/explore');
  assert.strictEqual(await text(dan, '#view h1'), 'גילוי');
  await dan.waitForSelector('#search');
  await dan.waitForSelector('#explore-empty #explore-empty-check');
  await shot(dan, '02-explore-empty-he');
  // Round 12: the board with no week yet also tells the newcomer what to do.
  await go(dan, '#/board');
  await dan.waitForSelector('#board-empty #board-empty-check');
  // A visitor checks first and signs up later: one guest check, then the result offers to keep it.
  await go(dan, '#/check');
  await dan.waitForSelector('#guest-banner');
  assert.strictEqual(await text(dan, '#guest-banner h3'), 'קודם מנסים');
  await dan.click('.chip[data-intent=Casual]');
  await choosePhoto(dan, '#photo', await makeJpeg(dan, 900, 1200));
  await dan.waitForFunction(() => !document.getElementById('submit').disabled);
  await dan.click('#submit');
  await dan.waitForSelector('#result .score', { timeout: 30000 });
  await dan.waitForSelector('#guest-keep');
  assert.strictEqual(await count(dan, '#post-open'), 0, 'a guest cannot post');
  await shot(dan, '00-guest-result-he');
  // Round 12: the result becomes a 12-second vertical video drawn and encoded on the device. This Chromium has no H.264
  // encoder, so the VP9/WebM rung runs here; phones take the MP4 rung. Saving it counts once on the server (204).
  await dan.click('#share-video');
  await dan.waitForSelector('#sv-progress');
  await dan.waitForSelector('#sv-video[data-path="vp9-webm"]', { timeout: 120000 });
  const svBytes = Number(await dan.getAttribute('#sv-video', 'data-bytes'));
  assert.ok(svBytes > 50000 && svBytes < 6 * 1024 * 1024, 'the video is a real file under 6 MB: ' + svBytes);
  const counted = dan.waitForResponse((r) => r.url().endsWith('/shared-video') && r.request().method() === 'POST');
  await dan.click('#sv-save');
  assert.strictEqual((await counted).status(), 204, 'the save is tallied once');
  await dan.waitForSelector('#toast');
  await shot(dan, '42-share-video-he');
  await dan.keyboard.press('Escape');
  await dan.waitForFunction(() => !document.querySelector('.sheet'));
  expected.push('POST /api/checks -> 429');
  const secondGuest = await dan.request.post(base + '/api/checks', { headers: { 'X-Requested-With': 'Orevosh' }, multipart: { intent: 'Casual', language: 'he', image: { name: 'outfit.jpg', mimeType: 'image/jpeg', buffer: await makeJpeg(dan, 300, 400) } } });
  assert.strictEqual(secondGuest.status(), 429, 'one free look per guest');
  await go(dan, '#/feed/following');
  assert.strictEqual(await text(dan, '.notice h3'), 'צריך להתחבר בשביל זה');

  step = '2';
  // 2. Noa signs up (handle, password, 16+ only) and lands on the welcome screen: styles, no brands yet, done.
  await noa.goto(base + '/');
  await noa.waitForSelector(settled);
  assert.strictEqual(await noa.getAttribute('html', 'lang'), 'en');
  await go(noa, '#/signup');
  await noa.waitForSelector('#a-handle');
  assert.strictEqual(await count(noa, '#a-brand'), 0, 'no brand question at signup');
  assert.strictEqual(await count(noa, '#a-name'), 0, 'no display name at signup');
  assert.strictEqual(await count(noa, '#a-guidelines'), 1, 'the community guidelines are one tap from signup');
  await noa.fill('#a-handle', 'noa');
  await noa.fill('#a-password', 'password123');
  await shot(noa, '03-signup-en');
  expected.push('POST /api/auth/signup -> 400');
  expected.push('POST /api/auth/signup -> 400');
  await noa.click('#a-submit');
  await noa.waitForSelector('form .alert:not([hidden])');
  assert.strictEqual(await text(noa, 'form .alert'), 'Add your date of birth.');
  assert.strictEqual(await count(noa, '#a-age'), 0, 'the 16+ checkbox is gone: the date decides');
  assert.strictEqual(await count(noa, '#a-agree #a-terms'), 1, 'terms are one tap from signup');
  assert.strictEqual(await count(noa, '#a-agree #a-privacy'), 1, 'privacy is one tap from signup');
  await noa.fill('#a-dob', '2015-01-01');
  await noa.click('#a-submit');
  await noa.waitForFunction(() => document.querySelector('form .alert') && !document.querySelector('form .alert').hidden && document.querySelector('form .alert').textContent.includes('16'));
  await noa.fill('#a-dob', '1990-01-01');
  await noa.click('#a-submit');
  await noa.waitForFunction(() => location.hash === '#/welcome');
  await noa.waitForSelector('.chip[data-intent]');
  assert.strictEqual(await text(noa, '#view h1'), 'Welcome to OREVOSH');
  await noa.click('.chip[data-intent=Date]');
  await noa.click('.chip[data-intent=Streetwear]');
  await shot(noa, '04-welcome-en');
  await noa.click('button:has-text("Take me in")');
  await noa.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await noa.waitForSelector(settled);
  assert.deepStrictEqual((await me(noa)).interests.sort(), ['Date', 'Streetwear']);
  assert.strictEqual(await noa.getAttribute('.tab[data-tab=home]', 'aria-current'), 'page');
  // The owner makes Noa a moderator with the --admin command, after the account exists; the client sees it on its next load.
  assert.ok(/noa/.test(maintenance('--admin', 'noa')), '--admin reports the handle');
  await noa.reload();
  await noa.waitForSelector(settled);
  assert.strictEqual((await me(noa)).isAdmin, true, 'promoted');

  step = '3';
  // 3. NEXOR signs up, skips the welcome, becomes a brand in settings and uploads an avatar.
  await brand.goto(base + '/');
  await brand.waitForSelector(settled);
  await signup(brand, 'nexor', 'password123');
  await brand.click('button:has-text("Skip")');
  await brand.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await go(brand, '#/settings');
  await brand.waitForSelector('#s-save');
  await brand.fill('#s-name', 'NEXOR');
  await brand.fill('#s-web', 'https://nexor.example');
  await brand.click('#s-save');
  await brand.waitForFunction(() => document.getElementById('s-save') && !document.getElementById('s-save').disabled);

  // Round 17: a brand account is granted, not taken. The box is there for everybody and the server refuses the
  // switch until the owner has verified the account - because "Brand" puts a company's name in front of other
  // people, on the Explore front page and above every person in search. Somebody could have typed a real shop's
  // name and been at the top of the app in one tap. This is the real flow a brand goes through now.
  expected.push('PATCH /api/users/me -> 403');
  await brand.check('#s-brand');
  await brand.click('#s-save');
  await brand.waitForSelector('form .alert:not([hidden])');
  assert.strictEqual((await me(brand)).accountType, 'Person', 'the box alone does not make a brand');
  assert.ok(/nexor/.test(maintenance('--verify', 'nexor')), '--verify reports the handle');
  await brand.reload();
  await brand.waitForSelector(settled);
  await go(brand, '#/settings');
  await brand.waitForSelector('#s-save');
  await brand.check('#s-brand');
  await brand.click('#s-save');
  await brand.waitForFunction(() => document.getElementById('s-save') && !document.getElementById('s-save').disabled);
  await brand.waitForFunction(async () => true);
  assert.ok(/nexor/.test(maintenance('--admin', 'nexor')), 'a second moderator, for the metrics after Noa leaves');
  const brandMe = await me(brand);
  assert.strictEqual(brandMe.accountType, 'Brand');
  assert.strictEqual(brandMe.name, 'NEXOR');
  const avatarJpeg = await makeJpeg(brand, 600, 800);
  await choosePhoto(brand, 'button:has-text("Change photo")', avatarJpeg, 'me.jpg');
  await brand.waitForFunction(() => !!document.querySelector('#view .avatar img'));
  const avatarUrl = await brand.getAttribute('#view .avatar img', 'src');
  assert.match(avatarUrl, /^\/api\/users\/nexor\/avatar\?v=\d+$/, avatarUrl);
  const avatarResponse = await get(base + avatarUrl);
  assert.strictEqual(avatarResponse.status, 200);
  assert.strictEqual(avatarResponse.headers['content-type'], 'image/jpeg');
  assert.ok(avatarResponse.headers['cache-control'].includes('public'));
  await shot(brand, '05-settings-brand-en');
  await go(brand, '#/me');
  await brand.waitForSelector('.profile-head');
  await brand.waitForSelector('#profile-empty #profile-empty-check');   // Round 12: a profile with no looks yet says what to do
  assert.strictEqual(await text(brand, '.profile-head .brand-mark'), 'Brand');
  assert.ok(await brand.$('.profile-head .avatar img'), 'avatar shown on the profile');
  assert.strictEqual(await count(brand, '.profile-tabs button'), 3, 'brands get Looks, Community and Featured');
  await shot(brand, '06-profile-brand-en');

  step = '4';
  // 4. Noa checks a look and posts it with a #tag and an @mention of the brand.
  const bigJpeg = await makeJpeg(noa, 1800, 2400);
  assert.strictEqual((await getJson(`${base}/api/config`)).plans.freeChecksPerDay, 5);
  await go(noa, '#/check');
  await noa.waitForSelector('#checks-left');
  assert.strictEqual(await text(noa, '#checks-left'), '5 of 5 checks left today');
  assert.strictEqual(await count(noa, '#which-one'), 1, 'the comparison is one tap from the check');
  await runCheck(noa, { intent: 'Date', occasion: 'dinner with friends', buffer: bigJpeg, score: 7, beforeSubmit: () => shot(noa, '07-check-ready-en') });
  assert.strictEqual(await text(noa, '.result-headline'), 'Clean casual with one weak link');
  assert.deepStrictEqual(await noa.$$eval('.item-verdict', (n) => n.map((x) => x.textContent)), ['Works', 'Neutral', 'Weak']);
  assert.strictEqual(await noa.getAttribute('.bar', 'aria-valuenow'), '72');
  // Rubric v2: three rings and the accessories read, with the one piece that would finish the look.
  assert.deepStrictEqual(await noa.$$eval('#breakdown ul.breakdown li .score-badge b', (n) => n.map((x) => x.textContent)), ['7', '8', '4']);
  assert.deepStrictEqual(await noa.$$eval('#breakdown .breakdown-label', (n) => n.map((x) => x.textContent)), ['Fit', 'Color', 'Accessories']);
  assert.strictEqual(await text(noa, '#accessories .acc-verdict'), 'No accessories');
  assert.strictEqual(await count(noa, '#accessories .acc-verdict.missing'), 1);
  assert.ok((await text(noa, '#accessories .acc-add')).includes('A thin black leather belt.'));
  await shot(noa, '08-result-en');
  await noa.click('#post-open');
  await noa.waitForSelector('#post-confirm');
  // Items: the stylist's pieces are rows on the sheet; the running shoes carry a brand guess that is never sent unconfirmed.
  await noa.waitForSelector('#items-editor');
  assert.ok((await count(noa, '#items-list > li.items-row[data-source=Stylist]')) >= 2, 'the stylist\'s items are rows');
  await noa.waitForSelector('.items-suggest[data-brand="Nike"]:not([hidden])');
  const shoesKey = await noa.$eval('.items-suggest[data-brand="Nike"]', (n) => n.closest('li.items-row').dataset.key);
  await noa.click('.items-suggest[data-brand="Nike"] button[data-action=confirm]');
  await noa.waitForFunction((k) => /Nike/.test(document.querySelector('li.items-row[data-key="' + k + '"] .txt').textContent), shoesKey);
  await noa.click('li.items-row[data-key="' + shoesKey + '"] button.items-place');
  await noa.waitForSelector('#items-photo.placing');
  const box = await noa.$eval('#items-photo', (n) => { const r = n.getBoundingClientRect(); return { x: r.left + r.width * 0.5, y: r.top + r.height * 0.75 }; });
  await noa.mouse.click(box.x, box.y);
  await noa.waitForSelector('li.items-row.placed');
  await shot(noa, '35-items-editor-en');
  assert.strictEqual(await count(noa, '.products-grid'), 0, 'people do not get product links');
  await noa.fill('#caption', 'Dinner fit, thoughts? #datenight #DateNight @nexor @nobody');
  await shot(noa, '09-post-sheet-en');
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const post1 = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
  await go(noa, '#/post/' + post1);
  await noa.waitForSelector('#look-items:not([hidden]) li[data-item]');
  const taggedShoes = await noa.$eval('#look-items li[data-item] button.look-item.placed .txt', (n) => n.textContent);
  assert.ok(/Nike/.test(taggedShoes), 'the confirmed brand shows on the look: ' + taggedShoes);
  await noa.waitForSelector('#items-toggle');
  await noa.click('#items-toggle');
  await noa.waitForSelector('#item-dots:not([hidden]) .item-dot[data-item]');
  await noa.click('#item-dots .item-dot[data-item]');
  await noa.waitForSelector('#item-sheet');
  assert.ok((await text(noa, '#item-sheet')).includes('Nike'), 'the item sheet names the brand');
  await shot(noa, '36-item-sheet-en');
  await noa.keyboard.press('Escape');
  await noa.waitForSelector('.sheet', { state: 'detached' });
  // The item pages: looks with Nike, and the brands list.
  const nikeLooks = await (await get(base + '/api/items?brand=nike')).body.toString('utf8');
  assert.ok(nikeLooks.includes(post1), 'the look is found by its brand');
  const brands = JSON.parse((await get(base + '/api/items/brands?q=ni')).body.toString('utf8'));
  assert.ok(brands.items.some((b) => b.name === 'Nike'), 'Nike is a brand people wear');
  await go(noa, '#/items/Nike');
  await noa.waitForSelector('#view .grid a, #view .card');
  await shot(noa, '37-items-page-en');
  await go(noa, '#/post/' + post1);
  await noa.waitForSelector('#view .card');
  assert.strictEqual(await text(noa, '.card .headline'), 'Clean casual with one weak link');
  // @nobody is not an account, so it stays plain text; the tags and the brand become links.
  assert.deepStrictEqual(await noa.$$eval('.card .caption a', (n) => n.map((x) => x.getAttribute('href'))), ['#/tag/datenight', '#/tag/datenight', '#/u/nexor']);
  assert.ok((await text(noa, '.card .caption')).includes('@nobody'));
  await noa.waitForSelector('.person');
  assert.strictEqual(await text(noa, '.person .name'), 'NEXORBrand', 'tagged brand listed');
  assert.strictEqual(await text(noa, '.card .score-badge'), '7/10');
  assert.deepStrictEqual(await noa.$$eval('#post-breakdown .score-badge b', (n) => n.map((x) => x.textContent)), ['7', '8', '4'], 'the breakdown is public with the score');
  await shot(noa, '10-post-en');

  step = '5';
  // 5. The brand is told, features the look, and its Community and Featured tabs fill up; Noa is told back.
  await go(brand, '#/activity');
  await brand.waitForSelector('.activity li a[href]');
  assert.deepStrictEqual(await brand.$$eval('.activity li a[href] > div:first-child', (n) => n.map((x) => x.textContent)), ['noa tagged you in a look']);
  await go(brand, '#/post/' + post1);
  await brand.waitForSelector('.menu-open');
  await brand.click('.menu-open');
  await brand.waitForSelector('.sheet');
  await brand.click('.sheet button:has-text("Feature this look")');
  await brand.waitForSelector('.card .featured');
  assert.strictEqual(await text(brand, '.card .featured'), 'Featured by NEXOR');
  await brand.click('.menu-open');
  await brand.waitForSelector('.sheet');
  assert.strictEqual(await count(brand, '.sheet button:has-text("Remove from featured")'), 1);
  await brand.keyboard.press('Escape');
  await brand.waitForFunction(() => !document.querySelector('.sheet'));
  await shot(brand, '11-featured-en');
  await go(brand, '#/u/nexor/community');
  await brand.waitForSelector('.grid a');
  assert.strictEqual(await count(brand, '.grid a'), 1);
  assert.strictEqual(await brand.getAttribute('.profile-tabs button:nth-child(2)', 'aria-selected'), 'true');
  await go(brand, '#/u/nexor/featured');
  await brand.waitForSelector('.grid a');
  assert.strictEqual(await count(brand, '.grid a'), 1);
  await shot(brand, '12-brand-community-en');
  await go(noa, '#/activity');
  await noa.waitForSelector('.activity li a[href]');
  assert.deepStrictEqual(await noa.$$eval('.activity li a[href] > div:first-child', (n) => n.map((x) => x.textContent)), ['NEXOR featured your look']);
  await go(noa, '#/u/noa');
  await noa.waitForSelector('.profile-tabs');
  assert.strictEqual(await count(noa, '.profile-tabs button'), 2, 'a featured person gets a Featured tab');
  // Round 21, settled in review: on her own card the author reads the stylist's word on each piece as its dot.
  await go(noa, '#/tag/datenight');
  await noa.waitForSelector('#view .card .card-photo .chip.piece');
  assert.ok((await count(noa, '.card .card-photo .chip.piece .dot')) > 0, 'the author sees the verdict dots');
  assert.strictEqual(await count(noa, '.card .card-photo .chip.piece .dot'), await count(noa, '.card .card-photo .chip.piece'), 'a dot on each of the stylist\'s pieces');

  step = '6';
  // 6. Explore, signed out: trending tag, the brand, the top look, search, the tag page.
  await go(dan, '#/explore');
  await dan.waitForSelector('a[href="#/tag/datenight"]');
  assert.ok((await text(dan, 'a[href="#/tag/datenight"]')).includes('#datenight'));
  await dan.waitForSelector('.brand-card');
  assert.strictEqual(await text(dan, '.brand-card .name'), 'NEXOR');
  assert.strictEqual(await count(dan, 'a.x-hero'), 1, 'the week\'s top look is the Explore hero');
  await shot(dan, '13-explore-he');
  await dan.fill('#search', 'nex');
  await dan.press('#search', 'Enter');
  await dan.waitForFunction(() => location.hash === '#/search/nex');
  await dan.waitForSelector('.person');
  assert.strictEqual(await text(dan, '.person .name'), 'NEXORמותג');
  await go(dan, '#/search/date');
  await dan.waitForSelector('a[href="#/tag/datenight"]');
  await go(dan, '#/tag/datenight');
  await dan.waitForSelector('#view .card');
  assert.strictEqual(await count(dan, '#view .card'), 1);
  // Somebody else's look: the pieces are named on the photo, and the stylist's word on each stays with its author.
  assert.ok((await count(dan, '.card .card-photo .chip.piece')) > 0, 'the pieces are named on the card');
  assert.strictEqual(await count(dan, '.card .card-photo .chip.piece .dot, .card .card-photo .chip.piece[data-verdict]'), 0, 'no verdict reaches a stranger');
  assert.strictEqual(await text(dan, '.card .featured'), 'הוצג על ידי NEXOR');
  await shot(dan, '14-tag-he');

  step = '7';
  // 7. Dan signs up in Hebrew, follows the brand from the welcome screen, sees the look in For you, double-taps to fire.
  await signup(dan, 'dan', 'password123');
  const claimed = await dan.request.get(base + '/api/users/me/checks').then((r) => r.json());
  assert.strictEqual(claimed.length, 1, 'the guest check now belongs to dan');
  await dan.waitForSelector('.person');
  assert.strictEqual(await text(dan, '.person .name'), 'NEXORמותג');
  await dan.click('.person .btn');
  await dan.waitForFunction(() => document.querySelector('.person .btn').getAttribute('aria-pressed') === 'true');
  await dan.click('.chip[data-intent=Date]');
  await shot(dan, '15-welcome-he');
  await dan.click('button:has-text("קחו אותי פנימה")');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await dan.waitForSelector('#view .card');
  assert.strictEqual(await count(dan, '#view .card'), 1);
  assert.strictEqual(await text(dan, '.segment[aria-pressed="true"]'), 'בשבילך');
  const photo = await dan.$('.card .card-photo');
  await photo.tap(); await photo.tap();
  await dan.waitForFunction(() => document.querySelector('.card .action.fire').getAttribute('aria-pressed') === 'true');
  assert.strictEqual(await text(dan, '.card .action.fire .count'), '1');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '', null, { timeout: 2000 });
  assert.ok(!(await hash(dan)).startsWith('#/post/'), 'double tap must not open the look');
  await shot(dan, '16-home-he');
  await go(dan, '#/feed/following');
  await dan.waitForSelector('#view .empty');
  await dan.waitForSelector('#feed-empty #feed-empty-check');
  await dan.waitForSelector('#feed-empty #feed-empty-find');   // Round 12: Your circle also offers to find people and brands
  await go(dan, '#/u/noa');
  await dan.waitForSelector('#follow, .profile-head');
  await dan.click('#follow');
  await dan.waitForFunction(() => !!document.querySelector('button[aria-pressed="true"].btn'));
  await go(dan, '#/feed/following');
  await dan.waitForSelector('#view .card');

  step = '8';
  // 8. A challenge from brief to winner, now reached through Explore.
  await go(brand, '#/explore');
  await brand.waitForSelector('a[href="#/challenges"]');
  await brand.click('a[href="#/challenges"]');
  await brand.waitForSelector('a[href="#/new-challenge"]');
  await brand.click('a[href="#/new-challenge"]');
  await brand.waitForSelector('#nc-submit');
  await brand.fill('#nc-title', 'Date night in black');
  assert.strictEqual(await brand.inputValue('#nc-tag'), '#datenightinblack', 'the hashtag follows the title');
  await brand.fill('#nc-tag', '#blackdate');
  await brand.click('.chip:has-text("Date")');
  await brand.fill('#nc-brief', 'All-black date looks. Texture over logos.');
  await brand.fill('#nc-prize', 'A black shirt of your choice');
  await brand.click('#nc-submit');
  await brand.waitForFunction(() => /^#\/challenge\/[0-9a-f-]{36}$/.test(location.hash));
  const challengeId = (await hash(brand)).replace('#/challenge/', '');
  await brand.waitForSelector('.challenge-title');
  assert.strictEqual(await text(brand, '.challenge-meta a.tag.accent'), '#blackdate');
  await shot(brand, '17-challenge-en');
  // Entering is the hashtag: the button pre-fills it in the caption, no picker, no intent lock.
  await go(noa, '#/challenge/' + challengeId);
  await noa.waitForSelector('button[data-enter]');
  assert.strictEqual(await text(noa, 'button[data-enter]'), 'Post a look with #blackdate');
  await noa.click('button[data-enter]');
  await noa.waitForSelector('#photo');
  assert.strictEqual(await noa.isDisabled('.chip[data-intent=Casual]'), false, 'any intent can enter');
  await runCheck(noa, { buffer: bigJpeg, score: 7 });
  await noa.click('#post-open');
  await noa.waitForSelector('#post-confirm');
  assert.strictEqual(await noa.inputValue('#caption'), '#blackdate ', 'the hashtag is pre-filled');
  assert.strictEqual(await count(noa, '#challenge-pick'), 0, 'no challenge picker');
  await noa.fill('#caption', '#blackdate Black on black');
  // "After the tip": her one earlier look is offered; picking it marks this look as the follow-up.
  await noa.waitForSelector('#after-picker:not([hidden])');
  assert.strictEqual(await count(noa, '#after-picker .after-opt.look'), 1, 'one earlier look to follow up on');
  assert.strictEqual(await noa.getAttribute('#after-picker .after-opt.none', 'aria-checked'), 'true', 'not a follow-up by default');
  await noa.click('#after-picker .after-opt.look');
  await noa.waitForFunction(() => document.querySelector('#after-picker .after-opt.look').getAttribute('aria-checked') === 'true');
  await shot(noa, '33-after-picker-en');
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const post2 = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
  await go(noa, '#/post/' + post2);
  await noa.waitForSelector('a.after-strip');
  assert.strictEqual(await noa.getAttribute('a.after-strip', 'href'), '#/post/' + post1, 'the strip links the earlier look');
  assert.ok((await text(noa, 'a.after-strip')).startsWith('After the tip'), 'the strip names the follow-up');
  await go(dan, '#/challenge/' + challengeId);
  await dan.waitForSelector('.lb-row .vote');
  await dan.click('.lb-row .vote');
  await dan.waitForFunction(() => document.querySelector('.lb-row .vote') && document.querySelector('.lb-row .vote').getAttribute('aria-pressed') === 'true');
  await shot(dan, '18-vote-he');
  const changed = execFileSync('python3', ['-c', [
    'import sqlite3, sys',
    'c = sqlite3.connect(sys.argv[1])',
    "n = c.execute(\"update Challenges set EndsAt = strftime('%Y-%m-%d %H:%M:%S', 'now', '-1 hour')\").rowcount",
    'c.commit(); print(n)'].join('\n'), DB]).toString().trim();
  assert.strictEqual(changed, '1');
  await go(dan, '#/challenge/' + challengeId);
  await dan.waitForSelector('.lb-row.winner');
  assert.strictEqual(await count(dan, '.card[data-post="' + post2 + '"]'), 1, 'winner card shown');
  await go(noa, '#/activity');
  await noa.waitForSelector('.activity li a[href]');
  assert.ok((await noa.$$eval('.activity li a[href] > div:first-child', (n) => n.map((x) => x.textContent))).includes("You won NEXOR's challenge"));
  // The brand features the winning entry through the challenge route (no mention needed).
  await go(brand, '#/post/' + post2);
  await brand.waitForSelector('.menu-open');
  await brand.click('.menu-open');
  await brand.waitForSelector('.sheet');
  await brand.click('.sheet button:has-text("Feature this look")');
  await brand.waitForSelector('.card .featured');

  step = 'try-the-tip';
  // Round 20 — the wedge. Noa's posted challenge look is still the result in hand: the primary next action after the
  // change tip is "Try the tip, then show me", the Round 13 yes/no row is gone, and there is no pair share until there
  // is a pair. The tap arms the attempt and lands on the check screen with the media sheet open — one tap from the
  // camera; the second check is an ordinary check (the stub sees nothing of the first); the pair is written afterwards.
  const noaChecks = await noa.request.get(base + '/api/users/me/checks').then((r) => r.json());
  const beforeCheck = noaChecks.find((c) => c.postId === post2).id;
  await go(noa, '#/result');
  await noa.waitForSelector('#tried-start');
  assert.strictEqual(await text(noa, '#tried-start'), 'Try the tip, then show me');
  assert.ok(await noa.$eval('#tried-start', (n) => n.classList.contains('btn') && !n.classList.contains('btn-secondary')), 'the primary treatment');
  assert.strictEqual(await count(noa, '#taste-reasons .chip[data-reason]'), 4, 'the four typed answers');
  assert.strictEqual(await count(noa, '#useful-yes'), 0, 'the yes/no row asked the same thing twice; it is gone');
  assert.strictEqual(await count(noa, '#share-pair'), 0, 'no pair share before there is a pair');
  await shot(noa, '52-try-the-tip-en');
  await noa.click('#tried-start');
  await noa.waitForFunction(() => location.hash === '#/check');
  await noa.waitForSelector('#media-camera');
  assert.strictEqual(await count(noa, '#media-library'), 1, 'the media sheet is open on arrival');
  const armed = await noa.evaluate(() => JSON.parse(sessionStorage.getItem('orevosh.tried') || 'null'));
  assert.strictEqual(armed && armed.beforeId, beforeCheck, 'the attempt names the first check');
  await noa.keyboard.press('Escape');
  await noa.waitForFunction(() => !document.querySelector('.sheet'));
  await choosePhoto(noa, '#photo', bigJpeg);
  await noa.waitForSelector('#photo img');
  await noa.waitForFunction(() => !document.getElementById('submit').disabled);
  await noa.click('#submit');
  await noa.waitForSelector('#result .score', { timeout: 30000 });
  await noa.waitForFunction(() => document.querySelector('#result .score').textContent === '7', null, { timeout: 5000 });
  // "Is this the look after the change?" — yes: the pair, both sides.
  await noa.waitForSelector('.sheet:has-text("Is this the look after the change?")');
  await noa.click('.sheet button:has-text("Yes, that\'s it")');
  await noa.waitForSelector('#tried-pair .pair-side[data-side=before]');
  assert.strictEqual(await count(noa, '#tried-pair .pair-side[data-side=after]'), 1);
  const lastCheckRequest = (await getJson(`http://127.0.0.1:${STUB_PORT}/`)).filter((r) => r.tool === 'submit_outfit_feedback').at(-1);
  assert.ok(!lastCheckRequest.user_text.includes(beforeCheck), 'the second check carries no id of the first');
  assert.ok(!lastCheckRequest.user_text.includes('Swap the running shoes'), 'nor the first tip');
  // The pair share leads the row; the card is drawn from the private before photo and the judged still; saving is tallied.
  assert.strictEqual(await noa.$eval('.share-row', (n) => n.firstElementChild && n.firstElementChild.id), 'share-pair', 'the pair share is first');
  await noa.click('#share-pair');
  await noa.waitForSelector('#before-after-sheet');
  await noa.waitForFunction(() => { const imgs = [...document.querySelectorAll('.ba-pair img')]; return imgs.length === 2 && imgs.every((i) => i.complete && i.naturalWidth > 0); }, null, { timeout: 15000 });
  assert.ok((await noa.$$eval('.ba-pair img', (n) => n.map((i) => i.getAttribute('src')))).some((src) => src === `/api/checks/${beforeCheck}/image`), 'the before is the private check photo');
  await noa.click('#ba-card');
  await noa.waitForSelector('#sc-card');
  await noa.waitForFunction(() => { const i = document.getElementById('sc-card'); return !!i && i.complete && i.naturalWidth > 0; }, null, { timeout: 30000 });
  const pairCounted = noa.waitForResponse((r) => r.url().endsWith('/tried/shared') && r.request().method() === 'POST');
  await noa.click('#sc-save');
  assert.strictEqual((await pairCounted).status(), 204, 'the pair share is tallied once');
  await shot(noa, '53-pair-share-en');
  await noa.keyboard.press('Escape');
  await noa.waitForFunction(() => !document.querySelector('.sheet'));
  const loop = (await metricsAs(noa)).stylist;
  assert.strictEqual(loop.triedPairs, 1, 'one pair on the numbers page');
  assert.strictEqual(typeof loop.triedPer100Ok, 'number', 'pairs per hundred ok checks is a number');
  // Posting the after: the earlier look is already picked, and the public page shows the pair.
  await noa.click('#post-open');
  await noa.waitForSelector('#post-confirm');
  await noa.waitForSelector('#after-picker:not([hidden])');
  await noa.waitForFunction((p) => { const o = document.querySelector('#after-picker .after-opt.look[data-post="' + p + '"]'); return !!o && o.getAttribute('aria-checked') === 'true'; }, post2);
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const postAfter = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
  const pairPage = (await get(`${base}/look/${postAfter}`)).body.toString('utf8');
  assert.ok(pairPage.includes('class="pair"'), 'the public page shows the pair');
  assert.ok(pairPage.includes(`/look/${post2}/image`), 'with the earlier look\'s photo');
  // The nudge is hourly and clock-based (TryTipNudgeTests own it); the list still answers, and the settings copy says what push sends.
  assert.strictEqual((await noa.request.get(base + '/api/notifications')).status(), 200);
  await go(noa, '#/settings');
  await noa.waitForSelector('label[for="s-push"] .hint');
  assert.strictEqual(await text(noa, 'label[for="s-push"] .hint'), "A ping when your look catches fire, someone follows you, a brand features you, or a day after a tip you haven't tried yet.");

  step = '9';
  // 9. Photos: the post image route and the avatar route are the only doors; nothing under storage is reachable by path.
  assert.strictEqual((await get(`${base}/api/posts/${post1}/image`)).status, 200);
  const storage = path.join(DATA, 'storage');
  const files = [];
  for (const user of fs.readdirSync(storage)) for (const f of fs.readdirSync(path.join(storage, user))) files.push(user + '/' + f);
  assert.strictEqual(files.length, 5, 'four OK checks (one claimed from a guest, one the look after the tip) plus one avatar: ' + files.join(','));
  for (const rel of files) {
    for (const url of [`${base}/${rel}`, `${base}/storage/${rel}`, `${base}/wwwroot/${rel}`]) {
      assert.strictEqual((await get(url)).status, 404, `photo reachable at ${url}`);
    }
  }
  assert.strictEqual((await get(`${base}/api/metrics/pilot`)).status, 401, 'the pilot metrics need a session');
  assert.strictEqual((await dan.request.get(base + '/api/metrics/pilot')).status(), 403, 'and a moderator');
  const metrics = await metricsAs(noa);
  assert.strictEqual(metrics.social.mentions, 1);
  assert.strictEqual(metrics.social.featured, 2);
  assert.strictEqual(metrics.social.brands, 1);

  step = '10';
  // 10. The in-app camera (a fake device here): a photo, then a clip in clip mode; the frame is picked, the stylist judges
  //     that still, the story card draws, the clip posts, streams with Range, and plays in the feed with its pill.
  await go(noa, '#/check');
  await noa.waitForSelector('#photo');
  await noa.click('#photo');
  await noa.waitForSelector('#media-camera');
  await noa.click('#media-camera');
  await noa.waitForSelector('.cam[data-phase="live"]', { timeout: 20000 });
  assert.strictEqual(await hash(noa), '#/camera');
  assert.strictEqual(await noa.isVisible('.tabbar'), false, 'the dock stays out of the viewfinder');
  await shot(noa, '19-camera-en');
  await noa.click('.cam-shutter');
  await noa.waitForSelector('.cam[data-phase="preview"]');
  await noa.click('#cam-use');
  await noa.waitForFunction(() => location.hash === '#/check');
  await noa.waitForSelector('#photo.has-image img');
  await noa.click('#photo');
  await noa.waitForSelector('#media-camera');
  await noa.click('#media-camera');
  await noa.waitForSelector('.cam[data-phase="live"]', { timeout: 20000 });
  await noa.click('.cam-modes button:nth-child(2)');
  await noa.click('.cam-shutter');
  await noa.waitForSelector('.cam[data-phase="recording"]');
  await noa.waitForTimeout(1600);
  await shot(noa, '20-recording-en');
  await noa.click('.cam-shutter');
  await noa.waitForSelector('.cam[data-phase="preview"]', { timeout: 20000 });
  await noa.click('#cam-use');
  await noa.waitForFunction(() => location.hash === '#/check');
  await noa.waitForSelector('#photo.has-clip video');
  await noa.waitForSelector('#clip-frame');
  await noa.click('.chip[data-intent=Streetwear]');
  // Pick a frame in the middle of the clip: the slider seeks, and the change captures that frame as the still.
  await noa.$eval('#clip-frame', (range) => {
    range.value = String(Math.round(Number(range.max) / 2));
    range.dispatchEvent(new Event('input', { bubbles: true }));
    range.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await noa.waitForFunction(() => !document.getElementById('submit').disabled, null, { timeout: 15000 });
  await shot(noa, '21-check-clip-en');
  await noa.click('#submit');
  await noa.waitForSelector('#result .score', { timeout: 30000 });
  await noa.click('#share-card');
  await noa.waitForSelector('#sc-card');
  await noa.waitForFunction(() => { const i = document.getElementById('sc-card'); return !!i && i.complete && i.naturalWidth === 1080 && i.naturalHeight === 1920; }, null, { timeout: 30000 });
  assert.ok((await noa.getAttribute('#sc-save', 'href')).startsWith('blob:'), 'the card is a saved image');
  await shot(noa, '22-share-card-en');
  await noa.keyboard.press('Escape');
  await noa.waitForFunction(() => !document.querySelector('.sheet'));
  const post3 = await postIt(noa, { caption: 'Filmed in the app #clip' });
  const clipPost = await noa.request.get(base + '/api/posts/' + post3).then((r) => r.json());
  assert.strictEqual(clipPost.videoUrl, '/api/posts/' + post3 + '/video');
  const clipHead = await noa.request.get(base + clipPost.videoUrl, { headers: { Range: 'bytes=0-3' } });
  assert.strictEqual(clipHead.status(), 206, 'a <video> seeks with Range');
  assert.match(clipHead.headers()['content-range'], /^bytes 0-3\/\d+$/);
  assert.match(clipHead.headers()['content-type'], /^video\//);
  assert.strictEqual((await get(`${base}${clipPost.videoUrl}`)).status, 200, 'a public look\'s clip plays signed out');
  // ffmpeg is on this machine, so the worker re-encodes the WebM to H.264 MP4 at the same URL within seconds.
  assert.strictEqual((await getJson(`${base}/api/config`)).transcoding, true, 'transcoding is on');
  const transcodeStart = Date.now();
  let clipType = '';
  while (Date.now() - transcodeStart < 60000) {
    clipType = (await get(`${base}${clipPost.videoUrl}`)).headers['content-type'] || '';
    if (clipType.startsWith('video/mp4')) break;
    await new Promise((r) => setTimeout(r, 1000));
  }
  assert.ok(clipType.startsWith('video/mp4'), 'the clip became MP4: ' + clipType);
  const mp4Bytes = (await get(`${base}${clipPost.videoUrl}`)).body;
  assert.strictEqual(mp4Bytes.subarray(4, 8).toString('latin1'), 'ftyp', 'an MP4 container');
  // Home keeps its last list for ten minutes; a reload is the honest way to see what was posted since.
  await go(dan, '#/');
  await dan.reload();
  await dan.waitForSelector('.card.has-clip', { timeout: 30000 });
  assert.strictEqual(await count(dan, '.card.has-clip .clip-pill'), 1, 'clips are marked');
  assert.ok(await dan.$('.card.has-clip video[poster]'), 'the picked frame is the poster');
  assert.strictEqual(await count(dan, '.card.has-clip .card-media .sound'), 1, 'a sound toggle');
  await shot(dan, '23-clip-card-he');
  await go(dan, '#/u/noa');
  await dan.waitForSelector('.grid a');
  assert.strictEqual(await count(dan, '.grid a.is-clip'), 1, 'the grid marks the clip');
  const clipFiles = [];
  for (const user of fs.readdirSync(storage)) for (const f of fs.readdirSync(path.join(storage, user))) clipFiles.push(user + '/' + f);
  assert.strictEqual(clipFiles.length, 7, 'the clip and its still joined the five photos: ' + clipFiles.join(','));
  assert.ok(clipFiles.some((f) => /\.mp4$/.test(f)) && !clipFiles.some((f) => /\.webm$/.test(f)), 'the MP4 replaced the WebM on disk: ' + clipFiles.join(','));
  for (const rel of clipFiles.filter((f) => /\.(webm|mp4)$/.test(f))) {
    assert.strictEqual((await get(`${base}/${rel}`)).status, 404, `clip reachable at /${rel}`);
  }
  assert.strictEqual((await metricsAs(noa)).social.videos, 1);

  // "Which one?" (Round 20: the check's two questions, slot A from the library and slot B through the in-app camera,
  //  a close call from the stub), then the insights over Noa's checks and a search by piece.
  await go(noa, '#/compare');
  await noa.waitForSelector('#occasions');
  await noa.click('.chip[data-occasion=Party]');
  await noa.click('.chip[data-style=Minimal]');
  await noa.fill('#cmp-occasion', 'close call on a rooftop');
  await choosePhoto(noa, '#cmp-slot-a', await makeJpeg(noa, 800, 1000), 'a.jpg');
  await noa.waitForSelector('#cmp-slot-a.has-image img');
  await noa.click('#cmp-slot-b');
  await noa.waitForSelector('#cmp-media-camera');
  await noa.click('#cmp-media-camera');
  await noa.waitForSelector('.cam[data-phase="live"]', { timeout: 20000 });
  assert.strictEqual(await hash(noa), '#/camera');
  assert.strictEqual(await noa.isVisible('.cam-modes'), false, 'no clip mode on a compare slot');
  await noa.click('.cam-shutter');
  await noa.waitForSelector('.cam[data-phase="preview"]');
  await noa.click('#cam-use');
  await noa.waitForFunction(() => location.hash === '#/compare');
  await noa.waitForSelector('#cmp-slot-b.has-image img');
  assert.strictEqual(await noa.getAttribute('.chip[data-occasion=Party]', 'aria-pressed'), 'true', 'the occasion survived the camera');
  assert.strictEqual(await noa.getAttribute('.chip[data-style=Minimal]', 'aria-pressed'), 'true', 'the style survived the camera');
  assert.strictEqual(await noa.inputValue('#cmp-occasion'), 'close call on a rooftop', 'the note survived the camera');
  await noa.waitForFunction(() => !document.getElementById('cmp-submit').disabled);
  await noa.click('#cmp-submit');
  await noa.waitForSelector('#cmp-result .cmp-winner', { timeout: 30000 });
  assert.strictEqual(await noa.getAttribute('#cmp-result .cmp-winner', 'data-side'), 'b');
  assert.match(await text(noa, '#cmp-asked'), /Party.*Minimal/, 'the verdict names both questions');
  assert.match(await text(noa, '.cmp-verdict'), /^Both work/, 'a 7 and a 7 reads as a close call');
  await shot(noa, '28-compare-en');
  // The old one-word shape is gone from the wire: the stylist read the Party guide and the Minimal guide.
  const compareRequest = (await getJson(`http://127.0.0.1:${STUB_PORT}/`)).filter((r) => r.tool === 'pick_outfit').at(-1);
  assert.ok(compareRequest && compareRequest.user_text.includes('Party:') && compareRequest.user_text.includes('Minimal: few pieces'), 'the compare carried the two guides: ' + (compareRequest && compareRequest.user_text));

  // The refusal that sells Pro: a fresh free account spends its day (Plans__FreeChecksPerDay is 5: four checks and a
  // compare), and the next compare is refused with the code the screen acts on - the sentence, the nudge naming the
  // published number of comparisons a day, and Go Pro saying where it came from. The Pro page counts the open.
  const lior = await person(browser, 'lior', 'en-US');
  await signup(lior, 'lior', 'password123');
  const liorPhoto = await makeJpeg(lior, 800, 1000);
  for (let i = 0; i < 4; i++) await runCheck(lior, { intent: 'Party', buffer: liorPhoto, score: 7 });
  const compareAs = async (page) => {
    await go(page, '#/compare');
    await page.waitForSelector('#occasions');
    await page.click('.chip[data-occasion=Party]');
    await choosePhoto(page, '#cmp-slot-a', liorPhoto, 'a.jpg');
    await choosePhoto(page, '#cmp-slot-b', liorPhoto, 'b.jpg');
    await page.waitForFunction(() => !document.getElementById('cmp-submit').disabled);
    await page.click('#cmp-submit');
  };
  await compareAs(lior);
  await lior.waitForSelector('#cmp-result .cmp-winner', { timeout: 30000 });
  expected.push('POST /api/compare -> 429');
  await compareAs(lior);
  await lior.waitForSelector('#cmp-error:not([hidden])');
  await lior.waitForSelector('#cmp-pro-nudge:not([hidden])');
  const proComparesPerDay = (await getJson(`${base}/api/config`)).plans.proComparesPerDay;
  assert.ok(proComparesPerDay > 0, 'this server gives Pro a number of comparisons a day');
  assert.ok((await text(lior, '#cmp-pro-nudge')).includes(String(proComparesPerDay)), 'the nudge names the published number');
  assert.strictEqual(await lior.getAttribute('#cmp-go-pro', 'href'), '#/pro?from=compare');
  await shot(lior, '28b-compare-nudge-en');
  await Promise.all([lior.waitForResponse((r) => r.url().endsWith('/api/funnel/pro-opened') && r.status() === 204), lior.click('#cmp-go-pro')]);
  await lior.waitForSelector('#pro-manual');
  assert.strictEqual(await hash(lior), '#/pro', 'the query is stripped once it has been read');
  assert.strictEqual((await metricsAs(noa)).funnel.days.at(-1).proFromCompare, 1);
  // The wardrobe's Pro line says where it came from too.
  await go(lior, '#/wardrobe');
  await lior.waitForSelector('#wardrobe-pro-link');
  assert.strictEqual(await lior.getAttribute('#wardrobe-pro-link', 'href'), '#/pro?from=wardrobe');
  await Promise.all([lior.waitForResponse((r) => r.url().endsWith('/api/funnel/pro-opened') && r.status() === 204), lior.click('#wardrobe-pro-link')]);
  await lior.waitForSelector('#pro-manual');
  assert.strictEqual((await metricsAs(noa)).funnel.days.at(-1).proFromWardrobe, 1);
  // Back from a paid Checkout that started on the compare screen (the Stripe round trip itself is the billing leg's and
  // BillingTests'): #/compare?ready=1 says Pro is on and opens slot A's sheet on the camera row, without a permission
  // prompt until a tap; the query is gone and Escape leaves the plain screen.
  await noa.evaluate(() => { location.hash = '#/compare?ready=1'; });
  await noa.waitForSelector('#cmp-media-camera');
  assert.strictEqual(await text(noa, '#toast'), 'Pro is on. Two photos, one answer.');
  assert.strictEqual(await hash(noa), '#/compare');
  await noa.keyboard.press('Escape');
  await noa.waitForFunction(() => !document.querySelector('.sheet'));
  assert.strictEqual(await hash(noa), '#/compare');
  await go(noa, '#/u/noa');
  await noa.waitForSelector('#insights-link');
  // Round 16: the numbers page asks for the month written back (Pro's) on every visit and swallows the refusal, so a
  // free account gets a 403 here by design - noa is still free at this point. It is listed rather than hidden,
  // because a 403 the watchdog cannot see is a 403 nobody notices when it starts arriving for the wrong reason.
  expected.push('/api/users/me/recap -> 403');
  await noa.click('#insights-link');
  await noa.waitForSelector('#insights-body');
  assert.ok(await noa.$('#insights-recap[hidden]'), 'the month paragraph stays out for a free account');
  await shot(noa, '29-insights-en');
  await go(dan, '#/search/running');
  await dan.waitForSelector('#search-looks .grid a');
  assert.ok((await count(dan, '#search-looks .grid a')) >= 1, 'looks are found by the pieces in them');
  // Pro: switched on by hand on this server (no Stripe keys), and the settings row says so.
  await go(noa, '#/pro');
  await noa.waitForSelector('#pro-manual');
  assert.ok(/noa/.test(maintenance('--pro', 'noa', '1')), '--pro reports the handle');
  await noa.reload();
  await noa.waitForSelector(settled);
  assert.strictEqual((await me(noa)).plan, 'pro');
  await go(noa, '#/settings');
  await noa.waitForSelector('#s-plan');
  assert.ok(await noa.$('#s-plan .pro-badge, #s-plan .badge, #s-plan b'), 'the plan row');
  await go(noa, '#/pro');
  await noa.waitForSelector('#pro-current');
  await shot(noa, '30-pro-en');
  // Round 16: what a subscriber reads on the screen they open every day. Pro is sold on the month, so the month is
  // the number quoted - the 30-a-day burst brake is never advertised, and quoting it here was the bug a real Pro
  // account found ("27 of 30 checks left today", the old pitch, on the one screen a subscriber sees daily). The
  // remaining count moves with the checks made earlier in this run, so only the allowance is pinned.
  await go(noa, '#/check');
  await noa.waitForSelector('#checks-left');
  assert.match(await text(noa, '#checks-left'), /^\d+ of 150 checks left this month$/, 'Pro quotes the month, not the day');
  await shot(noa, '31-check-pro-month-en');

  // Round 19 — Tomorrow. The wardrobe builds itself from a check: three keeps, one tap each, and the keep row's payoff
  // once two kinds are in ("Plan tomorrow from it"). Then the screen: the strip of her own pieces as photos before
  // anything is spent, the compose, the look card (one photo, since all three came from one check), the thumbs, another
  // idea, and "Wearing it? Check it" carrying the outfit's chips to the check screen. The forecast comes from the stub.
  step = 'tomorrow';
  await runCheck(noa, { intent: 'Office', buffer: await makeJpeg(noa, 900, 1200), score: 7 });
  for (let i = 0; i < 3; i++) {
    await noa.waitForSelector('#wardrobe-keep-yes', { timeout: 10000 });
    await noa.click('#wardrobe-keep-yes');
    await noa.waitForSelector('#wardrobe-kept-link');
  }
  await noa.waitForSelector('#wardrobe-kept-tomorrow', { timeout: 10000 });
  await noa.context().grantPermissions(['geolocation'], { origin: base });
  await noa.context().setGeolocation({ latitude: 32.0853, longitude: 34.7818 });
  await go(noa, '#/wardrobe');
  await noa.waitForSelector('#wardrobe-tomorrow');
  await noa.click('#wardrobe-tomorrow');
  await noa.waitForSelector('#tm-compose');
  assert.strictEqual(await count(noa, '#strip .tm-tile'), 3, 'her three pieces, as photos, before anything is spent');
  await noa.waitForSelector('#strip .tm-tile img', { timeout: 10000 });   // the photo, through the new owner-only route
  assert.ok(await noa.$('#occasions .chip[aria-pressed="true"]'), 'an occasion is pre-lit');
  await noa.click('#weather-use');
  await noa.waitForSelector('#weather-ready');
  // The day defaults to today before 15:00 and tomorrow after; the test pins tomorrow so the forecast it reads is fixed.
  await noa.click('#when .chip[data-when="tomorrow"]');
  await noa.waitForSelector('#when .chip[data-when="tomorrow"][aria-pressed="true"]');
  await shot(noa, '32-tomorrow-before-en');
  await noa.click('#tm-compose');
  await noa.waitForSelector('#tm-card', { timeout: 30000 });
  assert.ok(await noa.$('#tm-one-look'), 'three pieces from one check collapse to one photo of her, not three');
  assert.strictEqual(await count(noa, '#tm-one-look li'), 3, 'the three names under it');
  assert.ok((await text(noa, '#tm-sentence')).length > 10, 'the stylist\'s sentence');
  assert.ok(await noa.$('#weather-pill'), 'the forecast it was written for');
  assert.match(await text(noa, '#weather-pill'), /24° \/ 17°/, 'tomorrow\'s high and low from the stub');
  await shot(noa, '33-tomorrow-look-en');
  await noa.click('#tm-yes');
  await noa.waitForSelector('#tm-thumbs .hint');
  assert.strictEqual(await noa.getAttribute('#tm-yes', 'aria-pressed'), 'true', 'the yes stays lit');
  await noa.click('#tm-another');
  await noa.waitForSelector('#tm-idea', { timeout: 30000 });
  assert.strictEqual(await text(noa, '#tm-idea'), 'Idea 2', 'another idea is idea 2');
  await noa.waitForSelector('#tm-recent button');
  const checkIt = await noa.getAttribute('#tm-check-it', 'href');
  assert.ok(checkIt.includes('#/check?suggestion='), 'the check link carries the outfit');
  const suggestionId = checkIt.split('suggestion=')[1];
  // The check screen's own chips are moved off Office first, so the pre-light below is the outfit's doing and not what
  // the earlier check left pressed. Then the check is made from the link, and the server is asked whether that closed the
  // loop: the outfit carries the check it was worn in, and counts as a yes.
  await go(noa, '#/check');
  await noa.waitForSelector('#occasions .chip[data-occasion="Party"]');
  await noa.click('#occasions .chip[data-occasion="Party"]');
  await noa.waitForSelector('#occasions .chip[data-occasion="Party"][aria-pressed="true"]');
  await go(noa, '#/tomorrow');
  await noa.waitForSelector('#tm-check-it');
  await noa.click('#tm-check-it');
  await noa.waitForSelector('#photo');
  assert.strictEqual(await noa.getAttribute('#occasions .chip[aria-pressed="true"]', 'data-occasion'), 'Office', 'the outfit\'s occasion is pre-lit on the check screen, over the one left pressed');
  await choosePhoto(noa, '#photo', await makeJpeg(noa, 900, 1200));
  await noa.waitForSelector('#photo img');
  await noa.waitForFunction(() => !document.getElementById('submit').disabled);
  // Round 20 - the wait, told in stages. The stub holds this one answer for 4.5 s, so the line under the flame is seen
  // to open with "Looking at the look" and, three seconds in, become "Reading the pieces".
  await get(`http://127.0.0.1:${STUB_PORT}/__delay/4500`);
  await noa.click('#submit');
  await noa.waitForFunction(() => (document.getElementById('loading-line') || {}).textContent === 'Looking at the look', null, { timeout: 2500 });
  await noa.waitForFunction(() => (document.getElementById('loading-line') || {}).textContent === 'Reading the pieces', null, { timeout: 6000 });
  await shot(noa, '33c-wait-staged-en');
  await noa.waitForSelector('#result .score', { timeout: 30000 });
  const worn = await noa.evaluate(async (id) => {
    const { api } = await import('/app/core.js');
    const plan = await api('GET', '/api/tomorrow');
    const row = (plan.recent || []).find((s) => s.id === id);
    return row ? { wornCheckId: row.wornCheckId || null, usefulReason: row.usefulReason || null } : null;
  }, suggestionId);
  assert.ok(worn && worn.wornCheckId, 'wearing it closed the loop: the outfit carries the check it was worn in');
  assert.strictEqual(worn.usefulReason, 'worked', 'and counts as a yes');
  await go(noa, '#/u/noa');
  await noa.waitForSelector('#profile-tomorrow');

  // Dan has kept nothing: the honest card, in Hebrew, and no button that would spend.
  await go(dan, '#/tomorrow');
  await dan.waitForSelector('#tm-needs-go');
  assert.ok(!(await dan.$('#tm-compose')), 'no compose button without two kinds of piece');
  await shot(dan, '33b-tomorrow-empty-he');

  // Round 20 — the wedge: filling the closet faster, on a free Hebrew account. The keep row offers "Keep all 3" beside
  // Keep / Not this one; three "not this one"s keep nothing, and the wardrobe screen then lists the same pieces under
  // "Keep from an older look" (the wardrobe records no refusals), where one tap keeps one. A second check offers the two
  // left as "Keep all 2"; keeping them crosses the free slice (Plans__WardrobeNamesToStylist is 2), so the Pro moment
  // appears under the kept line with its Go Pro. It is once per tab: the wardrobe screen in the same tab does not say it
  // again, a fresh tab does, in place of the plain Pro notice, and its button opens the Pro page.
  step = 'wedge';
  await runCheck(dan, { intent: 'Office', buffer: await makeJpeg(dan, 900, 1200), score: 6 });
  await dan.waitForSelector('#wardrobe-keep-yes', { timeout: 10000 });
  await dan.waitForSelector('#wardrobe-keep-all');
  assert.strictEqual(await text(dan, '#wardrobe-keep-all'), 'שמור את כל 3', 'the third answer names the count');
  for (let i = 0; i < 3; i++) {
    await dan.waitForSelector('#wardrobe-keep-skip');
    await dan.click('#wardrobe-keep-skip');
  }
  await dan.waitForSelector('#wardrobe-keep', { state: 'hidden' });
  await go(dan, '#/wardrobe');
  await dan.waitForSelector('#wardrobe-unkept');
  assert.strictEqual(await count(dan, '#wardrobe-unkept .wardrobe-unkept-keep'), 3, 'the three pieces passed over, each with a Keep');
  assert.ok(!(await dan.$('#wardrobe-moment')), 'no moment on an empty wardrobe');
  await shot(dan, '33d-wardrobe-unkept-he');
  await dan.click('#wardrobe-unkept .wardrobe-unkept-keep');
  await dan.waitForSelector('#wardrobe-count');
  assert.strictEqual(await text(dan, '#wardrobe-count'), '1 מתוך 200 פריטים');
  assert.strictEqual(await count(dan, '#wardrobe-unkept .wardrobe-unkept-keep'), 2, 'the kept piece left the list');
  assert.ok(!(await dan.$('#wardrobe-moment')), 'one piece is under the free slice');
  await runCheck(dan, { intent: 'Office', buffer: await makeJpeg(dan, 900, 1200), score: 6 });
  await dan.waitForSelector('#wardrobe-keep-all', { timeout: 10000 });
  assert.strictEqual(await text(dan, '#wardrobe-keep-all'), 'שמור את כל 2', 'only the two not yet kept are offered');
  await dan.click('#wardrobe-keep-all');
  await dan.waitForSelector('#wardrobe-kept-link');
  await dan.waitForSelector('#wardrobe-keep-moment', { timeout: 10000 });
  assert.ok(await dan.$('#wardrobe-keep-moment-go'), 'the moment carries its Go Pro');
  assert.ok(!(await dan.$('#wardrobe-keep-all-full')), 'nothing was refused by the cap');
  await shot(dan, '33e-keep-all-moment-he');
  await go(dan, '#/wardrobe');
  await dan.waitForSelector('#wardrobe-count');
  assert.strictEqual(await text(dan, '#wardrobe-count'), '3 מתוך 200 פריטים');
  assert.ok(!(await dan.$('#wardrobe-unkept')), 'nothing left to keep from an older look');
  assert.strictEqual(await dan.$('#wardrobe-moment'), null, 'once per session: the same tab does not say it again');
  assert.ok(await dan.$('#wardrobe-pro'), 'the plain Pro notice is back when the moment is not drawn');
  const dan2 = await dan.context().newPage();
  await go(dan2, '#/wardrobe');
  await dan2.waitForSelector('#wardrobe-moment');
  assert.ok(!(await dan2.$('#wardrobe-pro')), 'the moment replaces the plain notice for that paint');
  assert.match(await text(dan2, '#wardrobe-moment p'), /3/, 'the sentence carries the count');
  await Promise.all([
    dan2.waitForResponse((r) => r.url().endsWith('/api/wardrobe/moment') && r.status() === 204 && r.request().postData().includes('go')),
    dan2.click('#wardrobe-moment-go')
  ]);
  // Billing is not on until the Stripe leg, so the Pro page ends in the manual notice, as it does for lior above.
  await dan2.waitForSelector('#pro-manual');
  assert.strictEqual(await hash(dan2), '#/pro', 'the source query is read and stripped');
  await dan2.close();

  // Round 20 — the morning loop behind a switch. The push's tap lands on #/tomorrow?from=push: the view reads the query
  // once, the server stamps a push's receipt (there is none here, so nothing is counted), and the address loses the
  // query; the stub sees no compose from any of it, twice over. Then the switch in Settings, drawn because the server
  // offers the push, disabled because this browser has no subscription (and push has no keys), with its line saying so.
  step = 'morning';
  const composesBeforeMarker = (await getJson(`http://127.0.0.1:${STUB_PORT}/`)).filter((r) => r.tool === 'compose_outfit').length;
  for (let visit = 0; visit < 2; visit++) {
    await noa.evaluate(() => { location.hash = '#/tomorrow?from=push'; });
    await noa.waitForFunction(() => location.hash === '#/tomorrow');
    await noa.waitForSelector('#strip .tm-tile');
  }
  const composesAfterMarker = (await getJson(`http://127.0.0.1:${STUB_PORT}/`)).filter((r) => r.tool === 'compose_outfit').length;
  assert.strictEqual(composesAfterMarker, composesBeforeMarker, 'opening from the push composes nothing');
  await go(noa, '#/settings');
  await noa.waitForSelector('#s-push-morning');
  await noa.waitForFunction(() => !document.getElementById('s-push-morning-status').hidden);
  assert.strictEqual(await noa.isDisabled('#s-push-morning'), true, 'no subscription in this browser: the switch is locked');
  assert.strictEqual(await noa.isChecked('#s-push-morning'), false);
  assert.strictEqual(await text(noa, '#s-push-morning-status'), 'Turn on notifications above first.');
  assert.strictEqual(await text(noa, 'label[for="s-push-morning"] b'), 'Your outfit each morning');
  assert.match(await text(noa, 'label[for="s-push-morning"] .hint'), /One ping at 07:30\./, 'the hint names the server\'s hour');
  const morningState = await (await noa.request.get(base + '/api/push/morning')).json();
  assert.deepStrictEqual(morningState, { on: true, offered: false, hour: '07:30' }, 'the account\'s switch is on by default; the server cannot send');
  await shot(noa, '33f-settings-morning-en');
  await go(dan, '#/settings');
  await dan.waitForSelector('#s-push-morning');
  assert.strictEqual(await text(dan, 'label[for="s-push-morning"] b'), 'הלוק שלך כל בוקר');
  step = 'today';

  // Today's look: the daily prompt strip on For you, its page, and "Post yours" pre-filling the tag.
  await go(noa, '#/feed');
  await noa.reload();
  await noa.waitForSelector('#today-strip');
  const todayTag = await noa.$eval('#today-strip', (n) => n.dataset.tag || (n.querySelector('#today-title') && n.querySelector('#today-title').textContent) || '');
  assert.ok(todayTag, 'the strip names today\'s prompt');
  await noa.click('#today-post');
  await noa.waitForSelector('#challenge-banner');
  assert.ok((await text(noa, '#challenge-banner')).startsWith('Entering: '), 'the daily prompt is entered like a challenge');
  await go(noa, '#/today');
  await noa.waitForSelector('.today-page');
  await noa.waitForSelector('.today-count, .empty');
  await shot(noa, '34-today-en');
  await noa.reload();                                   // drops the entered prompt so later captions start clean
  await noa.waitForSelector(settled);

  // Verified brands: the owner runs --verify; the check sits inside the brand mark on the profile and on the cards.
  assert.ok(/nexor/.test(maintenance('--verify', 'nexor')), '--verify reports the handle');
  await go(brand, '#/u/nexor');
  await brand.reload();
  await brand.waitForSelector('.profile-head .brand-mark.verified');
  await go(dan, '#/search/nexor');
  await dan.reload();
  await dan.waitForSelector('.person .brand-mark.verified');
  assert.ok(/nexor/.test(maintenance('--unverify', 'nexor')), '--unverify reports the handle');
  await brand.reload();
  await brand.waitForSelector('.profile-head .brand-mark');
  assert.strictEqual(await count(brand, '.profile-head .brand-mark.verified'), 0, 'the check goes with the flag');
  // The numbers page: a moderator's dashboard of the pilot metrics; a person gets the refusal.
  await go(noa, '#/admin/metrics');
  await noa.waitForSelector('#dash-return');
  assert.ok((await count(noa, '#dash-scores li')) >= 1, 'the score distribution has bars');
  // Round 20: the p95 beside the average (four tiles), the two cache tiles among the money tiles (six), and the prices
  // hint naming the cache mode this run set.
  assert.strictEqual(await count(noa, '#dash-tiles .dash-tile'), 4);
  assert.strictEqual(await count(noa, '#dash-spend-tiles .dash-tile'), 6);
  assert.match(await text(noa, '#dash-prices'), /Prompt cache: 5 minutes/);
  // Round 12 added "Share videos made"; Round 14 added five: private grades, comments begun from an opener, before/after
  // shares with and without the numbers, and challenges that state a rule.
  assert.strictEqual(await count(noa, '#dash-social .dash-tile'), 20);
  // Round 20 — the wedge: three more wardrobe tiles (keep-all taps, the Pro moment with its rate, the median), and the
  // numbers behind them are the wedge step's own: one keep-all that wrote, the moment shown twice and taken once.
  assert.strictEqual(await count(noa, '#dash-wardrobe .dash-tile'), 8);
  const wedge = (await metricsAs(noa)).wardrobe;
  assert.ok(wedge.keepAll >= 1, 'keep-all taps: ' + wedge.keepAll);
  assert.ok(wedge.momentShown >= 2, 'moment shown: ' + wedge.momentShown);
  assert.ok(wedge.momentGo >= 1, 'moment taken: ' + wedge.momentGo);
  assert.strictEqual(typeof wedge.piecesPerActiveMedian, 'number', 'a median while people are active');
  // Round 20 — the morning push's two tiles in the Tomorrow block (seven now): nothing was sent in this run, so the
  // pushes read 0 and the open rate is the en dash, and the two opens-from-push above counted nothing.
  await noa.waitForSelector('#dash-tomorrow');
  assert.strictEqual(await count(noa, '#dash-tomorrow .dash-tile'), 7);
  const tomorrowTiles = await noa.$$eval('#dash-tomorrow .dash-tile', (tiles) => tiles.map((tile) => tile.textContent.replace(/\s+/g, ' ').trim()));
  assert.ok(tomorrowTiles.includes('Morning pushes0'), 'morning pushes 0: ' + tomorrowTiles.join(' | '));
  assert.ok(tomorrowTiles.includes('Opened\u2013'), 'opened is the en dash: ' + tomorrowTiles.join(' | '));
  const morningMetrics = (await metricsAs(noa)).tomorrow;
  assert.strictEqual(morningMetrics.pushesSent, 0);
  assert.strictEqual(morningMetrics.pushesOpened, 0);
  assert.strictEqual(morningMetrics.openRate, undefined, 'no rate without a push');
  await shot(noa, '31-numbers-en');
  expected.push('GET /api/metrics/pilot -> 403');
  await go(dan, '#/admin/metrics');
  await dan.waitForSelector('#dash-forbidden');
  // Terms and privacy: ten sections each, a version line, the cross link; Hebrew is native copy.
  await go(dan, '#/terms');
  await dan.waitForSelector('#lg-terms');
  assert.strictEqual(await count(dan, '#lg-terms > li'), 10);
  assert.ok((await text(dan, '#lg-version')).includes('2'), 'the version line');
  await shot(dan, '32-terms-he');
  await dan.click('#lg-other');
  await dan.waitForSelector('#lg-privacy');
  assert.strictEqual(await count(dan, '#lg-privacy > li'), 10);
  await go(noa, '#/privacy');
  await noa.waitForSelector('#lg-privacy');
  assert.strictEqual(await count(noa, '#lg-privacy > li'), 10);
  // The launch files: the landing pages are static documents and the link-preview card is served.
  for (const [path, needle] of [['/landing/', 'Check the look.'], ['/landing/index.he.html', 'בודקים את הלוק.']]) {
    const r = await get(base + path);
    assert.strictEqual(r.status, 200, path);
    assert.ok((r.headers['content-type'] || '').includes('text/html'), path + ' is html');
    assert.ok(r.body.toString('utf8').includes(needle), path + ' carries the slogan');
  }
  const og = await get(base + '/brand/og-1200x630.png');
  assert.strictEqual(og.status, 200);
  assert.strictEqual(og.headers['content-type'], 'image/png');
  const shellHtml = (await get(base + '/')).body.toString('utf8');
  assert.ok(shellHtml.includes('og:image') && shellHtml.includes('twitter:card'), 'the shell carries the link-preview tags');

  // The weekly board: Dan's fire counted (he has a check; new accounts count in this run), so the looks board has rows.
  await go(dan, '#/board');
  await dan.waitForSelector('#board-tabs');
  await dan.waitForSelector('.board-row[data-rank], #board-panel .empty');
  assert.ok((await count(dan, '.board-row[data-rank]')) >= 1, 'a look is on this week\'s board');
  assert.strictEqual(await count(dan, '.board-row[data-rank="1"] .rank-medal.top'), 1, 'first place wears the medal');
  // Round 20: the sponsor from the server's settings is on the page, its name a link to the brand's profile.
  await dan.waitForSelector('#board-sponsor a[href="#/u/nexor"]');
  assert.ok((await text(dan, '#board-sponsor')).includes('NEXOR'), 'the sponsor of the week is named');
  await shot(dan, '38-board-he');
  await dan.click('#board-tabs .segment[data-tab=people]');
  await dan.waitForSelector('.board-person, #board-panel .empty');
  await dan.click('#board-tabs .segment[data-tab=picks]');
  await dan.waitForSelector('.board-row[data-rank], #board-panel .empty');
  await dan.click('#board-hall-link');
  await dan.waitForFunction(() => location.hash === '#/board/hall');
  await dan.waitForSelector('#hall, #view .empty');                 // no week has closed yet in this run: the empty hall
  await go(noa, '#/explore');
  await noa.waitForSelector('.brand-card');   // /api/explore has answered: a reload now aborts nothing in flight
  await noa.reload();
  await noa.waitForSelector('#board-strip .board-strip-row a');
  await shot(noa, '39-explore-strip-en');

  // Round 11. Dan blocks Noa from her profile: her looks leave his feed, Settings lists her, and the unblock undoes it.
  await go(dan, '#/u/noa');
  await dan.waitForSelector('#profile-menu');
  await dan.click('#profile-menu');
  await dan.waitForSelector('.sheet #block');
  await dan.click('.sheet #block');
  await dan.waitForSelector('.sheet .btn-danger');
  await dan.click('.sheet .btn-danger');
  await dan.waitForSelector('#toast');
  await shot(dan, '40-blocked-he');
  const blockedFeed = await (await dan.request.get(base + '/api/feed?tab=foryou')).json();
  assert.ok(!blockedFeed.items.some((x) => x.user.handle === 'noa'), 'a blocked person leaves the feed');
  await go(dan, '#/settings/blocked');
  await dan.waitForSelector('#blocked-list li[data-handle="noa"] button.unblock');
  await dan.click('#blocked-list li[data-handle="noa"] button.unblock');
  await dan.waitForSelector('#blocked-list li[data-handle="noa"]', { state: 'detached' });
  const backFeed = await (await dan.request.get(base + '/api/feed?tab=foryou')).json();
  assert.ok(backFeed.items.some((x) => x.user.handle === 'noa'), 'unblocking brings her looks back');

  // Round 11. Noa downloads her data, and the manual billing provider offers no Stripe portal.
  await go(noa, '#/settings');
  await noa.waitForSelector('#export-data');
  await noa.click('#export-data');
  await noa.waitForSelector('#export-status:not([hidden]) #export-ready');
  const exported = await noa.request.get(base + '/api/users/me/export');
  assert.strictEqual(exported.status(), 200, 'the export answers');
  assert.ok((exported.headers()['content-disposition'] || '').includes('orevosh-noa-'), 'the export is a named file');
  const dump = await exported.json();
  assert.strictEqual(dump.account.handle, 'noa');
  assert.ok(dump.posts.length >= 1 && dump.checks.length >= 1, 'the export carries her looks and her checks');
  assert.strictEqual(dump.account.birthDate, undefined, 'the birth date stays out of the export');
  assert.strictEqual(await count(noa, '#billing-manage'), 0, 'no portal button on the manual provider');
  expected.push('POST /api/billing/portal -> 404');
  const portal = await noa.request.post(base + '/api/billing/portal', { headers: { 'X-Requested-With': 'Orevosh' } });
  assert.strictEqual(portal.status(), 404, 'the manual provider has no portal');

  step = 'before-after';
  // Round 20 — the wedge: the before/after episode is fed by the app's own JSON and never by retyped numbers. Noa's
  // Office check (score 7, from the tomorrow step, in no pair yet) is the before; a fifth check whose note says
  // "after the tip" answers 8 with the shoes named differently; the pair is written over the API; then the three
  // shapes the renderer reads — GET /api/checks/<id>, the pair from GET /api/users/me/tried, and the export the
  // founder downloads — go through tools/brand/lib/before-after.js exactly as an episode JSON would, on the real
  // API, and the episode's numbers must be the app's. The photo route is the one the founder saves the look from.
  await runCheck(noa, { intent: 'Office', occasion: 'after the tip', buffer: bigJpeg, score: 8 });
  const baChecks = await noa.request.get(base + '/api/users/me/checks').then((r) => r.json());
  const baAfterId = baChecks[0].id;
  assert.strictEqual(baChecks[0].score, 8, 'the newest check is the one after the tip');
  assert.strictEqual(baChecks[0].note, 'after the tip');
  const baBeforeId = baChecks[1].id;
  assert.strictEqual(baChecks[1].score, 7, 'the one before it is the Office check');
  assert.strictEqual(baChecks[1].intent, 'Office');
  const baPairResponse = await noa.request.post(base + '/api/checks/' + baAfterId + '/tried', { headers: { 'X-Requested-With': 'Orevosh' }, data: { beforeId: baBeforeId } });
  assert.strictEqual(baPairResponse.status(), 201, 'the pair is written: ' + await baPairResponse.text());
  const baPair = await baPairResponse.json();
  assert.strictEqual(baPair.before.score, 7);
  assert.strictEqual(baPair.after.score, 8);
  assert.strictEqual(baPair.changed.length, 1, 'exactly one change: ' + JSON.stringify(baPair.changed));
  assert.strictEqual(baPair.changed[0].category, 'shoes');
  assert.strictEqual(baPair.preferred, undefined, 'nobody has said which they prefer');
  const baBefore = await noa.request.get(base + '/api/checks/' + baBeforeId).then((r) => r.json());
  const baAfter = await noa.request.get(base + '/api/checks/' + baAfterId).then((r) => r.json());
  const baList = await noa.request.get(base + '/api/users/me/tried').then((r) => r.json());
  assert.strictEqual(baList.items[0].id, baPair.id, 'the newest pair is this one');
  const baExport = await noa.request.get(base + '/api/users/me/export').then((r) => r.json());
  const beforeAfter = require('../brand/lib/before-after.js');
  const episode = beforeAfter.fromApp({ before: baBefore, after: baAfter, pair: baList.items[0], lang: 'en', where: 'e2e' });
  assert.strictEqual(episode.before.score, 7);
  assert.strictEqual(episode.after.score, 8);
  assert.strictEqual(episode.delta, 1);
  assert.strictEqual(episode.tip, baBefore.feedback.oneTip, 'the tip is the before check\'s one tip');
  assert.strictEqual(episode.tip, 'Swap the running shoes for plain white leather sneakers.');
  assert.deepStrictEqual(episode.before.breakdown, { fit: 7, color: 8, accessories: 4 });
  assert.deepStrictEqual(episode.after.breakdown, { fit: 7, color: 8, accessories: 6 });
  assert.strictEqual(episode.changes.length, 1);
  assert.strictEqual(episode.changes[0].category, 'shoes');
  assert.strictEqual(episode.before.intent, 'OFFICE', 'the pill says the app\'s own word');
  const fromExport = beforeAfter.fromExport(baExport, baBeforeId, baAfterId, 'en', 'export');
  assert.deepStrictEqual([fromExport.before.score, fromExport.after.score, fromExport.delta, fromExport.tip], [7, 8, 1, episode.tip], 'the export gives the same numbers');
  assert.deepStrictEqual(fromExport.changes, episode.changes);
  assert.deepStrictEqual(fromExport.after.breakdown, episode.after.breakdown);
  const baImage = await noa.request.get(base + '/api/checks/' + baAfterId + '/image');
  assert.strictEqual(baImage.status(), 200, 'the photo the founder saves for the episode');
  assert.ok((baImage.headers()['content-type'] || '').startsWith('image/jpeg'), 'as a JPEG: ' + baImage.headers()['content-type']);

  step = 'distribution';
  // Round 20 - distribution that can be counted. (1) An entry link: a fourth person, Maya, on a Hebrew phone, follows
  // /go/tt and lands on the check screen with the source kept beside the invite would be; her guest check and her
  // signup carry it, the numbers page attributes both to TikTok, and the source is spent by the signup. An unknown
  // link lands on the landing page and moves no row. (2) Inside Instagram's browser the app says "open this in Safari
  // or Chrome" once and never suggests Add to Home Screen. (3) The installed app says "standalone" on its first call of
  // the day and only then; the numbers page counts one launch. (4) Copy link carries the share sentence above the
  // address on a look page (Hebrew reads as a challenge) and on the result screen once the look is posted.
  const maya = await person(browser, 'maya', 'he-IL');
  await maya.goto(base + '/go/tt');
  await maya.waitForFunction(() => location.hash === '#/check');
  await maya.waitForSelector('#occasions .chip');
  assert.ok(maya.url().endsWith('/?src=tt#/check'), 'the entry link lands on the check screen with the source: ' + maya.url());
  assert.ok((await count(maya, '#occasions .chip')) > 0, 'the occasion chips are up');
  assert.strictEqual(await maya.evaluate(() => localStorage.getItem('orevosh.source')), 'tt', 'the source is kept on the device');
  const ttBefore = (await metricsAs(noa)).funnel.sources.find((s) => s.source === 'tt');
  assert.strictEqual(ttBefore.arrivals, 1, 'one arrival through the TikTok link');
  const mayaPhoto = await makeJpeg(maya, 800, 1000);
  await runCheck(maya, { intent: 'Party', buffer: mayaPhoto, score: 7 });
  assert.strictEqual(await maya.evaluate(() => localStorage.getItem('orevosh.source')), 'tt', 'a guest check does not spend the source');
  await signup(maya, 'maya', 'password123');
  assert.strictEqual(await maya.evaluate(() => localStorage.getItem('orevosh.source')), null, 'the signup spends the source');
  const tt = (await metricsAs(noa)).funnel.sources.find((s) => s.source === 'tt');
  assert.ok(tt.arrivals >= 1 && tt.guestChecks >= 1 && tt.signups >= 1, 'the walk is attributed to TikTok: ' + JSON.stringify(tt));
  const sourceRows = (await metricsAs(noa)).funnel.sources;
  assert.ok(sourceRows.every((s) => s.source === 'tt' || (s.arrivals === 0 && s.guestChecks === 0 && s.signups === 0)), 'no other row moved');
  // The result screen once the look is posted: the Share button falls back to the clipboard (headless Chromium has no
  // share sheet) with the sentence and the look's own address, which is Maya's invite link.
  await maya.context().grantPermissions(['clipboard-read', 'clipboard-write'], { origin: base });
  await runCheck(maya, { intent: 'Party', buffer: mayaPhoto, score: 7 });
  const mayaPost = await postIt(maya, {});
  await maya.click('#result-share');
  await maya.waitForFunction(() => navigator.clipboard.readText().then((c) => c.includes('/look/')));
  const resultClip = (await maya.evaluate(() => navigator.clipboard.readText())).split('\n');
  assert.strictEqual(resultClip.length, 2, 'sentence, then the address: ' + JSON.stringify(resultClip));
  assert.ok(resultClip[0].includes('7/10') && resultClip[0].includes('OREVOSH'), 'the result sentence: ' + resultClip[0]);
  assert.ok(resultClip[1].endsWith('/look/' + mayaPost + '?via=maya'), 'the look address carries her invite: ' + resultClip[1]);
  await shot(maya, '20-copy-link');
  // An unknown source: the landing page, uncounted.
  await maya.goto(base + '/go/nope');
  await maya.waitForSelector('main, h1');
  assert.ok(maya.url().endsWith('/landing/'), 'an unknown source lands on the landing page: ' + maya.url());
  assert.strictEqual((await metricsAs(noa)).funnel.sources.find((s) => s.source === 'tt').arrivals, tt.arrivals, 'an unknown link moves no row');
  await maya.context().close();

  // (2) Instagram's browser: the note once, no Add to Home Screen advice, gone after "Got it" and not back on a reload.
  const inapp = await person(browser, 'inapp', 'en-US', { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 Instagram 300.0.0.0' });
  await inapp.goto(base + '/');
  await inapp.waitForSelector('#inapp-hint');
  assert.strictEqual(await count(inapp, '#install-hint'), 0, 'no install hint in a webview');
  assert.strictEqual(await count(inapp, '.install:not(#inapp-hint)'), 0, 'no Add to Home Screen advice in a webview');
  await shot(inapp, '20-inapp-hint');
  await inapp.click('#inapp-hint-ok');
  assert.strictEqual(await count(inapp, '#inapp-hint'), 0, 'Got it takes it away');
  await inapp.reload();
  await inapp.waitForSelector(settled);
  assert.strictEqual(await count(inapp, '#inapp-hint'), 0, 'once per device');
  await inapp.context().close();

  // (3) The installed app: the launch header on the first /api/config of the day, absent on the reload, one row.
  const installed = await person(browser, 'installed', 'en-US', { standalone: true });
  const launches = [];
  installed.on('request', (r) => { if (r.url().endsWith('/api/config')) launches.push(r.headers()['x-orevosh-launch'] || null); });
  await installed.goto(base + '/');
  await installed.waitForSelector(settled);
  await installed.reload();
  await installed.waitForSelector(settled);
  assert.deepStrictEqual(launches, ['standalone', null], 'the header rides the first call of the day only: ' + JSON.stringify(launches));
  assert.strictEqual((await metricsAs(noa)).funnel.days.at(-1).standalone, 1, 'one home-screen launch today');
  await installed.context().close();

  // (4) Copy link on a look page: the sentence above the address. Noa in English on her own look, Dan in Hebrew on the
  // same look, where the line is a challenge to the group and the address carries his invite.
  await noa.context().grantPermissions(['clipboard-read', 'clipboard-write'], { origin: base });
  await go(noa, '#/post/' + post1);
  await noa.click('#post-link-share');
  await noa.waitForSelector('#link-copy');
  await noa.click('#link-copy');
  await noa.waitForFunction(() => navigator.clipboard.readText().then((c) => c.includes('/look/')));
  const noaClip = (await noa.evaluate(() => navigator.clipboard.readText())).split('\n');
  assert.strictEqual(noaClip.length, 2, 'sentence, then the address: ' + JSON.stringify(noaClip));
  assert.ok(/ look /.test(noaClip[0]) && noaClip[0].endsWith('on OREVOSH'), 'the English sentence: ' + noaClip[0]);
  assert.ok(noaClip[1].endsWith('/look/' + post1 + '?via=noa'), 'her look address carries her invite: ' + noaClip[1]);
  await noa.keyboard.press('Escape');
  await noa.waitForSelector('.sheet', { state: 'detached' });
  await dan.context().grantPermissions(['clipboard-read', 'clipboard-write'], { origin: base });
  await go(dan, '#/post/' + post1);
  await dan.click('#post-link-share');
  await dan.waitForSelector('#link-copy');
  await dan.click('#link-copy');
  await dan.waitForFunction(() => navigator.clipboard.readText().then((c) => c.includes('/look/')));
  const danClip = (await dan.evaluate(() => navigator.clipboard.readText())).split('\n');
  assert.strictEqual(danClip.length, 2, 'sentence, then the address: ' + JSON.stringify(danClip));
  assert.ok(danClip[0].endsWith('?'), 'the Hebrew line is a challenge: ' + danClip[0]);
  assert.ok(danClip[1].endsWith('/look/' + post1 + '?via=dan'), 'the address carries his invite: ' + danClip[1]);
  await dan.keyboard.press('Escape');
  await dan.waitForSelector('.sheet', { state: 'detached' });
  // Numbers page: the last funnel column and the sources table, TikTok's row first and non-zero.
  await go(noa, '#/admin/metrics');
  await noa.waitForSelector('#dash-sources');
  assert.strictEqual(await text(noa, '#dash-funnel-table thead th:last-child'), 'Home-screen opens');
  assert.strictEqual(await text(noa, '#dash-funnel-table tbody tr:last-child td[data-key=standalone]'), '1');
  assert.strictEqual(await text(noa, '#dash-sources tbody tr:first-child th'), 'TikTok');
  assert.strictEqual(await text(noa, '#dash-sources tr[data-source=tt] td[data-key=signups]'), '1');
  await shot(noa, '20-sources-en');

  step = '11';
  // 11. Dan reports the clip; the owner makes Noa a moderator with the --admin command (the way it is done on a server,
  //     after the account exists); the queue, hide, show again, suspend Dan, lift it.
  const report = await dan.request.post(base + '/api/posts/' + post3 + '/report', { headers: { 'X-Requested-With': 'Orevosh' }, data: { reason: 'not an outfit' } });
  assert.ok(report.ok(), 'report ' + report.status());
  expected.push('GET /api/admin/queue -> 403');
  const notAdmin = await dan.request.get(base + '/api/admin/queue');
  assert.strictEqual(notAdmin.status(), 403, 'the queue is for moderators');
  await go(noa, '#/settings');
  await noa.waitForSelector('#moderation');
  await noa.click('#moderation');
  await noa.waitForFunction(() => location.hash === '#/admin');
  await noa.waitForSelector('.adm-item[data-kind="post"]');
  assert.strictEqual(await count(noa, '.adm-item'), 1);
  assert.ok((await text(noa, '.adm-item .adm-reasons')).includes('not an outfit'));
  await shot(noa, '24-admin-en');
  await noa.click('.adm-item[data-kind="post"] button:has-text("Hide")');
  await noa.waitForSelector('.adm-item[data-kind="post"] button:has-text("Show again")');
  expected.push(`GET /api/posts/${post3} -> 404`);
  assert.strictEqual((await dan.request.get(base + '/api/posts/' + post3)).status(), 404, 'hidden for everyone else');
  assert.strictEqual((await noa.request.get(base + '/api/posts/' + post3 + '/video')).status(), 200, 'the moderator can still play it');
  await noa.click('.adm-item[data-kind="post"] button:has-text("Show again")');
  await noa.waitForFunction(() => !document.querySelector('.adm-item'));
  assert.strictEqual((await dan.request.get(base + '/api/posts/' + post3)).status(), 200, 'back for everyone');
  await noa.fill('#adm-q', 'dan');
  await noa.press('#adm-q', 'Enter');
  await noa.waitForSelector('#adm-users button:has-text("Suspend account")');
  await noa.click('#adm-users button:has-text("Suspend account")');
  await noa.waitForSelector('.sheet .btn-danger');
  await noa.click('.sheet .btn-danger');
  await noa.waitForSelector('#adm-users button:has-text("Lift suspension")');
  expected.push('GET /api/auth/me -> 403');
  assert.strictEqual((await dan.request.get(base + '/api/auth/me')).status(), 403, 'a suspended account is refused');
  expected.push('GET /api/users/dan -> 404');
  assert.strictEqual((await get(`${base}/api/users/dan`)).status, 404, 'and reads as missing');
  await noa.click('#adm-users button:has-text("Lift suspension")');
  await noa.waitForSelector('#adm-users button:has-text("Suspend account")');
  assert.strictEqual((await get(`${base}/api/users/dan`)).status, 200);
  // The refusal ended Dan's session; a reload shows him signed out, and he signs back in.
  await dan.reload();
  await dan.waitForSelector(settled);
  await go(dan, '#/login');
  await dan.waitForSelector('#a-handle');
  await dan.fill('#a-handle', 'dan');
  await dan.fill('#a-password', 'password123');
  await dan.click('#a-submit');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await dan.waitForSelector(settled);

  step = '11b';
  // 11b. Round 20 - owner tooling without a terminal. Noa is still on #/admin with 'dan' searched. From the account
  //      block she grants Dan three months of Pro (the sheet with the months), takes it back, verifies him and removes
  //      that, keeps him off the board and puts him back; each tap re-renders the row from the server's word and Dan's
  //      own /me says what it did. Then the read-only sponsor card: the settings the API started with, the bare host
  //      shown as https, and the one warning it can give - nexor's verification was taken away by --unverify in the
  //      Round 9 steps, so the card says the handle is not a verified brand yet; Noa verifies it one section up and the
  //      warning goes.
  const daysAhead = (iso) => (new Date(iso) - Date.now()) / 86400000;
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] button:has-text("Grant Pro")');
  await noa.click('#adm-users .adm-account[data-handle="dan"] button:has-text("Grant Pro")');
  await noa.waitForSelector('.sheet #adm-months');
  await noa.selectOption('.sheet #adm-months', '3');
  await noa.click('.sheet button:has-text("Grant Pro")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] .tag:has-text("Pro")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] button:has-text("Remove Pro")');
  assert.ok((await text(noa, '#adm-users .adm-account[data-handle="dan"] .sub')).includes('Pro until'), 'the row says until when');
  let danMe = await me(dan);
  assert.strictEqual(danMe.plan, 'pro', 'Dan is Pro by the moderator\'s hand');
  assert.ok(Math.abs(daysAhead(danMe.proUntil) - 93) < 1, 'three months of 31 days: ' + danMe.proUntil);
  await noa.click('#adm-users .adm-account[data-handle="dan"] button:has-text("Remove Pro")');
  await noa.waitForSelector('.sheet .btn-danger');
  await noa.click('.sheet .btn-danger');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] button:has-text("Grant Pro")');
  assert.strictEqual((await me(dan)).plan, 'free', 'and back on Free, so the free caps later in the run hold');
  await noa.click('#adm-users .adm-account[data-handle="dan"] button:has-text("Verify brand")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] .tag:has-text("Verified")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] button:has-text("Remove verification")');
  assert.strictEqual((await me(dan)).verified, true, 'verified from the screen, as --verify does from the box');
  await noa.click('#adm-users .adm-account[data-handle="dan"] button:has-text("Remove verification")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] button:has-text("Verify brand")');
  assert.strictEqual((await me(dan)).verified, false);
  await noa.click('#adm-users .adm-account[data-handle="dan"] button:has-text("Exclude from board")');
  await noa.waitForSelector('.sheet .btn-danger');
  await noa.click('.sheet .btn-danger');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] .tag:has-text("Off the board")');
  const boardWhileOff = await (await dan.request.get(base + '/api/board')).json();
  const onBoard = (b, handle) => ['looks', 'picks', 'people'].some((list) => (b[list] || []).some((row) => ((row.post && row.post.user) || row.user || {}).handle === handle));
  assert.strictEqual(onBoard(boardWhileOff, 'dan'), false, 'an excluded account is on no board');
  await noa.click('#adm-users .adm-account[data-handle="dan"] button:has-text("Put back on the board")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="dan"] button:has-text("Exclude from board")');
  assert.strictEqual(await count(noa, '#adm-users .adm-account[data-handle="dan"] .tag:has-text("Off the board")'), 0, 'the tag goes with the flag');
  await noa.waitForSelector('#adm-sponsor .adm-sponsor');
  const sponsorText = await text(noa, '#adm-sponsor');
  assert.ok(sponsorText.includes('NEXOR'), 'the sponsor card names the brand');
  assert.ok(sponsorText.includes('nexor.example'), 'and its site, from the bare host the settings hold');
  assert.strictEqual(await noa.getAttribute('#adm-sponsor a[target="_blank"]', 'href'), 'https://nexor.example', 'the bare host reads as https');
  assert.strictEqual(await count(noa, '#adm-sponsor a[href="#/u/nexor"]'), 1, 'the handle exists: it links to the profile');
  assert.strictEqual(await count(noa, '#adm-sponsor .alert'), 1, 'one warning: the handle is not a verified brand right now');
  assert.ok((await text(noa, '#adm-sponsor .alert')).includes('not a verified brand yet'), 'and it says which');
  await shot(noa, '24b-admin-accounts-en');
  // The fix is one section up: verify the brand, and the card (reloaded with every action) stops warning.
  await noa.fill('#adm-q', 'nexor');
  await noa.press('#adm-q', 'Enter');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="nexor"] button:has-text("Verify brand")');
  assert.strictEqual(await count(noa, '#adm-users .adm-account[data-handle="nexor"] .tag:has-text("Moderator")'), 1, 'nexor is a moderator too, and the row says so');
  await noa.click('#adm-users .adm-account[data-handle="nexor"] button:has-text("Verify brand")');
  await noa.waitForSelector('#adm-users .adm-account[data-handle="nexor"] .tag:has-text("Verified")');
  await noa.waitForFunction(() => document.querySelector('#adm-sponsor .adm-sponsor') && !document.querySelector('#adm-sponsor .alert'));
  assert.strictEqual((await me(brand)).verified, true, 'the brand reads verified, as after --verify');
  await shot(noa, '24c-admin-sponsor-en');

  step = '12';
  // 12. The guidelines page, the push switch on a server without keys, and the production surface.
  await go(dan, '#/guidelines');
  assert.strictEqual(await text(dan, '#view h1'), 'כללי הקהילה');
  assert.strictEqual(await count(dan, '.g-rules li'), 5);
  await shot(dan, '25-guidelines-he');
  await go(noa, '#/settings');
  await noa.waitForSelector('#s-push');
  await noa.waitForFunction(() => !document.getElementById('s-push-status').hidden);
  assert.strictEqual(await text(noa, '#s-push-status'), 'Not set up on this server yet.');
  assert.strictEqual(await noa.isDisabled('#s-push'), true);
  assert.deepStrictEqual(await noa.request.get(base + '/api/push/state').then((r) => r.json()), { enabled: false, subscribed: false });
  expected.push('POST /api/push/subscriptions -> 400');
  assert.strictEqual((await noa.request.post(base + '/api/push/subscriptions', { headers: { 'X-Requested-With': 'Orevosh' }, data: { endpoint: 'https://push.example/x', p256dh: 'a', auth: 'b' } })).status(), 400);
  const health = await get(`${base}/healthz`);
  assert.strictEqual(health.status, 200);
  assert.strictEqual(health.body.toString(), 'ok');
  // Round 11: /healthz is liveness and stays a bare word; /readyz is what a deploy waits on and names each check.
  const ready = await get(`${base}/readyz`);
  assert.strictEqual(ready.status, 200, 'readyz');
  assert.strictEqual(ready.headers['cache-control'], 'no-store', 'readiness is never cached');
  const readyBody = JSON.parse(ready.body.toString('utf8'));
  assert.strictEqual(readyBody.ok, true, 'every readiness check passes');
  assert.strictEqual(readyBody.checks.db, 'ok');
  assert.strictEqual(readyBody.checks.storage, 'ok');
  const config = await getJson(`${base}/api/config`);
  assert.strictEqual(config.maxVideoSeconds, 30);
  assert.strictEqual(config.pushPublicKey, undefined, 'no push key without VAPID keys');
  const home = await get(`${base}/`);
  assert.strictEqual(home.headers['x-content-type-options'], 'nosniff');
  assert.strictEqual(home.headers['x-frame-options'], 'DENY');
  assert.strictEqual(home.headers['strict-transport-security'], undefined, 'HSTS only over https');

  // Account recovery. Mail goes to the log on this server (Email__Host=log), so the links are read from api.log.
  const links = () => {
    const log = fs.readFileSync(path.join(DATA, 'api.log'), 'utf8');
    return [...log.matchAll(/https?:\/\/\S+\/#\/(verify|reset)\/([A-Za-z0-9_-]{43})/g)].map((m) => ({ kind: m[1], token: m[2] }));
  };
  assert.strictEqual((await getJson(`${base}/api/config`)).email, true, 'recovery is on');
  await go(dan, '#/settings');
  await dan.waitForSelector('#s-email');
  await dan.fill('#s-email', 'Dan@Example.test');
  await dan.click('#s-save');
  await dan.waitForFunction(() => document.getElementById('s-save') && !document.getElementById('s-save').disabled);
  await dan.waitForFunction(() => /@/.test(document.getElementById('s-email').value));
  assert.strictEqual((await me(dan)).email, 'dan@example.test', 'stored lower-cased');
  assert.strictEqual((await me(dan)).emailVerified, false);
  const verify = links().filter((l) => l.kind === 'verify').pop();
  assert.ok(verify, 'a verification link was logged');
  await go(dan, '#/verify/' + verify.token);
  await dan.waitForSelector('#v-done');
  assert.strictEqual((await me(dan)).emailVerified, true, 'confirmed from the link');
  expected.push('POST /api/auth/verify-email -> 400');
  await go(dan, '#/verify/' + verify.token);
  await dan.waitForSelector('#v-invalid');
  await shot(dan, '26-verify-he');
  await go(dan, '#/settings');
  await dan.waitForSelector('#logout');
  await dan.click('#logout');
  await dan.waitForFunction(() => !!document.getElementById('top-auth'));
  await go(dan, '#/login');
  await dan.waitForSelector('#a-forgot');
  await dan.click('#a-forgot');
  await dan.waitForSelector('#f-key');
  await dan.fill('#f-key', 'dan');
  await dan.click('#f-submit');
  await dan.waitForSelector('#f-sent');
  await shot(dan, '27-forgot-he');
  const reset = links().filter((l) => l.kind === 'reset').pop();
  assert.ok(reset, 'a reset link was logged');
  await go(dan, '#/reset/' + reset.token);
  await dan.waitForSelector('#r-password');
  await dan.fill('#r-password', 'brand-new-pass-9');
  await dan.click('#r-submit');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await dan.waitForSelector(settled);
  assert.strictEqual((await me(dan)).handle, 'dan', 'signed in by the reset');
  // The link is single-use: the same token with another password is refused and the screen says so.
  expected.push('POST /api/auth/reset -> 400');
  await go(dan, '#/reset/' + reset.token);
  await dan.waitForSelector('#r-password');
  await dan.fill('#r-password', 'another-pass-10');
  await dan.click('#r-submit');
  await dan.waitForSelector('#r-invalid');
  // an unknown handle gets the same answer, and nothing is mailed
  const before = links().length;
  const unknown = await dan.request.post(base + '/api/auth/forgot', { headers: { 'X-Requested-With': 'Orevosh' }, data: { handleOrEmail: 'nobody-here' } });
  assert.strictEqual(unknown.status(), 202, 'the same answer for an unknown handle');
  await new Promise((r) => setTimeout(r, 500));
  assert.strictEqual(links().length, before, 'no link for an unknown handle');

  step = '13';
  // 13. Noa deletes her first look, then her account; the brand's walls empty out; Dan signs out and back in.
  await go(noa, '#/post/' + post1);
  await noa.waitForSelector('.menu-open');
  await noa.click('.menu-open');
  await noa.waitForSelector('.sheet');
  assert.strictEqual(await count(noa, '.sheet button:has-text("Report")'), 0, 'no report on your own look');
  await noa.click('.sheet button:has-text("Delete look")');
  await noa.waitForSelector('.sheet .btn-danger');
  await noa.click('.sheet .btn-danger');
  await noa.waitForFunction(() => location.hash === '#/me');
  expected.push(`GET /api/posts/${post1}/image -> 404`);
  assert.strictEqual((await get(`${base}/api/posts/${post1}/image`)).status, 404);
  await go(brand, '#/u/nexor/community');
  await brand.waitForSelector('#view .empty');
  // A moderator cannot delete the account while moderating: the owner runs --unadmin first, then it goes.
  await go(noa, '#/settings');
  await noa.waitForSelector('#delete-account');
  await noa.click('#delete-account');
  await noa.waitForSelector('.sheet .btn-danger');
  expected.push('DELETE /api/users/me -> 403');
  await noa.click('.sheet .btn-danger');
  await noa.waitForSelector('.s-account .alert:not([hidden])');
  assert.ok((await text(noa, '.s-account .alert')).includes('--unadmin'), 'the refusal names the command');
  assert.ok(/noa/.test(maintenance('--unadmin', 'noa')), '--unadmin reports the handle');
  await noa.reload();
  await noa.waitForSelector(settled);
  assert.strictEqual((await me(noa)).isAdmin, false, 'demoted');
  await go(noa, '#/settings');
  await noa.waitForSelector('#delete-account');
  await noa.click('#delete-account');
  await noa.waitForSelector('.sheet .btn-danger');
  await noa.click('.sheet .btn-danger');
  await noa.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await noa.waitForFunction(() => !!document.getElementById('top-auth'));
  const after = await metricsAs(brand);
  assert.strictEqual(after.social.users, 4, 'brand, dan, lior and maya (Round 20) remain');
  assert.strictEqual(after.social.featured, 0);
  assert.strictEqual(after.social.mentions, 0);
  assert.strictEqual(after.social.videos, 0, 'the clip left with the account');
  expected.push(`GET /api/posts/${post3}/video -> 404`);
  assert.strictEqual((await get(`${base}/api/posts/${post3}/video`)).status, 404);
  await go(brand, '#/u/nexor/featured');
  await brand.waitForSelector('#view .empty');
  await go(dan, '#/settings');
  await dan.waitForSelector('#logout');
  await dan.click('#logout');
  await dan.waitForFunction(() => !!document.getElementById('top-auth'));
  await go(dan, '#/login');
  await dan.fill('#a-handle', 'dan');
  await dan.fill('#a-password', 'wrong-password');
  expected.push('POST /api/auth/login -> 401');
  await dan.click('#a-submit');
  await dan.waitForSelector('form .alert:not([hidden])');
  assert.strictEqual(await text(dan, 'form .alert'), 'הכינוי או הסיסמה לא נכונים.');
  await dan.fill('#a-password', 'brand-new-pass-9');
  await dan.click('#a-submit');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await dan.reload();
  await dan.waitForSelector(settled);
  await dan.waitForFunction(() => !document.getElementById('top-auth'));
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'rtl');

  step = '14';
  // 14. Money (Round 20). The API comes back with Stripe on and pointed at the stub: a yearly price beside the monthly
  //     one, a seven-day no-card trial. Eli, a fourth person, reads the Pro page (the toggle, the saving computed from
  //     the two numbers, the trial line, whose price follows the toggle, and its button), back from a Checkout he
  //     cancelled on the way from a compare, so the next one still asks to return there; he buys the year on trial,
  //     and the signed webhook - posted twice, the second time ignored by its id - makes him Pro for the trial's days;
  //     the portal returns to Settings; a
  //     cancelled trial ends Pro and mails the "Pro ended" letter; and --stripe-check reads both prices from the stub.
  const stripeEnv = {
    Billing__Provider: 'stripe',
    Billing__StripeSecretKey: 'sk_test_e2e',
    Billing__StripePriceId: 'price_e2e_month',
    Billing__StripeYearlyPriceId: 'price_e2e_year',
    Billing__StripeWebhookSecret: 'whsec_e2e',
    Billing__StripeBaseUrl: `http://127.0.0.1:${STUB_PORT}/`,
    Billing__PublicOrigin: base,
    Plans__ProPriceAmount: '29',
    Plans__ProPriceCurrency: 'USD',
    Plans__ProYearlyPriceAmount: '290',
    Plans__ProTrialDays: '7',
  };
  await restartApi(stripeEnv, path.join(DATA, 'api-stripe.log'));
  maintenanceEnv = stripeEnv;
  assert.strictEqual((await getJson(`${base}/api/config`)).plans.yearly, true, 'the restarted API sells a year');
  const eli = await person(browser, 'eli', 'en-US');
  await signup(eli, 'eli', 'password123');
  const eliMe = await me(eli);
  // Stripe's cancel URL for a Checkout that started on a refused compare (BillingEndpoints puts return=compare on both).
  await go(eli, '#/pro?checkout=cancel&return=compare');
  await eli.waitForSelector('#pro-go');
  assert.strictEqual(await hash(eli), '#/pro', 'the query is stripped once it has been read');
  assert.ok(await eli.$('#pro-interval'), 'the interval toggle is drawn where a year can be sold');
  assert.match(await text(eli, '#pro-price'), /a month/, 'monthly by default');
  assert.ok((await eli.$$eval('li.pro-benefit', (items) => items.map((i) => i.textContent))).some((s) => s.includes('7 days free')), 'the trial line is drawn for a fresh account');
  assert.match(await text(eli, '#pro-trial-hint'), /^Then \$29(\.00)? a month\. Cancel before the trial ends/, 'the trial turns into the month the page is on');
  assert.strictEqual(await text(eli, '#pro-go'), 'Start 7 free days');
  await eli.click('#pro-interval-year');
  assert.match(await text(eli, '#pro-price'), /a year/, 'the year after the tap');
  assert.strictEqual(await text(eli, '#pro-saving'), 'Save 17% against paying monthly', '1 - 290 / 348, rounded');
  // Review of Round 20: Checkout sells the year after the trial, so the trial line says the year too.
  assert.match(await text(eli, '#pro-trial-hint'), /^Then \$290(\.00)? a year\. Cancel before the trial ends/, 'the trial line follows the toggle');
  await shot(eli, '50-pro-yearly-trial-en');
  await eli.click('#pro-go');
  await eli.waitForFunction(() => location.hash === '#/pro');
  await eli.waitForSelector('#pro-thanks:not([hidden])');
  const stripeForms = await getJson(`http://127.0.0.1:${STUB_PORT}/stripe`);
  const checkoutForm = stripeForms.find((r) => r.path === '/v1/checkout/sessions');
  assert.ok(checkoutForm, 'the checkout session reached the stub');
  assert.strictEqual(checkoutForm.form['line_items[0][price]'], 'price_e2e_year');
  assert.strictEqual(checkoutForm.form['subscription_data[trial_period_days]'], '7');
  assert.strictEqual(checkoutForm.form['payment_method_collection'], 'if_required');
  assert.strictEqual(checkoutForm.form['metadata[interval]'], 'year');
  assert.strictEqual(checkoutForm.form['metadata[trialDays]'], '7');
  assert.strictEqual(checkoutForm.form.currency, 'usd');
  assert.strictEqual(checkoutForm.form.client_reference_id, eliMe.id.replace(/-/g, ''));
  // The second try after a cancel still asks Checkout to come back to the compare screen once paid.
  assert.ok(checkoutForm.form.success_url.endsWith('#/pro?checkout=success&return=compare'), 'the return to the compare outlived the cancel: ' + checkoutForm.form.success_url);
  // Eli confirms an address first, so the letters below have somewhere to go (the log, on this server).
  await go(eli, '#/settings');
  await eli.waitForSelector('#s-email');
  await eli.fill('#s-email', 'eli@example.test');
  await eli.click('#s-save');
  await eli.waitForFunction(() => document.getElementById('s-save') && !document.getElementById('s-save').disabled);
  const eliLinks = () => {
    const log = fs.readFileSync(apiLog, 'utf8');
    return [...log.matchAll(/https?:\/\/\S+\/#\/(verify|reset)\/([A-Za-z0-9_-]{43})/g)].map((m) => ({ kind: m[1], token: m[2] }));
  };
  const eliVerify = eliLinks().filter((l) => l.kind === 'verify').pop();
  assert.ok(eliVerify, 'a verification link was logged for eli');
  await go(eli, '#/verify/' + eliVerify.token);
  await eli.waitForSelector('#v-done');
  assert.strictEqual((await me(eli)).emailVerified, true);
  // The webhook, signed the way Stripe signs it (StripeClient.VerifySignature), without the CSRF header Stripe cannot send.
  const crypto = require('crypto');
  const postEvent = async (event) => {
    const body = JSON.stringify(event);
    const t = Math.floor(Date.now() / 1000);
    const v1 = crypto.createHmac('sha256', 'whsec_e2e').update(`${t}.${body}`).digest('hex');
    const r = await eli.request.post(base + '/api/billing/webhook', { headers: { 'Stripe-Signature': `t=${t},v1=${v1}`, 'Content-Type': 'application/json' }, data: body });
    assert.strictEqual(r.status(), 200, 'the webhook answers 200');
    return r.json();
  };
  const completed = {
    id: 'evt_e2e_1', type: 'checkout.session.completed',
    data: { object: { id: 'cs_e2e', object: 'checkout.session', client_reference_id: eliMe.id.replace(/-/g, ''), customer: 'cus_e2e', subscription: 'sub_e2e', payment_status: 'no_payment_required', metadata: { userId: eliMe.id.replace(/-/g, ''), interval: 'year', trialDays: '7' } } },
  };
  const firstAnswer = await postEvent(completed);
  assert.strictEqual(firstAnswer.replayed, undefined, 'the first delivery is handled');
  const stateAfterFirst = await (await eli.request.get(base + '/api/billing/state')).json();
  assert.strictEqual(stateAfterFirst.plan, 'pro', 'the trial made him Pro');
  const daysOut = (new Date(stateAfterFirst.proUntil) - Date.now()) / 86400000;
  assert.ok(daysOut > 9.9 && daysOut < 10.1, `a no-card trial grants its days plus the slack, not a year: ${daysOut}`);
  const secondAnswer = await postEvent(completed);
  assert.strictEqual(secondAnswer.replayed, true, 'the same event again is ignored by its id');
  const stateAfterSecond = await (await eli.request.get(base + '/api/billing/state')).json();
  assert.strictEqual(stateAfterSecond.proUntil, stateAfterFirst.proUntil, 'a replayed checkout stacks nothing');
  assert.strictEqual(stateAfterSecond.trialDays, 0, 'a customer is never offered a second trial');
  // The tab still holds the "me" it loaded before the webhook; a reload reads the plan again, as the person's next open would.
  await eli.reload();
  await eli.waitForSelector(settled);
  await go(eli, '#/pro');
  await eli.waitForSelector('#pro-current');
  await eli.waitForSelector('#billing-manage');
  await go(eli, '#/settings');
  await eli.waitForSelector('#billing-manage');
  await go(eli, '#/pro');
  await eli.waitForSelector('#billing-manage');
  await eli.click('#billing-manage');
  await eli.waitForFunction(() => location.hash === '#/settings');
  const portalForm = (await getJson(`http://127.0.0.1:${STUB_PORT}/stripe`)).find((r) => r.path === '/v1/billing_portal/sessions');
  assert.ok(portalForm, 'the portal session reached the stub');
  assert.strictEqual(portalForm.form.customer, 'cus_e2e');
  assert.strictEqual(portalForm.form.return_url, base + '/#/settings');
  // The trial ends with no card: Stripe cancels, Pro ends, and the letter says so.
  await postEvent({ id: 'evt_e2e_2', type: 'customer.subscription.deleted', data: { object: { id: 'sub_e2e', object: 'subscription', customer: 'cus_e2e', status: 'canceled' } } });
  assert.strictEqual((await (await eli.request.get(base + '/api/billing/state')).json()).plan, 'free', 'the cancelled trial ended Pro');
  assert.ok(/Email to eli@example.test: Your OREVOSH Pro has ended/.test(fs.readFileSync(apiLog, 'utf8')), 'the "Pro ended" letter was logged');
  // --stripe-check against the stub: both prices read back and matched to the two tables, the webhook endpoint found.
  const stripeCheck = maintenance('--stripe-check');
  assert.ok(/OK\s+stripe-live/.test(stripeCheck), 'stripe-live: ' + stripeCheck);
  assert.ok(/OK\s+stripe-price/.test(stripeCheck), 'stripe-price: ' + stripeCheck);
  assert.ok(/OK\s+stripe-yearly/.test(stripeCheck), 'stripe-yearly: ' + stripeCheck);
  assert.ok(/OK\s+stripe-webhook/.test(stripeCheck), 'stripe-webhook: ' + stripeCheck);
  assert.ok(/WARN billing .*StripeBaseUrl/.test(stripeCheck), 'the doctor warns that Stripe is somewhere else: ' + stripeCheck);

  // Round 20 - the guest at the ceiling. A second API, --no-build, on the next port, against a fresh database, with a
  // ceiling so low the second guest check meets it (the stub's 1000/300 usage is 0.005 USD a call) and a push key pair
  // so the welcome screen can offer the phone. A new visitor: one look answered; the second refused with the resting
  // sentence AND the offer under it; the join link carries ?back=stylist and the form says what it promises; after the
  // signup the welcome screen draws the push step (Playwright has no push service, so it is drawn and not tapped); skip
  // lands on the check screen the offer came from.
  step = 'resting';
  const nodeCrypto = require('crypto');
  const vapid = nodeCrypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const vapidPublic = vapid.publicKey.export({ format: 'jwk' });
  const vapidPublicKey = Buffer.concat([Buffer.from([4]), Buffer.from(vapidPublic.x, 'base64url'), Buffer.from(vapidPublic.y, 'base64url')]).toString('base64url');
  const vapidPrivateKey = vapid.privateKey.export({ format: 'jwk' }).d;
  const base2 = `http://127.0.0.1:${API_PORT + 1}`;
  const restingProc = start('dotnet', ['run', '--no-build', '--project', REPO], {
    ...API_ENV,
    ASPNETCORE_URLS: base2,
    ConnectionStrings__Default: `Data Source=${path.join(DATA, 'e2e-resting.db')}`,
    Storage__Root: path.join(DATA, 'storage-resting'),
    Limits__SpendPerDayUsd: '0.001',
    Push__PublicKey: vapidPublicKey,
    Push__PrivateKey: vapidPrivateKey,
    Push__Subject: 'mailto:hello@example.test'
  }, path.join(DATA, 'api-resting.log'));
  await waitFor(`${base2}/api/config`);
  const guest = await person(browser, 'guest', 'en-US');
  expected.push('/api/auth/me -> 401');
  await guest.goto(base2 + '/#/check');
  await guest.waitForSelector('#guest-banner');
  await guest.click('.chip[data-intent=Date]');
  await choosePhoto(guest, '#photo', bigJpeg);
  await guest.waitForSelector('#photo img');
  await guest.waitForFunction(() => !document.getElementById('submit').disabled);
  await guest.click('#submit');
  await guest.waitForSelector('#result .score', { timeout: 30000 });
  // The second look: the day is spent (0.005 over a 0.001 ceiling), so the stylist rests. The gate is before the guest
  // allowance, so this is the 503 and not the guest's own 429.
  expected.push('/api/checks -> 503');
  await guest.evaluate(() => { location.hash = '#/check'; });
  await guest.waitForSelector('#photo');
  if (!(await guest.$('#photo img'))) await choosePhoto(guest, '#photo', bigJpeg);
  await guest.waitForSelector('#photo img');
  await guest.waitForFunction(() => !document.getElementById('submit').disabled);
  await guest.click('#submit');
  await guest.waitForSelector('#resting-offer', { timeout: 30000 });
  assert.strictEqual(await text(guest, '#check-error'), 'The stylist is resting until tomorrow. Your look is not spent.');
  assert.match(await text(guest, '#resting-offer p'), /Sign up now and we'll tell you when the stylist is back/);
  await shot(guest, '40-resting-offer-en');
  await guest.click('#resting-join');
  await guest.waitForFunction(() => location.hash === '#/signup?back=stylist');
  await guest.waitForSelector('#a-stylist-back');
  assert.strictEqual(await text(guest, '#a-stylist-back'), "We'll tell you when the stylist is back.");
  await guest.fill('#a-handle', 'resting');
  await guest.fill('#a-password', 'password123');
  await guest.fill('#a-dob', '1990-01-01');
  await guest.click('#a-submit');
  await guest.waitForFunction(() => location.hash === '#/welcome');
  await guest.waitForSelector('#w-push');
  assert.strictEqual(await text(guest, '#w-push h2'), 'Hear when the stylist is back');
  assert.match(await text(guest, '#w-email-step .hint'), /reaches your inbox too/);
  await shot(guest, '41-welcome-push-en');
  await guest.click('#w-skip');
  await guest.waitForFunction(() => location.hash === '#/check');
  // The ask is on the account: the row the five-minute pass will turn into the one note once the day opens.
  const restingLog = fs.readFileSync(path.join(DATA, 'api-resting.log'), 'utf8');
  assert.ok(/daily spend ceiling is reached/.test(restingLog), 'the closed day was announced once');
  await guest.context().close();
  const restingGone = new Promise((resolve) => restingProc.once('exit', resolve));
  restingProc.kill('SIGTERM');
  await restingGone;

  const stubRequests = await getJson(`http://127.0.0.1:${STUB_PORT}/`);
  assert.ok(stubRequests.length >= 3, 'stub saw the checks (incl. the retried one)');
  for (const r of stubRequests) { assert.strictEqual(r.model, 'claude-sonnet-5'); }
  // Round 19: a planned outfit is text only — no photograph ever travels with it; every other call carried the JPEG.
  const composes = stubRequests.filter((r) => r.tool === 'compose_outfit');
  assert.ok(composes.length >= 2, 'the stub saw the compose and the second idea');
  // Round 20: the two opens from the morning push added none of them.
  assert.strictEqual(composesAfterMarker, composesBeforeMarker, 'the open marker never composes');
  for (const r of composes) { assert.strictEqual(r.image_len, 0); assert.strictEqual(r.media_type, ''); assert.ok(!r.user_text.includes('@'), 'no handle in the figures'); }
  for (const r of stubRequests.filter((r) => r.tool !== 'compose_outfit')) { assert.strictEqual(r.media_type, 'image/jpeg'); }
  assert.ok(stubRequests[1].image_len < bigJpeg.length, `downscaled: ${stubRequests[1].image_len} < ${bigJpeg.length}`);
  // Round 20: with Anthropic__PromptCache=5m every check and comparison carried the five-minute breakpoint on its rubric
  // block; a planned outfit never does (its tool is per wearer). The system prompt is always at least one block, and
  // the calls made after Noa's thumbs-up carry her taste advisory as a second, uncached block: the check she made from
  // "Wearing it? Check it" and the second idea alike.
  for (const r of stubRequests.filter((r) => r.tool !== 'compose_outfit')) { assert.deepStrictEqual(r.cache_control, { type: 'ephemeral' }, 'breakpoint on ' + r.tool); }
  for (const r of composes) { assert.strictEqual(r.cache_control, null, 'no breakpoint on a compose'); }
  for (const r of stubRequests) { assert.ok(r.system_blocks >= 1, 'system blocks'); }
  assert.ok(stubRequests.some((r) => r.tool === 'submit_outfit_feedback' && r.system_blocks === 2), 'a check carried the taste advisory as its own block');
  assert.ok(composes.some((r) => r.system_blocks === 2), 'a compose carried the taste advisory as its own block');

  const i18nWarnings = consoleWarnings.filter((w) => w.startsWith('i18n:'));
  assert.deepStrictEqual(i18nWarnings, [], 'missing i18n keys: ' + i18nWarnings.join(' | '));
  const unexpectedUrls = failedUrls.filter((u) => !(u.includes('net::ERR_ABORTED') && [...noContent].some((k) => u.includes(k)))
    && !(u.includes('net::ERR_ABORTED') && /\/(image|avatar|video)|favicon|\/fonts\/|\/api\/(today|feed|board)\b/.test(u))   // a face, like a photo, may still be loading when the page moves on
    && !expected.some((e) => u.endsWith(e)));
  assert.deepStrictEqual(unexpectedUrls, [], 'unexpected failed requests: ' + unexpectedUrls.join(' | '));
  const realErrors = consoleErrors.filter((e) => !e.includes('Failed to load resource'));
  assert.deepStrictEqual(realErrors, [], 'console errors: ' + realErrors.join(' | '));

  console.log('E2E OK');
  console.log('screenshots:', fs.readdirSync(SHOTS).length, 'in', SHOTS);
  console.log('console warnings (non-i18n):', consoleWarnings.filter((w) => !w.startsWith('i18n:')));
  await browser.close();
})().then(() => { for (const p of procs) p.kill(); process.exit(0); },
  async (err) => {
    console.error('E2E FAILED at step', step + ':', err);
    console.error('failed requests:', failedUrls);
    console.error('console errors:', consoleErrors.filter((e) => !e.includes('Failed to load resource')));
    for (const [name, page] of Object.entries(pages)) {
      try {
        await page.screenshot({ path: path.join(SHOTS, `failed-${name}.png`), fullPage: true });
        fs.writeFileSync(path.join(SHOTS, `failed-${name}.html`), await page.evaluate(() => location.hash + '\n' + document.getElementById('view').outerHTML));
      } catch (e) { /* page may be gone */ }
    }
    for (const p of procs) p.kill();
    process.exit(1);
  });
