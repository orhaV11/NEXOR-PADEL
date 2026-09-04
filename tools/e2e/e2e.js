// Browser smoke test for OREVOSH: the real client and the real API in a phone-sized Chromium,
// with only the Anthropic API stubbed (stub_anthropic.py).
// Run from this folder: npm install && node e2e.js   (after `dotnet build` at the repository root).
//
// Three people take part: Noa (a person, English UI), NEXOR (a brand, English UI) and Dan (a person who
// mostly browses, Hebrew UI). The run covers: browsing signed out, signup validation, a check with the
// client-side downscale, the result screen, posting to the feed, feed tabs and intent filter, fire, save,
// comments, follow, the activity feed and badge, a brand opening a challenge, entering it from a check,
// voting, product links on a brand post, the winner being fixed when the challenge ends, photo privacy,
// deleting a post, metrics, and deleting an account.
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
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale });
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

async function go(page, hash) {
  if (!page.url().startsWith(base)) {
    await page.goto(base + '/' + hash);
  } else if (await page.evaluate(() => location.hash) === hash) {
    // Same route again: tapping the active tab refreshes it, which is what a person would do.
    await page.evaluate(() => { const tab = document.querySelector('.tab[aria-current="page"]'); if (tab) tab.click(); else window.dispatchEvent(new HashChangeEvent('hashchange')); });
  } else {
    await page.evaluate((h) => { location.hash = h; }, hash);
  }
  await page.waitForFunction((h) => location.hash === h, hash);
  await page.waitForSelector('#view h1, #view .card, #view .empty, #view .notice', { timeout: 15000 });
}
const hash = (page) => page.evaluate(() => location.hash);
const text = (page, sel) => page.textContent(sel).then((s) => (s || '').trim());
const count = async (page, sel) => (await page.$$(sel)).length;

