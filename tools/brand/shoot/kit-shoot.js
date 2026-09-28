#!/usr/bin/env node
/*
 * The brand kit's ten app screens (tools/brand/templates/screens/*.png), shot from the REAL app. It builds nothing and
 * draws nothing over a capture: it starts the API (on 5188, with its own data folder) and the e2e stub stylist (on
 * 5199, through kit_stub.py, which answers each photo with a verdict written for that photo), then walks the app as
 * people do: a brand that is verified and features two looks, three people who check, post and fire the three looks
 * of templates/photos/, and a Hebrew visitor who takes the language offer, checks as a guest and signs up. Every file
 * is the phone viewport, 390x844 at 2x = 780x1688, the size render-kit.js lays into its frames. Chromium runs with a
 * fake camera playing a two-second still of the pink look (made here with ffmpeg), so the recording screen shows a
 * person, not the test pattern.
 *
 * From the repository root, after a build of the API (it runs with --no-build):
 *
 *   dotnet build src/FitCheck.Api
 *   node tools/brand/shoot/kit-shoot.js      # the ten screens, into tools/brand/templates/screens/
 *   node tools/brand/render-kit.js           # then the kit, the store set and the landing screens from them
 *
 * Playwright is the browser test's copy (tools/e2e/node_modules; PLAYWRIGHT_PATH overrides it), the browser is
 * CHROMIUM_PATH or Playwright's own, and python3, dotnet and ffmpeg come from PATH. The database, the uploads, the two
 * servers' logs, the camera clip and a few alt-*.png framings of the same moments (to choose from by eye) go to
 * KIT_SHOOT_SCRATCH (default <tmp>/orevosh-kit-shoot), never into the repository. The people, the counts and the
 * times on the screens are this run's; the looks are the owner's, used with permission.
 */
