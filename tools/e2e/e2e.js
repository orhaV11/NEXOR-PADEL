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
const consoleErrors = [];
const consoleWarnings = [];
const failedUrls = [];
const expected = [];          // "METHOD /path -> status" entries that a step deliberately provokes
const noContent = new Set();
const pages = {};
let step = 'boot';

async function person(browser, name, locale) {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale, serviceWorkers: 'block' });
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

async function signup(page, handle, password) {
  await go(page, '#/signup');
  await page.waitForSelector('#a-handle');
  await page.fill('#a-handle', handle);
  await page.fill('#a-password', password);
  await page.check('#a-age');
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
  const [chooser] = await Promise.all([page.waitForEvent('filechooser'), page.click(buttonSelector)]);
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

(async () => {
  start('python3', [path.join(ROOT, 'stub_anthropic.py'), String(STUB_PORT)], {}, path.join(DATA, 'stub.log'));
  start('dotnet', ['run', '--no-build', '--project', REPO], {
    ASPNETCORE_URLS: `http://127.0.0.1:${API_PORT}`,
    ANTHROPIC_API_KEY: 'stub-key-not-real',
    Anthropic__BaseUrl: `http://127.0.0.1:${STUB_PORT}`,
    ConnectionStrings__Default: `Data Source=${DB}`,
    Storage__Root: path.join(DATA, 'storage'),
  }, path.join(DATA, 'api.log'));

  await waitFor(`http://127.0.0.1:${STUB_PORT}/`);
  await waitFor(`${base}/api/metrics/pilot`);
  const stubState = await getJson(`http://127.0.0.1:${STUB_PORT}/`);
  assert.deepStrictEqual(stubState, [], `port ${STUB_PORT} is served by a stale stub with ${stubState.length} recorded requests; kill it first`);

  const browser = await chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});
  const noa = await person(browser, 'noa', 'en-US');
  const brand = await person(browser, 'brand', 'en-US');
  const dan = await person(browser, 'dan', 'he-IL');
  const shot = (page, name) => page.screenshot({ path: path.join(SHOTS, name + '.png'), fullPage: true });

  step = '1';
  // 1. PWA surface and a signed-out visitor with a Hebrew browser: OREVOSH wordmark, RTL, empty feed, Explore, sign-in prompts.
  const manifest = await get(`${base}/manifest.webmanifest`);
  assert.strictEqual(manifest.status, 200);
  assert.strictEqual(JSON.parse(manifest.body.toString()).name, 'OREVOSH');
  assert.strictEqual((await get(`${base}/sw.js`)).status, 200);
  assert.strictEqual((await get(`${base}/icons/icon-192.png`)).headers['content-type'], 'image/png');
  expected.push('GET /api/auth/me -> 401');
  await dan.goto(base + '/');
  await dan.waitForSelector(settled);
  assert.strictEqual(await dan.getAttribute('html', 'lang'), 'he');
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'rtl');
  assert.strictEqual(await text(dan, '.wordmark'), 'OREVOSH');
  assert.strictEqual(await dan.getAttribute('meta[name="apple-mobile-web-app-capable"]', 'content'), 'yes');
  assert.strictEqual(await text(dan, '.tab[data-tab=home] span'), 'בית');
  assert.strictEqual(await text(dan, '.tab[data-tab=explore] span'), 'גילוי');
  assert.strictEqual(await text(dan, '#top-auth'), 'הצטרפות');
  await dan.waitForSelector('#view .empty');
  await shot(dan, '01-home-empty-he');
  await go(dan, '#/explore');
  assert.strictEqual(await text(dan, '#view h1'), 'גילוי');
  await dan.waitForSelector('#search');
  await shot(dan, '02-explore-empty-he');
  await go(dan, '#/check');
  assert.strictEqual(await text(dan, '.notice h3'), 'צריך להתחבר בשביל זה');
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
  await noa.fill('#a-handle', 'noa');
  await noa.fill('#a-password', 'password123');
  await shot(noa, '03-signup-en');
  expected.push('POST /api/auth/signup -> 400');
  await noa.click('#a-submit');
  await noa.waitForSelector('form .alert:not([hidden])');
  await noa.check('#a-age');
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
  await brand.check('#s-brand');
  await brand.click('#s-save');
  await brand.waitForFunction(() => document.getElementById('s-save') && !document.getElementById('s-save').disabled);
  await brand.waitForFunction(async () => true);
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
  assert.strictEqual(await text(brand, '.profile-head .brand-mark'), 'Brand');
  assert.ok(await brand.$('.profile-head .avatar img'), 'avatar shown on the profile');
  assert.strictEqual(await count(brand, '.profile-tabs button'), 3, 'brands get Looks, Community and Featured');
  await shot(brand, '06-profile-brand-en');

  step = '4';
  // 4. Noa checks a look and posts it with a #tag and an @mention of the brand.
  const bigJpeg = await makeJpeg(noa, 1800, 2400);
  await runCheck(noa, { intent: 'Date', occasion: 'dinner with friends', buffer: bigJpeg, score: 7, beforeSubmit: () => shot(noa, '07-check-ready-en') });
  assert.strictEqual(await text(noa, '.result-headline'), 'Clean casual with one weak link');
  assert.deepStrictEqual(await noa.$$eval('.item-verdict', (n) => n.map((x) => x.textContent)), ['Works', 'Neutral', 'Weak']);
  assert.strictEqual(await noa.getAttribute('.bar', 'aria-valuenow'), '72');
  await shot(noa, '08-result-en');
  await noa.click('#post-open');
  await noa.waitForSelector('#post-confirm');
  assert.strictEqual(await count(noa, '.products-grid'), 0, 'people do not get product links');
  await noa.fill('#caption', 'Dinner fit, thoughts? #datenight #DateNight @nexor @nobody');
  await shot(noa, '09-post-sheet-en');
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const post1 = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
  await noa.click('#post-link');
  await noa.waitForSelector('#view .card');
  assert.strictEqual(await text(noa, '.card .headline'), 'Clean casual with one weak link');
  // @nobody is not an account, so it stays plain text; the tags and the brand become links.
  assert.deepStrictEqual(await noa.$$eval('.card .caption a', (n) => n.map((x) => x.getAttribute('href'))), ['#/tag/datenight', '#/tag/datenight', '#/u/nexor']);
  assert.ok((await text(noa, '.card .caption')).includes('@nobody'));
  await noa.waitForSelector('.person');
  assert.strictEqual(await text(noa, '.person .name'), 'NEXORBrand', 'tagged brand listed');
  assert.strictEqual(await text(noa, '.card .score-badge'), '7/10');
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

  step = '6';
  // 6. Explore, signed out: trending tag, the brand, the top look, search, the tag page.
  await go(dan, '#/explore');
  await dan.waitForSelector('a[href="#/tag/datenight"]');
  assert.ok((await text(dan, 'a[href="#/tag/datenight"]')).includes('#datenight'));
  await dan.waitForSelector('.brand-card');
  assert.strictEqual(await text(dan, '.brand-card .name'), 'NEXOR');
  assert.strictEqual(await count(dan, '.grid a'), 1, 'top look this week');
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
  assert.strictEqual(await text(dan, '.card .featured'), 'הוצג על ידי NEXOR');
  await shot(dan, '14-tag-he');

  step = '7';
  // 7. Dan signs up in Hebrew, follows the brand from the welcome screen, sees the look in For you, double-taps to fire.
  await signup(dan, 'dan', 'password123');
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
  await go(dan, '#/u/noa');
  await dan.waitForSelector('#follow, .profile-head');
  await dan.click('.profile-head ~ * button.btn, button.btn:has-text("לעקוב")');
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
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const post2 = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
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

  step = '9';
  // 9. Photos: the post image route and the avatar route are the only doors; nothing under storage is reachable by path.
  assert.strictEqual((await get(`${base}/api/posts/${post1}/image`)).status, 200);
  const storage = path.join(DATA, 'storage');
  const files = [];
  for (const user of fs.readdirSync(storage)) for (const f of fs.readdirSync(path.join(storage, user))) files.push(user + '/' + f);
  assert.strictEqual(files.length, 3, 'two OK checks plus one avatar: ' + files.join(','));
  for (const rel of files) {
    for (const url of [`${base}/${rel}`, `${base}/storage/${rel}`, `${base}/wwwroot/${rel}`]) {
      assert.strictEqual((await get(url)).status, 404, `photo reachable at ${url}`);
    }
  }
  const metrics = await getJson(`${base}/api/metrics/pilot`);
  assert.strictEqual(metrics.social.mentions, 1);
  assert.strictEqual(metrics.social.featured, 2);
  assert.strictEqual(metrics.social.brands, 1);

  step = '10';
  // 10. Noa deletes her first look, then her account; the brand's walls empty out; Dan signs out and back in.
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
  await go(noa, '#/settings');
  await noa.waitForSelector('#delete-account');
  await noa.click('#delete-account');
  await noa.waitForSelector('.sheet .btn-danger');
  await noa.click('.sheet .btn-danger');
  await noa.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await noa.waitForFunction(() => !!document.getElementById('top-auth'));
  const after = await getJson(`${base}/api/metrics/pilot`);
  assert.strictEqual(after.social.users, 2);
  assert.strictEqual(after.social.featured, 0);
  assert.strictEqual(after.social.mentions, 0);
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
  await dan.fill('#a-password', 'password123');
  await dan.click('#a-submit');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await dan.reload();
  await dan.waitForSelector(settled);
  await dan.waitForFunction(() => !document.getElementById('top-auth'));
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'rtl');

  const stubRequests = await getJson(`http://127.0.0.1:${STUB_PORT}/`);
  assert.ok(stubRequests.length >= 3, 'stub saw the checks (incl. the retried one)');
  for (const r of stubRequests) { assert.strictEqual(r.media_type, 'image/jpeg'); assert.strictEqual(r.model, 'claude-sonnet-5'); }
  assert.ok(stubRequests[1].image_len < bigJpeg.length, `downscaled: ${stubRequests[1].image_len} < ${bigJpeg.length}`);

  const i18nWarnings = consoleWarnings.filter((w) => w.startsWith('i18n:'));
  assert.deepStrictEqual(i18nWarnings, [], 'missing i18n keys: ' + i18nWarnings.join(' | '));
  const unexpectedUrls = failedUrls.filter((u) => !u.includes('fonts.googleapis.com') && !u.includes('fonts.gstatic.com')
    && !(u.includes('net::ERR_ABORTED') && [...noContent].some((k) => u.includes(k)))
    && !(u.includes('net::ERR_ABORTED') && /\/(image|avatar)/.test(u))
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