async function signup(page, opts) {
  await go(page, '#/signup');
  await page.waitForSelector('#a-handle');
  await page.fill('#a-handle', opts.handle);
  await page.fill('#a-password', opts.password);
  if (opts.name) await page.fill('#a-name', opts.name);
  if (opts.brand) await page.check('#a-brand');
  await page.check('#a-age');
  await page.click('#a-submit');
  await page.waitForFunction(() => location.hash === '#/feed');
  await page.waitForFunction((n) => document.getElementById('top-auth').textContent === n, opts.name || opts.handle);
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

async function runCheck(page, opts) {
  await go(page, '#/check');
  await page.waitForSelector('#photo');
  if (opts.intent) await page.click(`.chip[data-intent=${opts.intent}]`);
  await page.setInputFiles('#file', { name: 'outfit.jpg', mimeType: 'image/jpeg', buffer: opts.buffer });
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
  if (opts.challengeId !== undefined) await page.selectOption('#challenge-pick', opts.challengeId);
  if (opts.products) {
    const inputs = await page.$$('.products-grid input');
    assert.strictEqual(inputs.length, 9, 'three product rows for a brand');
    for (const [i, p] of opts.products.entries()) {
      await inputs[i * 3].fill(p.label);
      await inputs[i * 3 + 1].fill(p.url);
      if (p.price) await inputs[i * 3 + 2].fill(p.price);
    }
  }
  if (opts.beforeConfirm) await opts.beforeConfirm();
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
  // 1. A signed-out visitor with a Hebrew browser: Hebrew, RTL, an empty feed, and the check tab asks to sign in.
  expected.push('GET /api/auth/me -> 401');
  await dan.goto(base + '/');
  await dan.waitForSelector('#view h1');
  assert.strictEqual(await dan.getAttribute('html', 'lang'), 'he');
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'rtl');
  assert.strictEqual(await text(dan, '#view h1'), 'פיד');
  await dan.waitForSelector('#feed-list .empty');
  assert.strictEqual(await text(dan, '.tab[data-tab=feed] span'), 'פיד');
  assert.strictEqual(await text(dan, '#top-auth'), 'הצטרפות');
  await shot(dan, '01-feed-empty-he');
  await go(dan, '#/check');
  assert.strictEqual(await text(dan, '.notice h3'), 'צריך להתחבר בשביל זה');
  await go(dan, '#/feed/following');
  assert.strictEqual(await text(dan, '.notice h3'), 'צריך להתחבר בשביל זה');

  step = '2';
  // 2. Noa signs up in English. The age box is required; the server says so in the UI's language.
  await noa.goto(base + '/');
  await noa.waitForSelector('#view h1');
  assert.strictEqual(await noa.getAttribute('html', 'lang'), 'en');
  await go(noa, '#/signup');
  await noa.waitForSelector('#a-handle');
  await noa.fill('#a-handle', 'noa');
  await noa.fill('#a-password', 'password123');
  await noa.fill('#a-name', 'Noa');
  await shot(noa, '02-signup-en');
  expected.push('POST /api/auth/signup -> 400');
  await noa.click('#a-submit');
  await noa.waitForSelector('form .alert:not([hidden])');
  assert.ok((await text(noa, 'form .alert')).length > 10, 'age error shown');
  await noa.check('#a-age');
  await noa.click('#a-submit');
  await noa.waitForFunction(() => location.hash === '#/feed');
  await noa.waitForFunction(() => document.getElementById('top-auth').textContent === 'Noa');
  assert.strictEqual(await noa.getAttribute('.tab[data-tab=feed]', 'aria-current'), 'page');

  step = '3';
  // 3. Noa checks a look (large JPEG, downscaled on the client) and posts it to the feed.
  const bigJpeg = await makeJpeg(noa, 1800, 2400);
  await runCheck(noa, { intent: 'Date', occasion: 'dinner with friends', buffer: bigJpeg, score: 7, beforeSubmit: () => shot(noa, '03-check-ready-en') });
  assert.strictEqual(await text(noa, '.result-headline'), 'Clean casual with one weak link');
  assert.strictEqual(await text(noa, '.vibe'), 'relaxed weekend');
  assert.strictEqual(await count(noa, '.items li'), 3);
  assert.deepStrictEqual(await noa.$$eval('.item-verdict', (n) => n.map((x) => x.textContent)), ['Works', 'Neutral', 'Weak']);
  assert.strictEqual(await count(noa, '.working li'), 2);
  assert.strictEqual(await text(noa, '.tip p'), 'Swap the running shoes for plain white leather sneakers.');
  assert.strictEqual(await noa.getAttribute('.bar', 'aria-valuenow'), '72');
  await shot(noa, '04-result-en');
  await noa.click('#post-open');
  await noa.waitForSelector('#post-confirm');
  assert.deepStrictEqual(await noa.$$eval('#challenge-pick option', (o) => o.map((x) => x.textContent)), ['Just the feed'], 'no challenges yet');
  assert.strictEqual(await count(noa, '.products-grid'), 0, 'people do not get product links');
  await noa.fill('#caption', 'Dinner fit, thoughts?');
  await shot(noa, '05-post-sheet-en');
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const post1 = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
  assert.match(post1, /^[0-9a-f-]{36}$/);
  await noa.click('#post-link');
  await noa.waitForSelector('#view .card');
  assert.strictEqual(await text(noa, '.card .headline'), 'Clean casual with one weak link');
  assert.strictEqual(await text(noa, '.card .caption'), 'Dinner fit, thoughts?');
  assert.strictEqual(await text(noa, '.card .score-badge'), '7/10');
  assert.strictEqual(await text(noa, '.card-head .tag'), 'Date');
  assert.strictEqual(await text(noa, '.comments li'), 'No comments yet.');
  await shot(noa, '06-post-view-en');

  step = '4';
  // 4. The feed: one card, the intent filter hides and shows it, "top" and "fresh" both list it.
  await go(noa, '#/feed');
  await noa.waitForSelector('#feed-list .card');
  assert.strictEqual(await count(noa, '#feed-list .card'), 1);
  await noa.click('.chip[data-intent=Office]');
  await noa.waitForSelector('#feed-list .empty');
  assert.strictEqual(await count(noa, '#feed-list .card'), 0);
  await noa.click('.chip[data-intent=""]');
  await noa.waitForSelector('#feed-list .card');
  await shot(noa, '07-feed-en');
  await go(noa, '#/feed/top');
  await noa.waitForSelector('#feed-list .card');
  assert.strictEqual(await noa.getAttribute('.segment:nth-child(2)', 'aria-pressed'), 'true');

  // The signed-out visitor can open the post and read it, but is asked to sign in to comment.
  await go(dan, '#/post/' + post1);
  await dan.waitForSelector('#view .card');
  assert.strictEqual(await text(dan, '.card .caption'), 'Dinner fit, thoughts?');
  assert.strictEqual(await text(dan, 'a[href="#/login"].btn-text'), 'מתחברים כדי להגיב');
  assert.ok((await text(dan, '.card .match span')).startsWith('משדר דייט'), await text(dan, '.card .match span'));

  step = '5';
  // 5. NEXOR signs up as a brand and opens a challenge.
  await brand.goto(base + '/');
  await brand.waitForSelector('#view h1');
  await signup(brand, { handle: 'nexor', password: 'password123', name: 'NEXOR', brand: true });
  await go(brand, '#/challenges');
  await brand.waitForSelector('a[href="#/new-challenge"]');
  assert.strictEqual(await text(brand, '#view .empty'), 'No open challenges right now.');
  await brand.click('a[href="#/new-challenge"]');
  await brand.waitForSelector('#nc-submit');
  await brand.fill('#nc-title', 'Date night in black');
  await brand.click('.chip:has-text("Date")');
  await brand.fill('#nc-brief', 'All-black date looks. Texture over logos.');
  await brand.fill('#nc-prize', 'A black shirt of your choice');
  await brand.fill('#nc-url', 'https://example.com/black-shirt');
  await shot(brand, '08-challenge-form');
  await brand.click('#nc-submit');
  await brand.waitForFunction(() => /^#\/challenge\/[0-9a-f-]{36}$/.test(location.hash));
  const challengeId = (await hash(brand)).replace('#/challenge/', '');
  await brand.waitForSelector('.challenge-title');
  assert.strictEqual(await text(brand, 'h1.challenge-title'), 'Date night in black');
  assert.match(await text(brand, '.countdown'), /^Ends in 3 days$/);
  assert.ok((await text(brand, '.prize')).includes('A black shirt of your choice'));
  assert.strictEqual(await text(brand, '#view .empty'), 'No entries yet. Be the first.');
  assert.strictEqual(await count(brand, 'button:has-text("Enter with a check")'), 0, 'the brand cannot enter its own challenge');
  await shot(brand, '09-challenge-new');

  step = '6';
  // 6. The brand reacts to Noa's look: fire, save, comment, follow.
  await go(brand, '#/feed');
  await brand.waitForSelector('#feed-list .card');
  assert.strictEqual(await brand.getAttribute('.card .action.fire', 'aria-pressed'), 'false');
  await brand.click('.card .action.fire');
  await brand.waitForFunction(() => document.querySelector('.card .action.fire').getAttribute('aria-pressed') === 'true');
  assert.strictEqual(await text(brand, '.card .action.fire .count'), '1');
  await brand.click('.card .action.save');
  await brand.waitForFunction(() => document.querySelector('.card .action.save').getAttribute('aria-pressed') === 'true');
  await brand.click('.card .card-photo');
  await brand.waitForSelector('#comment-input');
  await brand.fill('#comment-input', 'Love the palette. Shoes could go leather.');
  await brand.click('#comment-send');
  await brand.waitForSelector('.comments li .text b');
  assert.strictEqual(await text(brand, '.comments li .text b'), 'NEXOR');
  assert.ok((await text(brand, '.comments li .text')).includes('Love the palette'));
  await brand.click('.card-head a.name');
  await brand.waitForSelector('#follow');
  assert.strictEqual(await text(brand, '#follow'), 'Follow');
  await brand.click('#follow');
  await brand.waitForFunction(() => document.getElementById('follow') && document.getElementById('follow').getAttribute('aria-pressed') === 'true');
  assert.strictEqual(await text(brand, '#follow'), 'Following');
  assert.deepStrictEqual(await brand.$$eval('.stats .stat b', (n) => n.map((x) => x.textContent)), ['1', '1', '0', '1', '7', '1']);
  await shot(brand, '10-profile-noa-by-brand');
  await go(brand, '#/saved');
  await brand.waitForSelector('.grid a');
  assert.strictEqual(await count(brand, '.grid a'), 1);

  step = '7';
  // 7. Noa's activity: three unread (fire, comment, follow), the badge shows 3 and clears after reading.
  await noa.reload();
  await noa.waitForSelector('#view h1');
  await noa.waitForFunction(() => !document.getElementById('activity-badge').hidden);
  assert.strictEqual(await text(noa, '#activity-badge'), '3');
  await go(noa, '#/activity');
  await noa.waitForSelector('.activity li');
  const activity = await noa.$$eval('.activity li a > div:first-child', (n) => n.map((x) => x.textContent));
  assert.deepStrictEqual(activity.sort(), ['NEXOR commented on your look', 'NEXOR set your look on fire', 'NEXOR started following you']);
  assert.strictEqual(await count(noa, '.activity li.unread'), 3);
  await noa.waitForFunction(() => document.getElementById('activity-badge').hidden);
  await shot(noa, '11-activity-noa');
  await go(noa, '#/post/' + post1);
  await noa.waitForSelector('.card');
  assert.strictEqual(await text(noa, '.card .action.fire .count'), '1');
  assert.strictEqual(await text(noa, '.card a.action .count'), '1', 'comment count on the card');

  step = '8';
  // 8. Noa enters the challenge from its card: the intent is locked to the challenge's, the post sheet preselects it.
  await go(noa, '#/challenges');
  await noa.waitForSelector('.challenge-card');
  assert.strictEqual(await text(noa, '.challenge-title'), 'Date night in black');
  assert.strictEqual(await text(noa, '.challenge-meta'), 'Date' + 'by NEXOR' + '0 entries');
  await noa.click('button:has-text("Enter with a check")');
  await noa.waitForSelector('#photo');
  assert.strictEqual(await text(noa, '.alert b'), 'Entering: Date night in black');
  assert.strictEqual(await noa.getAttribute('.chip[data-intent=Date]', 'aria-pressed'), 'true');
  assert.strictEqual(await noa.isDisabled('.chip[data-intent=Casual]'), true);
  await shot(noa, '12-check-entering');
  await runCheck(noa, { buffer: bigJpeg, score: 7 });
  await noa.click('#post-open');
  await noa.waitForSelector('#post-confirm');
  await noa.waitForFunction((id) => document.getElementById('challenge-pick').value === id, challengeId);
  assert.strictEqual(await count(noa, '#challenge-pick option'), 2);
  await noa.click('#post-confirm');
  await noa.waitForSelector('#post-link');
  const post2 = (await noa.getAttribute('#post-link', 'href')).replace('#/post/', '');
  await go(noa, '#/challenge/' + challengeId);
  await noa.waitForSelector('.lb-row');
  assert.strictEqual(await count(noa, '.lb-row'), 1);
  assert.strictEqual(await text(noa, '.lb-who .name'), 'Noa');
  assert.ok((await text(noa, '.lb-who .votes')).startsWith('0 votes'));
  assert.strictEqual(await noa.isDisabled('.lb-row .vote'), true, 'no voting for your own entry');
  assert.strictEqual(await text(noa, '.challenge-card .tag.accent'), "You're in");
  await shot(noa, '13-leaderboard');

  step = '9';
  // 9. Dan signs up in Hebrew, scrolls the feed, fires, votes for Noa, follows her, and sees a "following" feed.
  await signup(dan, { handle: 'dan', password: 'password123' });
  await go(dan, '#/feed');
  await dan.waitForSelector('#feed-list .card');
  assert.strictEqual(await count(dan, '#feed-list .card'), 2);
  assert.strictEqual(await text(dan, '.card:nth-child(1) .challenge-link'), 'באתגר: Date night in black');
  await shot(dan, '14-feed-he');
  await dan.click('.card:nth-child(2) .action.fire');
  await dan.waitForFunction(() => document.querySelector('.card:nth-child(2) .action.fire').getAttribute('aria-pressed') === 'true');
  assert.strictEqual(await text(dan, '.card:nth-child(2) .action.fire .count'), '2');
  await dan.click('.card:nth-child(1) .challenge-link');
  await dan.waitForSelector('.lb-row .vote');
  assert.strictEqual(await text(dan, '.lb-row .vote'), 'להצביע');
  await dan.click('.lb-row .vote');
  await dan.waitForFunction(() => document.querySelector('.lb-row .vote') && document.querySelector('.lb-row .vote').getAttribute('aria-pressed') === 'true');
  assert.strictEqual(await text(dan, '.lb-row .vote'), 'ההצבעה שלך');
  assert.ok((await text(dan, '.lb-who .votes')).startsWith('1 הצבעות'), await text(dan, '.lb-who .votes'));
  await shot(dan, '15-vote-he');
  await go(dan, '#/u/noa');
  await dan.waitForSelector('#follow');
  await dan.click('#follow');
  await dan.waitForFunction(() => document.getElementById('follow') && document.getElementById('follow').getAttribute('aria-pressed') === 'true');
  assert.strictEqual(await text(dan, '#follow'), 'עוקבים');
  await go(dan, '#/feed/following');
  await dan.waitForSelector('#feed-list .card');
  assert.strictEqual(await count(dan, '#feed-list .card'), 2);
  await go(dan, '#/feed/top');
  await dan.waitForSelector('#feed-list .card');
  assert.strictEqual(await text(dan, '.card:nth-child(1) .action.fire .count'), '2', 'top sorts by fire');

  step = '10';
  // 10. A brand post carries product links; the feed shows "shop the look".
  await runCheck(brand, { intent: 'Minimal', buffer: bigJpeg, score: 7 });
  const post3 = await postIt(brand, { caption: 'The shirt from the challenge.', products: [{ label: 'Black shirt', url: 'https://example.com/black-shirt', price: '₪199' }] });
  await go(brand, '#/feed');
  await brand.waitForSelector('#feed-list .card');
  assert.strictEqual(await count(brand, '#feed-list .card'), 3);
  assert.strictEqual(await text(brand, '.card:nth-child(1) .brand-mark'), 'Brand');
  assert.strictEqual(await text(brand, '.card:nth-child(1) .shop a'), 'Black shirt₪199');
  assert.strictEqual(await brand.getAttribute('.card:nth-child(1) .shop a', 'href'), 'https://example.com/black-shirt');
  await shot(brand, '16-feed-brand-post');
  await go(brand, '#/activity');
  await brand.waitForSelector('.activity li');
  assert.deepStrictEqual(await brand.$$eval('.activity li a > div:first-child', (n) => n.map((x) => x.textContent)), ['Noa entered your challenge']);

  step = '11';
  // 11. Time passes: the challenge ends, the winner is fixed on the next read, both sides get told.
  const changed = execFileSync('python3', ['-c', [
    'import sqlite3, sys',
    'c = sqlite3.connect(sys.argv[1])',
    "n = c.execute(\"update Challenges set EndsAt = strftime('%Y-%m-%d %H:%M:%S', 'now', '-1 hour')\").rowcount",
    'c.commit(); print(n)'].join('\n'), DB]).toString().trim();
  assert.strictEqual(changed, '1');
  await go(dan, '#/challenges/ended');
  await dan.waitForSelector('.challenge-card');
  assert.strictEqual(await text(dan, '.challenge-card .tag.accent'), 'הזכייה');
  await dan.click('.challenge-card .challenge-title');
  await dan.waitForSelector('.lb-row.winner');
  assert.match(await text(dan, '.countdown'), /^הסתיים לפני שעה$/);
  assert.strictEqual(await count(dan, '.card[data-post="' + post2 + '"]'), 1, 'winner card shown');
  assert.strictEqual(await dan.isDisabled('.lb-row .vote'), true, 'voting closed');
  await shot(dan, '17-winner-he');
  await go(noa, '#/activity');
  await noa.waitForSelector('.activity li');
  const noaActivity = await noa.$$eval('.activity li a > div:first-child', (n) => n.map((x) => x.textContent));
  assert.ok(noaActivity.includes("You won NEXOR's challenge"), noaActivity.join(' | '));
  assert.ok(noaActivity.includes('dan voted for your entry'), noaActivity.join(' | '));
  await go(brand, '#/activity');
  await brand.waitForSelector('.activity li');
  assert.ok((await brand.$$eval('.activity li a > div:first-child', (n) => n.map((x) => x.textContent))).includes('Your challenge ended. Winner: Noa'));
  await go(brand, '#/challenges');
  await brand.waitForSelector('#view .empty');
  assert.strictEqual(await text(brand, '#view .empty'), 'No open challenges right now.');

  step = '12';
  // 12. Photos: a public post serves its photo through the API only; nothing under storage is reachable by path.
  const image = await get(`${base}/api/posts/${post1}/image`);
  assert.strictEqual(image.status, 200);
  assert.strictEqual(image.headers['content-type'], 'image/jpeg');
  assert.ok(image.headers['cache-control'].includes('private'));
  const storage = path.join(DATA, 'storage');
  const files = [];
  for (const user of fs.readdirSync(storage)) for (const f of fs.readdirSync(path.join(storage, user))) files.push(user + '/' + f);
  assert.strictEqual(files.length, 3, 'three OK checks keep their photos: ' + files.join(','));
  for (const rel of files) {
    for (const url of [`${base}/${rel}`, `${base}/storage/${rel}`, `${base}/wwwroot/${rel}`]) {
      assert.strictEqual((await get(url)).status, 404, `photo reachable at ${url}`);
    }
  }

  step = '13';
  // 13. Noa deletes her first post: it leaves the feed and its photo goes private again.
  await go(noa, '#/post/' + post1);
  await noa.waitForSelector('.menu-open');
  await noa.click('.menu-open');
  await noa.waitForSelector('#post-delete');
  assert.strictEqual(await count(noa, '#post-report'), 0, 'no report button on your own post');
  await noa.click('#post-delete');
  await noa.waitForFunction(() => location.hash === '#/me');
  await noa.waitForSelector('.grid a');
  assert.strictEqual(await count(noa, '.grid a'), 1);
  expected.push(`GET /api/posts/${post1}/image -> 404`);
  assert.strictEqual((await get(`${base}/api/posts/${post1}/image`)).status, 404);
  await shot(noa, '18-profile-me');
  await go(noa, '#/checks');
  await noa.waitForSelector('.activity li');
  assert.strictEqual(await count(noa, '.activity li'), 2);
  assert.strictEqual(await count(noa, '.activity li .pill.accent'), 1, 'the unposted check can be posted from the list');

  step = '14';
  // 14. Metrics (post 1 took its two fires and its comment with it), then Noa deletes her account:
  //     her posts, photos and entry vanish; the others keep theirs.
  const metrics = await getJson(`${base}/api/metrics/pilot`);
  assert.strictEqual(metrics.totalChecks, 3);
  assert.deepStrictEqual(metrics.social, { users: 3, brands: 1, posts: 2, fires: 0, follows: 2, comments: 0, challengesOpen: 0, challengesEnded: 1, votes: 1, activeUsers7d: 3 });
  await go(noa, '#/settings');
  await noa.waitForSelector('#delete-account');
  await shot(noa, '19-settings');
  await noa.click('#delete-account');
  await noa.waitForFunction(() => location.hash === '#/feed');
  await noa.waitForFunction(() => document.getElementById('top-auth').textContent === 'Join');
  await noa.waitForSelector('#feed-list .card');
  assert.strictEqual(await count(noa, '#feed-list .card'), 1, 'only the brand post remains');
  assert.strictEqual(fs.readdirSync(storage).length, 1, 'only the brand folder remains');
  const after = await getJson(`${base}/api/metrics/pilot`);
  assert.strictEqual(after.social.users, 2);
  assert.strictEqual(after.social.posts, 1);
  assert.strictEqual(after.social.fires, 0);
  expected.push('GET /api/users/noa -> 404');
  await go(dan, '#/u/noa');
  assert.strictEqual(await text(dan, '#view .empty'), 'לא מצאנו את הפרופיל הזה.');
  const detail = await getJson(`${base}/api/challenges/${challengeId}`);
  assert.strictEqual(detail.winner, undefined, 'the winning entry is gone with its author');
  assert.strictEqual(detail.entriesByVotes.length, 0);

  step = '15';
  // 15. Sign out and back in; the language preference and session survive a reload.
  await go(dan, '#/me');
  await dan.waitForSelector('#logout');
  await dan.click('#logout');
  await dan.waitForFunction(() => document.getElementById('top-auth').textContent === 'הצטרפות');
  await go(dan, '#/login');
  await dan.fill('#a-handle', 'dan');
  await dan.fill('#a-password', 'wrong-password');
  expected.push('POST /api/auth/login -> 401');
  await dan.click('#a-submit');
  await dan.waitForSelector('form .alert:not([hidden])');
  assert.strictEqual(await text(dan, 'form .alert'), 'הכינוי או הסיסמה לא נכונים.');
  await dan.fill('#a-password', 'password123');
  await dan.click('#a-submit');
  await dan.waitForFunction(() => location.hash === '#/feed');
  await dan.reload();
  await dan.waitForSelector('#view h1');
  await dan.waitForFunction(() => document.getElementById('top-auth').textContent === 'dan');
  assert.strictEqual(await dan.getAttribute('html', 'dir'), 'rtl');

  const stubRequests = await getJson(`http://127.0.0.1:${STUB_PORT}/`);
  assert.ok(stubRequests.length >= 4, 'stub saw the requests (incl. the retried one)');
  for (const r of stubRequests) {
    assert.strictEqual(r.media_type, 'image/jpeg');
    assert.strictEqual(r.thinking && r.thinking.type, 'disabled');
    assert.strictEqual(r.model, 'claude-sonnet-5');
  }
  assert.ok(stubRequests[1].image_len < bigJpeg.length, `downscaled: ${stubRequests[1].image_len} < ${bigJpeg.length}`);
  assert.ok(stubRequests[1].user_text.includes('dinner with friends'));

  const i18nWarnings = consoleWarnings.filter((w) => w.startsWith('i18n:'));
  assert.deepStrictEqual(i18nWarnings, [], 'missing i18n keys: ' + i18nWarnings.join(' | '));
  // Chromium reports a body-less 204 fetch as net::ERR_ABORTED after the response arrived, and a reload cancels
  // lazy images still in flight; neither is a failure.
  const unexpectedUrls = failedUrls.filter((u) => !u.includes('fonts.googleapis.com') && !u.includes('fonts.gstatic.com')
    && !(u.includes('net::ERR_ABORTED') && [...noContent].some((k) => u.includes(k)))
    && !(u.includes('net::ERR_ABORTED') && /\/image -> /.test(u))
    && !expected.some((e) => u.endsWith(e)));
  assert.deepStrictEqual(unexpectedUrls, [], 'unexpected failed requests: ' + unexpectedUrls.join(' | '));
  // Chromium logs every non-2xx response as a console error; those are judged above through failedUrls.
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
