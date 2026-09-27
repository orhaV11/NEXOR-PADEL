/*
 * The before/after converter, against fixtures in the app's three shapes (a CheckDto, an export row and a pair
 * side, copied from the browser test's stub verdict and TriedTests' pair), and the template's end card.
 *
 *   node --test tools/brand/test/*.test.js
 *
 * No browser and no render here: the layout is asserted by CI's cover render of 006-before-after-sample.json,
 * which measures every line at 1080x1920 and exits non-zero on an overflow.
 */
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const ba = require('../lib/before-after.js');

const FIX = path.join(__dirname, 'fixtures');
const read = name => JSON.parse(fs.readFileSync(path.join(FIX, name), 'utf8'));
const REPO = path.resolve(__dirname, '../../..');
const clone = o => JSON.parse(JSON.stringify(o));

const BEFORE_ID = '11111111-1111-4111-8111-111111111111';
const AFTER_ID = '22222222-2222-4222-8222-222222222222';
const TIP = 'Swap the running shoes for plain white leather sneakers.';

/* The same assertion for the three shapes: fails with a message that names what the brief says it names. */
function fails(fn, ...needles) {
  let caught = null;
  try { fn(); } catch (e) { caught = e; }
  assert.ok(caught, 'expected a failure');
  assert.ok(caught instanceof ba.BeforeAfterError, 'a BeforeAfterError, got ' + caught);
  for (const n of needles) assert.ok(caught.message.includes(n), 'message should name ' + JSON.stringify(n) + ': ' + caught.message);
  return caught;
}

test('readSide normalises a CheckDto, an export row and a pair side to one shape', () => {
  const fromCheck = ba.readSide(read('check-before.json'));
  assert.equal(fromCheck.kind, 'side');
  assert.equal(fromCheck.id, BEFORE_ID);
  assert.equal(fromCheck.status, 'ok');
  assert.equal(fromCheck.intent, 'Office');
  assert.equal(fromCheck.occasion, 'after work drinks');
  assert.equal(fromCheck.score, 7);
  assert.equal(fromCheck.headline, 'Clean casual with one weak link');
  assert.equal(fromCheck.tip, TIP);
  assert.deepEqual(fromCheck.breakdown, { fit: 7, color: 8, accessories: 4 });
  assert.deepEqual(fromCheck.items, [
    { name: 'White tee', category: 'top' }, { name: 'Dark jeans', category: 'bottom' }, { name: 'Running shoes', category: 'shoes' }]);

  const row = read('export.json').checks.find(c => c.id === BEFORE_ID);
  const fromExport = ba.readSide(row);
  /* the export row carries the same facts, so the two readings agree field for field */
  assert.deepEqual(fromExport, fromCheck);

  const fromPair = ba.readSide(read('pair.json').before);
  assert.equal(fromPair.id, BEFORE_ID);
  assert.equal(fromPair.score, 7);
  assert.equal(fromPair.tip, TIP);
  assert.equal(fromPair.headline, 'Clean casual with one weak link');
  assert.equal(fromPair.breakdown, null, 'a pair side carries no breakdown');
  assert.deepEqual(fromPair.items, [], 'nor items');

  /* a side that has been read once reads again unchanged (it survives a JSON round trip) */
  assert.deepEqual(ba.readSide(clone(fromCheck)), fromCheck);
  fails(() => ba.readSide({ hello: 'world' }, 'x.json'), 'x.json', 'not a check the app wrote');
});

test('fromApp on the fixtures: 7 to 8, the tried tip, the delta and the three cells', () => {
  const ep = ba.fromApp({ before: read('check-before.json'), after: read('check-after.json'), pair: read('pair.json'), lang: 'en' });
  assert.equal(ep.variant, 'before-after');
  assert.equal(ep.before.score, 7);
  assert.equal(ep.after.score, 8);
  assert.equal(ep.delta, 1);
  assert.equal(ep.tip, TIP, 'the tip is the before check\'s one tip: the tip that was tried');
  assert.equal(ep.before.intent, 'OFFICE', 'the pill says the app\'s word, in caps');
  assert.equal(ep.after.intent, 'OFFICE');
  assert.equal(ep.before.occasion, 'after work drinks');
  assert.deepEqual([ep.before.breakdown.fit, ep.after.breakdown.fit], [7, 7]);
  assert.deepEqual([ep.before.breakdown.color, ep.after.breakdown.color], [8, 8]);
  assert.deepEqual([ep.before.breakdown.accessories, ep.after.breakdown.accessories], [4, 6]);
  assert.deepEqual(ep.changes, [{ category: 'shoes', from: 'Running shoes', to: 'White leather sneakers' }]);
  assert.deepEqual(ep.changeLines, ['shoes: Running shoes → White leather sneakers'], 'the app\'s own wording (tried.change_swap)');
});