'use strict';
const { spawn, execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');

const ROOT = path.resolve(__dirname, '..', '..', '..');
/* playwright is the browser test's copy (tools/e2e), as render-episode.js finds it. */
function loadPlaywright() {
  const tries = [process.env.PLAYWRIGHT_PATH, path.join(ROOT, 'tools/e2e/node_modules/playwright'), 'playwright'].filter(Boolean);
  for (const p of tries) { try { return require(p); } catch (e) { /* next */ } }
  console.error('playwright not found; tried:\n  ' + tries.join('\n  ') + '\nRun npm install in tools/e2e (or tools/brand).');
  process.exit(1);
}
const { chromium } = loadPlaywright();

const SCRATCH = path.resolve(process.env.KIT_SHOOT_SCRATCH || path.join(os.tmpdir(), 'orevosh-kit-shoot'));
const SCREENS = path.join(ROOT, 'tools', 'brand', 'templates', 'screens');
const ALT = path.join(SCRATCH, 'alt');
const REPO = path.join(ROOT, 'src', 'FitCheck.Api');
const DATA = path.join(SCRATCH, 'data');
const DB = path.join(DATA, 'kit.db');
const API_PORT = 5188;
const STUB_PORT = 5199;
const base = `http://127.0.0.1:${API_PORT}`;
const PHOTOS = path.join(ROOT, 'tools', 'brand', 'templates', 'photos');
const CAMEL = fs.readFileSync(path.join(PHOTOS, 'look-2-camel.jpg'));
const STREET = fs.readFileSync(path.join(PHOTOS, 'look-1-streetwear.jpg'));
const PINK = fs.readFileSync(path.join(PHOTOS, 'look-3-pink.jpg'));
const CAM = path.join(SCRATCH, 'cam-pink.y4m');

/* The scratch folder is emptied on every run, so it must never be the repository or hold it. */
if (ROOT === SCRATCH || ROOT.startsWith(SCRATCH + path.sep)) {
  console.error('KIT_SHOOT_SCRATCH (' + SCRATCH + ') holds the repository; it is emptied on every run. Point it at a folder of its own.');
  process.exit(1);
}
fs.rmSync(SCRATCH, { recursive: true, force: true });
fs.mkdirSync(DATA, { recursive: true });
fs.mkdirSync(ALT, { recursive: true });
/* The fake camera's clip: two seconds of the pink look at 720x1280, 15 fps, as the y4m Chromium plays in a loop. */
execFileSync('ffmpeg', ['-v', 'error', '-y', '-loop', '1', '-i', path.join(PHOTOS, 'look-3-pink.jpg'), '-t', '2', '-r', '15',
  '-vf', 'scale=720:1280', '-pix_fmt', 'yuv420p', CAM]);

// ---- the boot and the page helpers, in the browser test's shape (tools/e2e/e2e.js) ----
function get(url) {
  return new Promise((resolve, reject) => {
    http.get(url, (res) => { const chunks = []; res.on('data', (c) => chunks.push(c)); res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) })); }).on('error', reject);
  });
}
async function waitFor(url, tries = 120) {
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
const API_ENV = {
  ASPNETCORE_URLS: base,
  ANTHROPIC_API_KEY: 'stub-key-not-real',
  Anthropic__BaseUrl: `http://127.0.0.1:${STUB_PORT}`,
  ConnectionStrings__Default: `Data Source=${DB}`,
  Storage__Root: path.join(DATA, 'storage'),
  Email__Host: 'log',
  Anthropic__PromptCache: '5m',
  Plans__FreeChecksPerDay: '40',
  Plans__TomorrowMorningPush: 'true',
  Plans__WardrobeNamesToStylist: '2',
  Weather__BaseUrl: `http://127.0.0.1:${STUB_PORT}`,
  Board__NewAccountDays: '0',
  Board__MinChecksToCount: '1',
  Board__CacheSeconds: '0',
  Board__Sponsor__Name: 'NEXOR',
  Board__Sponsor__Handle: 'nexor',
  Board__Sponsor__PrizeText: 'A jacket from the new drop',
  Board__Sponsor__Url: 'nexor.example',
  Email__From: 'OREVOSH <noreply@example.test>',
  Languages__Enabled__0: 'en',
  Languages__Enabled__1: 'he',
};
/** e2e.js's maintenance(): one of the app's owner commands against this run's database. */
function maintenance(...args) {
  return execFileSync('dotnet', ['run', '--no-build', '--project', REPO, '--', ...args], {
    env: { ...process.env, ConnectionStrings__Default: `Data Source=${DB}`, Storage__Root: path.join(DATA, 'storage'), ANTHROPIC_API_KEY: 'stub-key-not-real' }
  }).toString();
}

const settled = '#view h1, #view .card, #view .empty, #view .notice, #view .grid, #view .sheet, #view .person, #view form, #view .x-hero, #view .profile-head, #view .plan';
async function go(page, hash) {
  if (!page.url().startsWith(base)) await page.goto(base + '/' + hash);
  else if (await page.evaluate(() => location.hash) === hash) await page.evaluate(() => { window.dispatchEvent(new HashChangeEvent('hashchange')); });
  else await page.evaluate((h) => { location.hash = h; }, hash);
  await page.waitForFunction((h) => location.hash === h || (h === '#/' && location.hash === ''), hash);
  await page.waitForSelector(settled, { timeout: 15000 });
}
async function settle(page, ms = 1200) {
  await page.evaluate(async () => {
    await document.fonts.ready;
    const near = [...document.images].filter((i) => !i.complete && i.getBoundingClientRect().top < innerHeight * 3);
    await Promise.race([Promise.all(near.map((i) => new Promise((r) => { i.onload = r; i.onerror = r; }))), new Promise((r) => setTimeout(r, 6000))]);
  });
  await page.waitForTimeout(ms);
}
/** Where a capture goes: the ten the kit uses into templates/screens, an alt-* framing into the scratch folder. */
const target = (name) => path.join(name.startsWith('alt-') ? ALT : SCREENS, name + '.png');
/** The phone viewport only: 390x844 at 2x is the 780x1688 capture render-kit.js expects. */
async function shot(page, name, ms) {
  await settle(page, ms);
  await page.screenshot({ path: target(name) });
  console.log('shot', name);
}
async function choosePhoto(page, photo) {
  await page.click('#photo');
  await page.waitForSelector('#media-library');
  const [chooser] = await Promise.all([page.waitForEvent('filechooser'), page.click('#media-library')]);
  if (await page.$('.sheet')) await page.waitForFunction(() => !document.querySelector('.sheet'));
  await chooser.setFiles({ name: 'outfit.jpg', mimeType: 'image/jpeg', buffer: photo });
}
async function person(browser, locale) {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale, serviceWorkers: 'block' });
  await context.grantPermissions(['geolocation', 'camera', 'microphone'], { origin: base });
  await context.setGeolocation({ latitude: 32.0853, longitude: 34.7818 });
  const page = await context.newPage();
  page.on('console', (m) => { if (m.type() === 'error') console.log('console error:', m.text()); });
  page.on('pageerror', (e) => console.log('pageerror:', e.message));
  page.on('response', (r) => { if (r.status() >= 400) console.log('HTTP', r.status(), r.request().method(), r.url().replace(base, '')); });
  page.on('dialog', (d) => d.accept());
  return page;
}
async function takeOffer(page, code) {
  await page.goto(base + '/');
  await page.waitForSelector('#lang-offer');
  await page.click('#lang-offer-yes');
  await page.waitForSelector(`html[lang=${code}]`);
}
async function signup(page, handle, dob) {
  await go(page, '#/signup');
  await page.waitForSelector('#a-handle');
  await page.fill('#a-handle', handle);
  await page.fill('#a-password', 'password123');
  await page.fill('#a-dob', dob || '1990-01-01');
  await page.click('#a-submit');
  await page.waitForFunction(() => location.hash === '#/welcome');
  await page.waitForSelector(settled);
}

