// Browser smoke test: the FitCheck client against the real API, with only the Anthropic API stubbed.
// Run from this folder: npm install && npx playwright install chromium && node e2e.js  (after `dotnet build` at the repo root).
// Exercises: locale auto-detect (he-IL), RTL, language switch without reload, onboarding, the
// downscale + multipart upload, the loading state, the result screen in both languages, history,
// not_outfit state, photo privacy (no URL serves a photo), and account deletion.
const { chromium } = require('playwright');
const { spawn } = require('child_process');
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

fs.rmSync(DATA, { recursive: true, force: true });
fs.mkdirSync(DATA, { recursive: true });
fs.mkdirSync(SHOTS, { recursive: true });

function get(url, headers) {
  return new Promise((resolve, reject) => {
    http.get(url, { headers }, (res) => {
      let body = '';
      res.on('data', (c) => (body += c));
      res.on('end', () => resolve({ status: res.statusCode, body }));
    }).on('error', reject);
  });
}

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

(async () => {
  start('python3', [path.join(ROOT, 'stub_anthropic.py'), String(STUB_PORT)], {}, path.join(DATA, 'stub.log'));
  start('dotnet', ['run', '--no-build', '--project', REPO], {
    ASPNETCORE_URLS: `http://127.0.0.1:${API_PORT}`,
    ANTHROPIC_API_KEY: 'stub-key-not-real',
    Anthropic__BaseUrl: `http://127.0.0.1:${STUB_PORT}`,
    ConnectionStrings__Default: `Data Source=${path.join(DATA, 'e2e.db')}`,
    Storage__Root: path.join(DATA, 'storage'),
  }, path.join(DATA, 'api.log'));

  await waitFor(`http://127.0.0.1:${STUB_PORT}/`);
  await waitFor(`http://127.0.0.1:${API_PORT}/api/metrics/pilot`);
  // A stub left over from an earlier run would answer on the same port with stale state: refuse to continue.
  const stubState = JSON.parse((await get(`http://127.0.0.1:${STUB_PORT}/`)).body);
  assert.deepStrictEqual(stubState, [], `port ${STUB_PORT} is served by a stale stub with ${stubState.length} recorded requests; kill it first`);

  const browser = await chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'he-IL',
  });
  const page = await context.newPage();
  const base = `http://127.0.0.1:${API_PORT}`;
  const consoleErrors = [];
  const consoleWarnings = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); if (m.type() === 'warning') consoleWarnings.push(m.text()); });
  page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + e.message));
  const failedUrls = [];
  let step = 'boot';
  const reqLog = [];
  page.on('request', (r) => { if (r.url().includes('/api/')) reqLog.push(`[${step}] ${r.method()} ${r.url().replace(base, '')}`); });
  page.on('requestfailed', (r) => failedUrls.push(`[${step}] ${r.method()} ${r.url()} -> ${r.failure() && r.failure().errorText}`));
  const noContent = new Set();
  page.on('response', (r) => {
    if (r.status() === 204) noContent.add(r.request().method() + ' ' + r.url());
    if (r.status() >= 400) failedUrls.push(`[${step}] ${r.request().method()} ${r.url()} -> ${r.status()}`);
  });
  page.on('dialog', (d) => d.accept());

  await page.goto(base + '/');
  await page.waitForSelector('#screen-onboarding:not([hidden])');

  step = '1';
  // 1. Auto-detect: he-IL browser -> Hebrew, RTL.
  assert.strictEqual(await page.getAttribute('html', 'lang'), 'he');
  assert.strictEqual(await page.getAttribute('html', 'dir'), 'rtl');
  assert.strictEqual(await page.textContent('#screen-onboarding h1'), 'הלוק הזה מתאים לאן שהוא הולך?');
  assert.strictEqual(await page.$eval('#lang', (s) => s.value), 'he');
  assert.strictEqual(await page.$eval('#lang option[value=he]', (o) => o.textContent), 'עברית');
  await page.screenshot({ path: path.join(SHOTS, '01-onboarding-he.png'), fullPage: true });

  step = '2';
  // 2. Manual switch to English: no reload, instant re-render, LTR.
  await page.evaluate(() => { window.__noReloadMarker = 'still-here'; });
  await page.selectOption('#lang', 'en');
  await page.waitForFunction(() => document.documentElement.lang === 'en');
  assert.strictEqual(await page.getAttribute('html', 'dir'), 'ltr');
  assert.strictEqual(await page.textContent('#screen-onboarding h1'), "Is this outfit right for where it's going?");
  assert.strictEqual(await page.evaluate(() => window.__noReloadMarker), 'still-here', 'language switch reloaded the page');
  assert.strictEqual(JSON.parse(await page.evaluate(() => localStorage.getItem('fitcheck.session'))).language, 'en');
  await page.screenshot({ path: path.join(SHOTS, '02-onboarding-en.png'), fullPage: true });

  step = '3';
  // 3. Onboarding validation and account creation.
  assert.strictEqual(await page.isDisabled('#start'), true);
  await page.fill('#handle', 'n');
  await page.check('#age');
  assert.strictEqual(await page.isDisabled('#start'), true, 'one-character handle must not enable start');
  await page.fill('#handle', 'noa');
  assert.strictEqual(await page.isDisabled('#start'), false);
  await page.click('#start');
  await page.waitForSelector('#screen-check:not([hidden])');
  const session = JSON.parse(await page.evaluate(() => localStorage.getItem('fitcheck.session')));
  assert.ok(session.userId, 'userId persisted');
  assert.strictEqual(session.handle, 'noa');
  assert.strictEqual(await page.textContent('#whoami'), 'noa');
  await page.screenshot({ path: path.join(SHOTS, '03-check-empty-en.png'), fullPage: true });

  step = '4';
  // 4. Intent + photo (a real JPEG drawn on a canvas, large enough to be downscaled).
  assert.strictEqual(await page.isDisabled('#submit'), true);
  await page.click('.chip[data-intent=Date]');
  assert.strictEqual(await page.getAttribute('.chip[data-intent=Date]', 'aria-pressed'), 'true');
  assert.strictEqual(await page.getAttribute('.chip[data-intent=Casual]', 'aria-pressed'), 'false');
  const bigJpeg = await page.evaluate(() => {
    const c = document.createElement('canvas'); c.width = 1800; c.height = 2400;
    const g = c.getContext('2d');
    g.fillStyle = '#e8e2d6'; g.fillRect(0, 0, c.width, c.height);
    for (let i = 0; i < 400; i++) { g.fillStyle = `hsl(${(i * 37) % 360} 40% ${30 + (i % 50)}%)`; g.fillRect((i * 97) % 1700, (i * 131) % 2300, 120, 160); }
    return c.toDataURL('image/jpeg', 0.92).split(',')[1];
  });
  const bigBuffer = Buffer.from(bigJpeg, 'base64');
  await page.setInputFiles('#file', { name: 'outfit.jpg', mimeType: 'image/jpeg', buffer: bigBuffer });
  await page.waitForSelector('#photo img');
  await page.waitForFunction(() => !document.getElementById('submit').disabled);
  await page.fill('#occasion', 'dinner with friends');
  await page.screenshot({ path: path.join(SHOTS, '04-check-ready-en.png'), fullPage: true });

  step = '5';
  // 5. Submit -> loading (stub answers 529 first, so the retry backoff keeps this visible) -> result.
  await page.click('#submit');
  await page.waitForSelector('#screen-loading:not([hidden])');
  assert.strictEqual(await page.textContent('#screen-loading p'), 'The stylist is looking.');
  await page.screenshot({ path: path.join(SHOTS, '05-loading-en.png') });
  await page.waitForSelector('#screen-result:not([hidden])', { timeout: 30000 });
  await page.waitForFunction(() => document.querySelector('.score') && document.querySelector('.score').textContent === '7', null, { timeout: 5000 });
  assert.strictEqual(await page.textContent('.headline'), 'Clean casual with one weak link');
  assert.strictEqual(await page.textContent('.vibe'), 'relaxed weekend');
  assert.strictEqual((await page.$$('.items li')).length, 3);
  assert.strictEqual((await page.$$('.working li')).length, 2);
  assert.strictEqual(await page.textContent('.tip p'), 'Swap the running shoes for plain white leather sneakers.');
  assert.strictEqual(await page.getAttribute('.bar', 'aria-valuenow'), '72');
  assert.ok(await page.$('button:has-text("Share")'));
  assert.strictEqual((await page.$$('#history li')).length, 1);
  await page.screenshot({ path: path.join(SHOTS, '06-result-en.png'), fullPage: true });

  step = '6';
  // 6. Switch to Hebrew on the result: chrome re-renders, the feedback keeps its language.
  await page.selectOption('#lang', 'he');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  assert.strictEqual(await page.textContent('.headline'), 'Clean casual with one weak link', 'feedback must not be re-displayed in another language');
  const h2s = await page.$$eval('#result h2', (n) => n.map((x) => x.textContent));
  assert.deepStrictEqual(h2s, ['מה יש בלוק', 'מה עובד', 'הטיפ האחד']);
  assert.strictEqual(await page.textContent('.chip[data-intent=Date]'), 'דייט');
  await page.screenshot({ path: path.join(SHOTS, '07-result-en-feedback-he-ui.png'), fullPage: true });

  step = '7';
  // 7. A Hebrew check end to end.
  await page.click('button:has-text("לבדוק לוק נוסף")');
  await page.waitForSelector('#screen-check:not([hidden])');
  assert.strictEqual(await page.getAttribute('.chip[data-intent=Date]', 'aria-pressed'), 'true', 'intent survives "check another"');
  assert.strictEqual(await page.isDisabled('#submit'), true, 'photo is cleared for the next check');
  await page.setInputFiles('#file', { name: 'outfit.jpg', mimeType: 'image/jpeg', buffer: bigBuffer });
  await page.waitForFunction(() => !document.getElementById('submit').disabled);
  await page.click('#submit');
  await page.waitForSelector('#screen-result:not([hidden])', { timeout: 30000 });
  await page.waitForFunction(() => document.querySelector('.score') && document.querySelector('.score').textContent === '6', null, { timeout: 5000 });
  assert.strictEqual(await page.textContent('.headline'), "קז'ואל נקי עם חוליה חלשה אחת");
  assert.strictEqual(await page.textContent('.tip p'), 'שווה להחליף את נעלי הריצה בסניקרס עור לבן פשוט.');
  assert.strictEqual((await page.$$('#history li')).length, 2);
  const historyText = await page.textContent('#history li');
  assert.ok(historyText.includes('דייט'), 'history uses localized intent label: ' + historyText);
  await page.screenshot({ path: path.join(SHOTS, '08-result-he.png'), fullPage: true });

  step = '8';
  // 8. Not an outfit: tiny image -> friendly state, no share button.
  await page.click('button:has-text("לבדוק לוק נוסף")');
  await page.waitForSelector('#screen-check:not([hidden])');
  const tinyJpeg = await page.evaluate(() => {
    const c = document.createElement('canvas'); c.width = 40; c.height = 50;
    const g = c.getContext('2d'); g.fillStyle = '#888'; g.fillRect(0, 0, 40, 50);
    return c.toDataURL('image/jpeg', 0.5).split(',')[1];
  });
  await page.setInputFiles('#file', { name: 'wall.jpg', mimeType: 'image/jpeg', buffer: Buffer.from(tinyJpeg, 'base64') });
  await page.waitForFunction(() => !document.getElementById('submit').disabled);
  await page.click('#submit');
  await page.waitForSelector('#screen-result:not([hidden])', { timeout: 30000 });
  await page.waitForSelector('.state h1');
  assert.strictEqual(await page.textContent('.state h1'), 'זה לא נראה כמו לוק');
  assert.ok((await page.textContent('.state p')).includes('wall'), 'model message shown for not_outfit');
  assert.strictEqual(await page.$('button:has-text("לשתף")'), null, 'no share button on not_outfit');
  assert.strictEqual(await page.isHidden('#history-block'), true);
  await page.screenshot({ path: path.join(SHOTS, '09-not-outfit-he.png'), fullPage: true });

  step = '9';
  // 9. Photos are on disk under the private root and unreachable by URL.
  const storage = path.join(DATA, 'storage');
  const files = [];
  for (const user of fs.readdirSync(storage)) for (const f of fs.readdirSync(path.join(storage, user))) files.push(user + '/' + f);
  assert.strictEqual(files.length, 2, 'two OK checks keep their photos, not_outfit does not: ' + files.join(','));
  for (const rel of files) {
    for (const url of [`${base}/${rel}`, `${base}/storage/${rel}`, `${base}/wwwroot/${rel}`]) {
      const r = await get(url);
      assert.strictEqual(r.status, 404, `photo reachable at ${url}`);
    }
  }
  const stubRequests = JSON.parse((await get(`http://127.0.0.1:${STUB_PORT}/`)).body);
  assert.ok(stubRequests.length >= 4, 'stub saw the requests (incl. the retried one)');
  for (const r of stubRequests) {
    assert.strictEqual(r.media_type, 'image/jpeg');
    assert.strictEqual(r.thinking && r.thinking.type, 'disabled');
    assert.strictEqual(r.max_tokens, 1200);
    assert.strictEqual(r.model, 'claude-sonnet-5');
  }
  assert.ok(stubRequests[1].image_len < bigBuffer.length, `downscaled: ${stubRequests[1].image_len} < ${bigBuffer.length}`);
  assert.ok(stubRequests[1].user_text.includes('dinner with friends'));
  assert.ok(stubRequests[2].language_line[0].includes('Hebrew (he)'));

  step = '10';
  // 10. Metrics reflect the two OK checks from one user (no second-check return yet within the same minute... it is within 7 days, so it counts).
  const metrics = JSON.parse((await get(`${base}/api/metrics/pilot`)).body);
  assert.strictEqual(metrics.totalChecks, 2);
  assert.strictEqual(metrics.usersWithAtLeastOneCheck, 1);
  assert.strictEqual(metrics.usersWithSecondCheckWithin7Days, 1);
  assert.strictEqual(metrics.returnRate, 1);
  assert.strictEqual(metrics.byLanguage.en, 1);
  assert.strictEqual(metrics.byLanguage.he, 1);

  step = '11';
  // 11. Delete everything.
  await page.click('button:has-text("לנסות תמונה אחרת")');
  await page.waitForSelector('#screen-check:not([hidden])');
  await page.click('#delete-account');
  await page.waitForSelector('#screen-onboarding:not([hidden])');
  assert.strictEqual(await page.evaluate(() => localStorage.getItem('fitcheck.session')), null);
  assert.strictEqual(fs.readdirSync(storage).length, 0, 'storage folder emptied');
  const after = JSON.parse((await get(`${base}/api/metrics/pilot`)).body);
  assert.strictEqual(after.totalChecks, 0);

  step = '12';
  // 12. Reload keeps the language choice and lands on onboarding again.
  await page.reload();
  await page.waitForSelector('#screen-onboarding:not([hidden])');
  assert.strictEqual(await page.getAttribute('html', 'lang'), 'he');

  const i18nWarnings = consoleWarnings.filter((w) => w.startsWith('i18n:'));
  assert.deepStrictEqual(i18nWarnings, [], 'missing i18n keys: ' + i18nWarnings.join(' | '));
  console.log('ALL FAILED:', failedUrls);
  console.log('API LOG:', reqLog);
  // Chromium reports a body-less 204 fetch as net::ERR_ABORTED after the response arrived; that is not a failure.
  const unexpectedUrls = failedUrls.filter((u) => !u.includes('fonts.googleapis.com') && !u.includes('fonts.gstatic.com')
    && !(u.includes('net::ERR_ABORTED') && [...noContent].some((k) => u.includes(k))));
  assert.deepStrictEqual(unexpectedUrls, [], 'unexpected failed requests: ' + unexpectedUrls.join(' | '));
  const pageErrors = consoleErrors.filter((e) => e.startsWith('pageerror:'));
  assert.deepStrictEqual(pageErrors, [], 'page errors: ' + pageErrors.join(' | '));
  console.log('failed requests (expected: fonts only in this sandbox):', failedUrls);
  console.log('api requests:', reqLog);

  console.log('E2E OK');
  console.log('stub requests:', JSON.stringify(stubRequests.map((r) => ({ model: r.model, max_tokens: r.max_tokens, thinking: r.thinking, media_type: r.media_type, image_len: r.image_len })), null, 0));
  console.log('console warnings (non-i18n):', consoleWarnings.filter((w) => !w.startsWith('i18n:')));
  await browser.close();
})().then(() => { for (const p of procs) p.kill(); process.exit(0); },
  (err) => { console.error('E2E FAILED:', err); for (const p of procs) p.kill(); process.exit(1); });