test('fromApp with two pair sides takes what changed from the pair, since the sides carry no items', () => {
  const pair = read('pair.json');
  const before = ba.readSide(pair.before), after = ba.readSide(pair.after);
  before.breakdown = { fit: 7, color: 8, accessories: 4 };
  after.breakdown = { fit: 7, color: 8, accessories: 6 };
  const ep = ba.fromApp({ before, after, pair, lang: 'en' });
  assert.deepEqual(ep.changes, pair.changed);
});

test('changes() mirrors Taste.Changes: order, joins, case, null sides, and empty when identical', () => {
  const before = [
    { name: 'Running shoes', category: 'shoes' }, { name: 'White tee', category: 'top' },
    { name: 'Dark jeans', category: 'bottom' }, { name: 'Gold hoops', category: 'accessory' }];
  const after = [
    { name: 'white TEE', category: 'top' }, { name: 'Dark jeans', category: 'bottom' },
    { name: 'White leather sneakers', category: 'shoes' }, { name: 'Camel coat', category: 'outerwear' }];
  assert.deepEqual(ba.changes(before, after), [
    { category: 'outerwear', from: null, to: 'Camel coat' },
    { category: 'shoes', from: 'Running shoes', to: 'White leather sneakers' },
    { category: 'accessory', from: 'Gold hoops', to: null }
  ], 'the server\'s category order (top, bottom, dress, outerwear, shoes, accessory, other); case does not count');
  assert.deepEqual(ba.changes(before, clone(before)), [], 'the same pieces: nothing changed');

  /* two pieces of one category are joined with ", " in the order they were named, never twice */
  assert.deepEqual(ba.changes(
    [{ name: 'Belt', category: 'accessory' }, { name: 'belt', category: 'accessory' }, { name: 'Watch', category: 'accessory' }],
    []), [{ category: 'accessory', from: 'Belt, Watch', to: null }]);

  /* Taste.Clean and Taste.Keep: quotes, control characters, the 40-character cut, and rule 1's words */
  assert.equal(ba.clean('  a "quoted"\tname  ', 40), "a 'quoted' name");
  assert.equal(ba.clean('x'.repeat(50), 40), 'x'.repeat(40));
  assert.equal(ba.keep('Running shoes'), true);
  assert.equal(ba.keep('a skinny fit'), false, 'a body word is dropped, as the server drops it');
  assert.equal(ba.keep('חולצה יפה'), false);
  assert.equal(ba.keep(''), false);
  assert.deepEqual(ba.changes([{ name: 'slim jeans for a tall guy', category: 'bottom' }], []), [], 'a dropped piece leaves the category absent on both sides');
});

test('fromExport finds both rows by id and fails naming a missing id', () => {
  const ep = ba.fromExport(read('export.json'), BEFORE_ID, AFTER_ID, 'en');
  assert.equal(ep.before.score, 7);
  assert.equal(ep.after.score, 8);
  assert.equal(ep.tip, TIP);
  assert.deepEqual(ep.changes, [{ category: 'shoes', from: 'Running shoes', to: 'White leather sneakers' }]);
  /* the ids may be typed in either case */
  assert.equal(ba.fromExport(read('export.json'), BEFORE_ID.toUpperCase(), AFTER_ID, 'en').delta, 1);
  fails(() => ba.fromExport(read('export.json'), BEFORE_ID, '99999999-9999-4999-8999-999999999999', 'en', 'looks.json'),
    'looks.json', '99999999-9999-4999-8999-999999999999', '--list-checks');
  fails(() => ba.fromExport({ posts: [] }, BEFORE_ID, AFTER_ID, 'en'), 'no "checks" list');
});

test('the guards: same id, after older than before, no verdict, no breakdown, a pair of other checks', () => {
  const before = read('check-before.json'), after = read('check-after.json');
  fails(() => ba.fromApp({ before, after: before, lang: 'en', where: 'e.json' }), 'e.json', 'same check', BEFORE_ID);
  fails(() => ba.fromApp({ before: after, after: before, lang: 'en', where: 'e.json' }), 'e.json', 'not newer', BEFORE_ID, AFTER_ID);

  const rejected = clone(after); rejected.status = 'not_outfit'; rejected.feedback.status = 'not_outfit';
  fails(() => ba.fromApp({ before, after: rejected, lang: 'en', where: 'e.json' }), 'e.json', 'has no verdict', AFTER_ID);

  const old = read('export.json').checks.find(c => c.id.startsWith('4444'));
  const newer = clone(after); newer.createdAt = '2026-09-22T10:00:00Z';
  fails(() => ba.fromApp({ before: old, after: newer, lang: 'en', where: 'e.json' }), 'e.json', 'before the breakdown existed', old.id, '"breakdown"');

  const other = read('pair.json'); other.after.id = '99999999-9999-4999-8999-999999999999';
  fails(() => ba.fromApp({ before, after, pair: other, lang: 'en', where: 'e.json' }), 'e.json', 'pair', '99999999-9999-4999-8999-999999999999', AFTER_ID);

  const badScore = clone(after); badScore.feedback.score = 11;
  fails(() => ba.fromApp({ before, after: badScore, lang: 'en', where: 'e.json' }), '"score"', '0 to 10');
  const noTip = clone(before); delete noTip.feedback.oneTip;
  fails(() => ba.fromApp({ before: noTip, after, lang: 'en', where: 'e.json' }), 'no tip');
});