// ---- the kit's own steps ----
const H = { 'X-Requested-With': 'Orevosh' };
/** The stylist's next verdicts are about this photo (kit_stub.py). */
const look = (name) => get(`http://127.0.0.1:${STUB_PORT}/__look/${name}`);
/** A square crop of one of the photos (the wearer's face), as the JPEG an avatar upload takes. */
function crop(page, buf, x, y, size) {
  return page.evaluate(async ([b64, x, y, size]) => {
    const img = new Image();
    await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = 'data:image/jpeg;base64,' + b64; });
    const c = document.createElement('canvas'); c.width = 600; c.height = 600;
    c.getContext('2d').drawImage(img, x, y, size, size, 0, 0, 600, 600);
    return c.toDataURL('image/jpeg', 0.92).split(',')[1];
  }, [buf.toString('base64'), x, y, size]).then((b64) => Buffer.from(b64, 'base64'));
}
/** The brand's picture: its name in white on near-black, as a shop's logo tile would be. */
function brandLogo(page) {
  return page.evaluate(async () => {
    await document.fonts.ready;
    const c = document.createElement('canvas'); c.width = 600; c.height = 600;
    const g = c.getContext('2d');
    g.fillStyle = '#0f0d12'; g.fillRect(0, 0, 600, 600);
    g.fillStyle = '#ffffff'; g.textAlign = 'center'; g.textBaseline = 'middle';
    g.font = '800 124px Outfit, sans-serif';
    g.fillText('NEXOR', 300, 290);
    g.fillRect(210, 372, 180, 6);
    return c.toDataURL('image/jpeg', 0.92).split(',')[1];
  }).then((b64) => Buffer.from(b64, 'base64'));
}
async function setAvatar(page, jpeg) {
  await go(page, '#/settings');
  await page.waitForSelector('#s-save');
  const [chooser] = await Promise.all([page.waitForEvent('filechooser'), page.click('button:has-text("Change photo")')]);
  await chooser.setFiles({ name: 'me.jpg', mimeType: 'image/jpeg', buffer: jpeg });
  await page.waitForFunction(() => !!document.querySelector('#view .avatar img'));
}
/** A check through the screen: the occasion (and a style), a note, the photo; opts.before runs with the photo in and the button live. */
async function check(page, lookName, photo, opts) {
  await go(page, '#/check');
  await page.waitForSelector('#photo');
  await page.click(`#occasions .chip[data-occasion="${opts.occasion}"]`);
  if (opts.style) { await page.click(`#styles .chip[data-style="${opts.style}"]`); await page.waitForSelector(`#styles .chip[data-style="${opts.style}"][aria-pressed="true"]`); }
  if (opts.note) await page.fill('#occasion', opts.note);
  await choosePhoto(page, photo);
  await page.waitForSelector('#photo img');
  await page.waitForFunction(() => !document.getElementById('submit').disabled);
  if (opts.before) await opts.before();
  await look(lookName);
  await page.click('#submit');
  await page.waitForSelector('#result .score', { timeout: 60000 });
  await page.waitForTimeout(1800);   // the count-up and the ring's draw-on
}
async function post(page, caption) {
  await page.click('#post-open');
  await page.waitForSelector('#post-confirm');
  await page.fill('#caption', caption);
  await page.click('#post-confirm');
  await page.waitForSelector('#post-link');
  return (await page.getAttribute('#post-link', 'href')).replace('#/post/', '');
}
async function api(page, method, url) {
  const r = await page.request.fetch(base + url, { method, headers: H });
  if (!r.ok()) throw new Error(method + ' ' + url + ' ' + r.status() + ' ' + (await r.text()).slice(0, 200));
}