test('intentWord reads the app\'s own word: OldMoney in English, Formal in Hebrew', () => {
  assert.equal(ba.intentWord('OldMoney', 'en'), 'OLD MONEY');
  const he = JSON.parse(fs.readFileSync(path.join(REPO, 'src/FitCheck.Api/wwwroot/i18n/he.json'), 'utf8'));
  assert.equal(ba.intentWord('Formal', 'he'), he['intent.Formal']);
  assert.equal(ba.intentWord('DATE', 'en'), 'DATE', 'a word already typed as it should appear passes through');
  assert.equal(ba.intentWord('', 'en'), '');
});

test('inline fields override a check file, and a side can come from the export by id', () => {
  const readJson = name => read(name);
  const spec = { photo: 'x.jpg', check: 'check-before.json', headline: 'Shorter line', score: 6 };
  const s = ba.sideFromSpec(spec, { where: 'e.json > before', readJson });
  assert.equal(s.headline, 'Shorter line');
  assert.equal(s.score, 6);
  assert.equal(s.tip, TIP, 'what was not overridden comes from the file');
  assert.equal(s.id, BEFORE_ID);

  const byId = ba.sideFromSpec({ photo: 'x.jpg', id: AFTER_ID }, { where: 'e.json > after', exportJson: read('export.json') });
  assert.equal(byId.score, 8);
  assert.equal(byId.headline, 'Same look, the shoes fixed');

  const inline = ba.sideFromSpec({ photo: 'x.jpg', intent: 'Date', score: 9, headline: 'Camel', breakdown: { fit: 9, color: 9, accessories: 8 } }, { where: 'e.json > before' });
  assert.equal(inline.status, 'ok');
  assert.deepEqual(inline.breakdown, { fit: 9, color: 9, accessories: 8 });

  fails(() => ba.sideFromSpec('nope', { where: 'e.json > before' }), 'e.json > before', 'must be an object');
  fails(() => ba.sideFromSpec({ id: '9999', photo: 'x.jpg' }, { where: 'e.json > before', exportJson: read('export.json') }), '9999', '--list-checks');
});

test('listChecks prints the scored checks newest first, with their ids', () => {
  const rows = ba.listChecks(read('export.json'));
  assert.deepEqual(rows.map(r => r.id), [AFTER_ID, BEFORE_ID, '44444444-4444-4444-8444-444444444444'], 'the refused check is left out; newest first');
  assert.deepEqual(rows.map(r => r.index), [1, 2, 3]);
  assert.equal(rows[2].breakdown, false, 'the old check is marked as having no breakdown');
  const text = ba.listChecksText(read('export.json'));
  const lines = text.split('\n');
  assert.ok(lines[0].startsWith('3 scored checks, newest first'), lines[0]);
  assert.ok(lines[1].includes('2026-09-20 18:30') && lines[1].includes('Office') && lines[1].includes(' 8/10') && lines[1].includes('Same look, the shoes fixed'), lines[1]);
  assert.ok(lines[2].trim() === AFTER_ID, 'the id on its own line, to be copied');
  assert.ok(text.includes('(no breakdown)'));
  assert.equal(ba.listChecksText({ checks: [] }), 'no scored checks in this export.');
});

test('the template\'s end card carries the address and COMING SOON cannot come back', () => {
  const html = fs.readFileSync(path.join(REPO, 'tools/brand/templates/episode.html'), 'utf8');
  assert.ok(!/COMING SOON/.test(html), 'the dead kicker is gone from episode.html');
  assert.ok(!/soon:\s*"בקרוב"/.test(html), 'and its Hebrew twin');
  const soon = html.match(/soon:\s*"([^"]*)"/g) || [];
  assert.deepEqual(soon, ['soon: "orevosh.com"', 'soon: "orevosh.com"'], 'COPY.soon is the bare address in both languages');
  assert.ok(html.includes("'before-after'") || html.includes('"before-after"'), 'the template knows the variant');
});

test('the shipped sample is a before-after episode with the same photograph on both sides', () => {
  const sample = JSON.parse(fs.readFileSync(path.join(REPO, 'brand-kit/episodes/006-before-after-sample.json'), 'utf8'));
  assert.equal(sample.variant, 'before-after');
  assert.equal(sample.before.photo, sample.after.photo, 'a layout sample, never a real pair: one photograph twice');
  const ep = ba.fromApp({ before: sample.before, after: sample.after, lang: sample.lang, tip: sample.tip, where: '006' });
  assert.equal(ep.delta, 1);
  assert.equal(ep.before.intent, 'DATE');
});