(async () => {
  start('python3', ['-B', path.join(__dirname, 'kit_stub.py'), String(STUB_PORT), base], {}, path.join(DATA, 'stub.log'));
  start('dotnet', ['run', '--no-build', '--project', REPO], API_ENV, path.join(DATA, 'api.log'));
  await waitFor(`http://127.0.0.1:${STUB_PORT}/`);
  await waitFor(`${base}/api/metrics/pilot`);
  let exe = process.env.CHROMIUM_PATH;
  if (exe && fs.existsSync(exe) && fs.statSync(exe).isDirectory()) exe = path.join(exe, 'chrome');
  const browser = await chromium.launch({ ...(exe ? { executablePath: exe } : {}),
    args: ['--no-sandbox', '--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--use-file-for-fake-video-capture=' + CAM] });

  // ---- the brand: signs up, is verified by the owner (e2e step 3), turns Brand in settings, gets its picture ----
  const brand = await person(browser, 'en-US');
  await brand.goto(base + '/');
  await brand.waitForSelector(settled);
  await signup(brand, 'nexor');
  console.log(maintenance('--verify', 'nexor').trim().split('\n').pop());
  await go(brand, '#/settings');
  await brand.waitForSelector('#s-save');
  await brand.fill('#s-name', 'NEXOR');
  await brand.fill('#s-web', 'https://nexor.example');
  await brand.check('#s-brand');
  await brand.click('#s-save');
  await brand.waitForFunction(() => document.getElementById('s-save') && !document.getElementById('s-save').disabled);
  const brandMe = await (await brand.request.get(base + '/api/auth/me')).json();
  console.log('brand account:', brandMe.accountType, brandMe.name, 'verified', brandMe.verified);
  await setAvatar(brand, await brandLogo(brand));

  // ---- three people, each with the face of their look as the picture ----
  const noa = await person(browser, 'en-US');
  await noa.goto(base + '/');
  await signup(noa, 'noa', '1994-03-03');
  await setAvatar(noa, await crop(noa, CAMEL, 350, 30, 250));
  const idan = await person(browser, 'en-US');
  await idan.goto(base + '/');
  await signup(idan, 'idan', '1998-05-05');
  await setAvatar(idan, await crop(idan, STREET, 400, 85, 240));
  const kofi = await person(browser, 'en-US');
  await kofi.goto(base + '/');
  await signup(kofi, 'kofi', '1970-07-07');
  await setAvatar(kofi, await crop(kofi, PINK, 215, 95, 240));

  // ---- Noa checks the camel look for a coffee date: the check ready to send, then the result; she posts it ----
  await check(noa, 'camel', CAMEL, { occasion: 'Date', note: 'coffee and a walk', before: async () => {
    await shot(noa, '07-check-ready-en');
    await noa.evaluate(() => document.getElementById('photo').scrollIntoView({ block: 'end' }));
    await shot(noa, 'alt-07-check-ready-en-photo');
    await noa.evaluate(() => scrollTo(0, 0));
  } });
  await shot(noa, 'alt-08-result-en-top');
  // the store caption for this screen is "Fit, color, accessories" (both languages): the result scrolled until the three
  // rings and their labels clear the dock's raised check mark, with the photo's foot, the pieces, the ring and the headline above
  await noa.evaluate(() => { const r = document.querySelector('#breakdown').getBoundingClientRect(); scrollBy(0, r.bottom - 724); });
  await shot(noa, '08-result-en');
  await noa.evaluate(() => scrollTo(0, 0));
  const camel = await post(noa, 'Coffee date fit, thoughts? #datenight @nexor');
  // ---- Idan: the grey look, everyday in streetwear. Kofi: the pink look, for a party ----
  await check(idan, 'street', STREET, { occasion: 'Everyday', style: 'Streetwear' });
  const street = await post(idan, 'Grey on grey, thoughts? #streetwear @nexor');
  await check(kofi, 'pink', PINK, { occasion: 'Party' });
  const pink = await post(kofi, 'Head to toe pink, thoughts? #party @nexor');
  console.log('posts', JSON.stringify({ camel, street, pink }));

  // ---- fire: the camel look is the week's top look (Explore's hero is the most fired), the others get some too ----
  for (const p of [idan, kofi, brand]) await api(p, 'POST', `/api/posts/${camel}/fire`);
  for (const p of [noa, kofi]) await api(p, 'POST', `/api/posts/${street}/fire`);
  for (const p of [noa]) await api(p, 'POST', `/api/posts/${pink}/fire`);

  // ---- the brand features the pink look on its page (e2e step 5, through the menu), and the camel one too ----
  await go(brand, '#/post/' + pink);
  await brand.waitForSelector('.menu-open');
  await brand.click('.menu-open');
  await brand.waitForSelector('.sheet');
  await brand.click('.sheet button:has-text("Feature this look")');
  await brand.waitForSelector('.card .featured');
  await brand.waitForFunction(() => !document.querySelector('.sheet'));
  await shot(brand, 'alt-11-featured-en-toast', 400);
  await brand.waitForFunction(() => !document.querySelector('#toast'), null, { timeout: 15000 }).catch(() => {});
  await shot(brand, 'alt-11-featured-en-top');
  // the pill sits under the comment composer at the top of the page: bring it up to just above the composer, so the
  // photo's foot, the ring, the headline, the caption and FEATURED BY NEXOR are one frame and the share row (with this
  // machine's address in it) stays under the dock
  await brand.evaluate(() => { const r = document.querySelector('.card .featured').getBoundingClientRect(); scrollBy(0, r.top + r.height / 2 - 660); });
  await shot(brand, '11-featured-en');
  await api(brand, 'POST', `/api/posts/${camel}/feature`);

  // ---- a look's page (the grey one, as Noa sees it), and the brand's page of the looks it featured ----
  await go(noa, '#/post/' + street);
  await noa.waitForSelector('#view .card');
  await shot(noa, '10-post-en');
  await go(noa, '#/u/nexor/featured');
  await noa.waitForSelector('.grid a');
  await shot(noa, '12-brand-community-en');
  await go(brand, '#/u/nexor/featured');
  await brand.waitForSelector('.grid a');
  await shot(brand, 'alt-12-brand-community-en-own');
  await go(noa, '#/u/nexor/community');
  await noa.waitForSelector('.grid a');
  await shot(noa, 'alt-12-brand-community-en-community');

  // ---- Dan, a Hebrew browser: takes the language, checks the pink look as a guest, browses Explore and a tag ----
  const dan = await person(browser, 'he-IL');
  await takeOffer(dan, 'he');
  await go(dan, '#/check');
  await dan.waitForSelector('#guest-banner');
  await check(dan, 'pink', PINK, { occasion: 'Party' });
  await dan.waitForSelector('#guest-keep');
  await shot(dan, 'alt-00-guest-result-he-top');
  await dan.evaluate(() => { const r = document.querySelector('#breakdown').getBoundingClientRect(); scrollBy(0, r.bottom - 724); });
  await shot(dan, '00-guest-result-he');
  await dan.evaluate(() => scrollTo(0, 0));
  await go(dan, '#/explore');
  await dan.waitForSelector('a[href="#/tag/datenight"]');
  await dan.waitForSelector('.brand-card');
  await dan.waitForSelector('a.x-hero');
  await shot(dan, '13-explore-he');
  await go(dan, '#/tag/datenight');
  await dan.waitForSelector('#view .card');
  await shot(dan, '14-tag-he');
  // ---- then signs up, follows the brand from the welcome screen and lands on Home ----
  await signup(dan, 'dan', '1992-02-02');
  await dan.waitForSelector('.person');
  await dan.click('.person .btn');
  await dan.waitForFunction(() => document.querySelector('.person .btn').getAttribute('aria-pressed') === 'true');
  await dan.click('button:has-text("קחו אותי פנימה")');
  await dan.waitForFunction(() => location.hash === '#/' || location.hash === '');
  await dan.waitForSelector('#view .card');
  await dan.waitForFunction(() => !document.querySelector('#toast'), null, { timeout: 15000 }).catch(() => {});
  // the Today strip is kept per language and account, so the one on Home is asked for in Hebrew, as dan
  await dan.waitForSelector('#today-strip .today-title');
  await shot(dan, 'alt-16-home-he-all');
  console.log('home-he cards:', JSON.stringify(await dan.$$eval('#view .card .headline', (n) => n.map((x) => x.textContent))));
  const danMe = await (await dan.request.get(base + '/api/auth/me')).json();
  console.log('dan preferredLanguage:', danMe.preferredLanguage || danMe.language, '| today title:', await dan.$eval('#today-strip .today-title', (n) => n.textContent).catch(() => '(none)'));
  // the feed's own occasion filter: Streetwear pressed, the grey look first (Home's "All" leads with the camel one, the most fired)
  await dan.click('.chip[data-intent="Streetwear"]');
  await dan.waitForSelector('.chip[data-intent="Streetwear"][aria-pressed="true"]');
  await dan.waitForFunction(() => { const h = document.querySelector('#view .card .headline'); return h && /grey/.test(h.textContent); }, null, { timeout: 15000 });
  await shot(dan, '16-home-he');

  // ---- the in-app camera, clip mode, recording (e2e step 10; the fake device plays the pink look) ----
  await go(noa, '#/check');
  await noa.waitForSelector('#photo');
  await noa.click('#photo');
  await noa.waitForSelector('#media-camera');
  await noa.click('#media-camera');
  await noa.waitForSelector('.cam[data-phase="live"]', { timeout: 20000 });
  await noa.waitForTimeout(800);
  await noa.screenshot({ path: target('alt-20-camera-live-en') });
  await noa.click('.cam-modes button:nth-child(2)');
  await noa.click('.cam-shutter');
  await noa.waitForSelector('.cam[data-phase="recording"]');
  await noa.waitForTimeout(2400);
  // the red dot blinks (1s, steps(2, start): .675 then .35): take the frame on the brighter step
  await noa.waitForFunction(() => Number(getComputedStyle(document.querySelector('.cam-rec i')).opacity) > 0.5, null, { polling: 20 });
  await noa.screenshot({ path: target('20-recording-en') });
  console.log('shot 20-recording-en');
  await noa.click('.cam-shutter');
  await noa.waitForSelector('.cam[data-phase="preview"]', { timeout: 20000 });

  await browser.close();
  for (const p of procs) p.kill('SIGTERM');
  console.log('done: the ten screens are in ' + path.relative(ROOT, SCREENS) + ', the alt framings and the logs in ' + SCRATCH);
})().catch((e) => { console.error(e); for (const p of procs) p.kill('SIGTERM'); process.exit(1); });
