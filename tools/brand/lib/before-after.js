/*
 * OREVOSH — the before/after episode's converter.
 *
 * The `before-after` variant of tools/brand/render-episode.js is fed by the app's own JSON and never by
 * retyped numbers: two checks, and optionally the pair the app wrote when the second one was linked to
 * the first. The app hands those out in three shapes, none of them a photo:
 *
 *   CheckDto         GET /api/checks/{id}            the verdict under "feedback" (oneTip, breakdown, items)
 *   ExportCheckDto   GET /api/users/me/export        one row of "checks[]" (tip, breakdown, items at the top)
 *   TriedSideDto     POST /api/checks/{id}/tried and GET /api/users/me/tried: before/after of a pair
 *                                                    (no breakdown and no items — enough to check the ids)
 *
 * This module reads all three into one shape, computes "what changed" exactly as the server does
 * (Services/Taste.cs, Changes), looks the intent's word up in the app's own i18n so the pill says what
 * the app says, and refuses the pairs the app itself refuses (the same check twice, an after older than
 * its before, a side with no verdict). It is pure: no browser, no rendering, nothing written. The
 * renderer requires it, the node --test file in tools/brand/test exercises it, and the browser test
 * (tools/e2e/e2e.js) feeds it the three shapes fetched from the real API, which is what keeps the
 * contract honest when a DTO moves.
 *
 * Every failure is a BeforeAfterError whose message names the file and the id, in the renderer's own
 * style: the person editing an episode edits JSON, never code.
 */
'use strict';

const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../../..');
const I18N_DIR = path.join(REPO, 'src/FitCheck.Api/wwwroot/i18n');

/* The categories the stylist may name, in the order the server walks them (Taste.OutfitCategories). */
const CATEGORIES = ['top', 'bottom', 'dress', 'outerwear', 'shoes', 'accessory', 'other'];
/* Taste.PieceMaxLength: a piece's name is cut here before it is compared or shown. */
const PIECE_MAX = 40;

class BeforeAfterError extends Error {}
function fail(msg) { throw new BeforeAfterError(msg); }

/* ------------------------------------------------------------------ one shape for a side */
/* Rule 1's word list, as OutfitAnalyzer.PersonWords has it. A piece whose name is about a body rather than a
   garment is dropped from "what changed" on the server (Taste.Keep), so it is dropped here too. The .NET \b is
   Unicode-aware; the JavaScript one is ASCII-only, so it is spelled out as letter/digit lookarounds. */
const NB = '(?<![\\p{L}\\p{N}_])', NE = '(?![\\p{L}\\p{N}_])';
const PERSON_WORDS = new RegExp(
  NB + '(bod(y|ies)|face|faces|facial|skin|hair|weight|fat|thin|slim|skinny|chubby|overweight|curvy|height|tall|' +
  'age|aged|old|young|child|children|kid|kids|teen|teenager|minor|boy|boys|girl|girls|man|men|woman|women|male|female|gender|' +
  'lady|guy|pretty|beautiful|handsome|ugly|attractive|sexy|cute|chest|breast|breasts|legs|hips|waist|belly|stomach|thighs|butt)' + NE +
  '|גוף|פנים|פרצוף|עור|שיער|משקל|שמן|שמנה|רזה|רזים|גיל|זקן|זקנה|צעיר|צעירה|ילד|ילדה|ילדים|נער|נערה|גבר|גברים|אישה|אשה|נשים|בחור|בחורה|יפה|יפים|מכוער|סקסי|חזה|רגליים|ירכיים|בטן|מותן' +
  '|جسم|جسد|وجه|بشرة|شعر|وزن|سمين|سمينة|نحيف|نحيفة|عمر|كبير|كبيرة|صغير|صغيرة|طفل|طفلة|أطفال|مراهق|فتاة|فتى|صبي|رجل|امرأة|سيدة|شاب|شابة|جميل|جميلة|قبيح|صدر|ساق|أرجل|خصر|بطن' +
  '|тел[оаеу]|лиц[оаеу]|кож[аеиу]|волос|вес[аеу]?' + NE + '|толст|худ[аеоы]|стройн|возраст|стар[аыо]|молод|ребен|ребён|дет[иейям]|подрост|мальчик|девочк|девушк|парен|мужчин|женщин|красив|некрасив|уродлив|сексуальн|груд[ьи]|ног[иа]|бедр|тали[яи]|живот',
  'iu');

/* Taste.Clean: control characters become spaces, a double quote a single one, runs of spaces one space, and
   the result is cut at `max` and trimmed. */
function clean(text, max) {
  if (text === undefined || text === null || String(text).trim() === '') return '';
  const folded = Array.from(String(text), c => (/[\p{Cc}]/u.test(c) ? ' ' : c === '"' ? "'" : c)).join('');
  const collapsed = folded.split(' ').filter(Boolean).join(' ');
  return collapsed.length <= max ? collapsed : collapsed.slice(0, max).trimEnd();
}
/* Taste.Keep: something is left, and it is a garment and not a person. */
function keep(text) { return text.length > 0 && !PERSON_WORDS.test(text); }

function breakdownOf(b) {
  if (!b || typeof b !== 'object') return null;
  if ([b.fit, b.color, b.accessories].some(v => v === undefined || v === null)) return null;
  return { fit: b.fit, color: b.color, accessories: b.accessories };
}
function itemsOf(items) {
  if (!Array.isArray(items)) return [];
  return items.filter(i => i && typeof i === 'object').map(i => ({ name: i.name === undefined || i.name === null ? '' : String(i.name), category: i.category ? String(i.category) : 'other' }));
}
function blank(s) { return s === undefined || s === null || String(s).trim() === '' ? null : String(s); }

/* The normalised side. `kind: "side"` marks it so a side that has been through here once is recognised
   again (it survives a JSON round trip, which an object identity would not). */
function side(fields) {
  return Object.assign({ kind: 'side', id: null, createdAt: null, status: 'ok', intent: null, occasion: null,
    score: null, headline: null, tip: null, breakdown: null, items: [] }, fields);
}

/**
 * One of the app's three shapes (or a side already read) to the one shape the renderer uses:
 * { kind: "side", id, createdAt, status, intent, occasion, score, headline, tip, breakdown, items[{name, category}] }.
 * The occasion is the wearer's free line (CheckDto.note / ExportCheckDto.note): the line the verdict prints as
 * FOR …. A TriedSideDto carries no breakdown and no items; that is reported, not invented.
 */
function readSide(obj, where) {
  where = where || 'the check';
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) {
    fail(where + ': not a check the app wrote (expected an object).');
  }
  if (obj.kind === 'side') return side(Object.assign({}, obj, { breakdown: breakdownOf(obj.breakdown), items: itemsOf(obj.items) }));

  if (obj.feedback && typeof obj.feedback === 'object') {
    /* CheckDto: GET /api/checks/{id} */
    const fb = obj.feedback;
    return side({
      id: blank(obj.id), createdAt: blank(obj.createdAt), status: blank(obj.status) || blank(fb.status) || 'ok',
      intent: blank(obj.intent), occasion: blank(obj.note),
      score: fb.score !== undefined && fb.score !== null ? fb.score : (obj.score === undefined ? null : obj.score),
      headline: blank(fb.headline), tip: blank(fb.oneTip), breakdown: breakdownOf(fb.breakdown), items: itemsOf(fb.items)
    });
  }
  if ('oneTip' in obj && !('items' in obj)) {
    /* TriedSideDto: one side of POST /api/checks/{id}/tried or GET /api/users/me/tried */
    return side({
      id: blank(obj.id), createdAt: blank(obj.createdAt), status: 'ok', intent: blank(obj.intent),
      score: obj.score === undefined ? null : obj.score, headline: blank(obj.headline), tip: blank(obj.oneTip),
      breakdown: null, items: []
    });
  }
  if ('items' in obj || 'tip' in obj || 'breakdown' in obj || ('status' in obj && 'score' in obj)) {
    /* ExportCheckDto: a row of GET /api/users/me/export → checks[] */
    return side({
      id: blank(obj.id), createdAt: blank(obj.createdAt), status: blank(obj.status) || 'ok', intent: blank(obj.intent),
      occasion: blank(obj.note), score: obj.score === undefined ? null : obj.score, headline: blank(obj.headline),
      tip: blank(obj.tip), breakdown: breakdownOf(obj.breakdown), items: itemsOf(obj.items)
    });
  }
  fail(where + ': not a check the app wrote. A check has "feedback" (GET /api/checks/<id>), or "tip" and ' +
    '"breakdown" at the top (a row of the export), or "oneTip" (a side of a pair).');
}

/* ------------------------------------------------------------------ what changed */
/** Taste.Grouped: the pieces by category, cleaned, kept, and never the same name twice in one category. */
function grouped(items) {
  const g = new Map();
  for (const item of itemsOf(items)) {
    const name = clean(item.name, PIECE_MAX);
    if (!keep(name)) continue;
    const category = item.category;
    if (!g.has(category)) g.set(category, []);
    const list = g.get(category);
    if (!list.some(n => n.toLowerCase() === name.toLowerCase())) list.push(name);
  }
  return g;
}

/**
 * Taste.Changes: per category in the server's order, the pieces named before and after, joined with ", ", only
 * where the two differ (compared without regard to case); null on the side where the category was absent.
 * Empty when the stylist named the same pieces in both.
 */
function changes(beforeItems, afterItems) {
  const a = grouped(beforeItems), b = grouped(afterItems);
  const out = [];
  for (const category of CATEGORIES) {
    const from = a.has(category) ? a.get(category).join(', ') : null;
    const to = b.has(category) ? b.get(category).join(', ') : null;
    const same = from === to || (from !== null && to !== null && from.toLowerCase() === to.toLowerCase());
    if (!same) out.push({ category: category, from: from, to: to });
  }
  return out;
}

/* ------------------------------------------------------------------ the app's words */
const i18nCache = new Map();
function i18n(lang) {
  if (!i18nCache.has(lang)) {
    const file = path.join(I18N_DIR, lang + '.json');
    let table = {};
    try { table = JSON.parse(fs.readFileSync(file, 'utf8')); } catch (e) { table = {}; }
    i18nCache.set(lang, table);
  }
  return i18nCache.get(lang);
}

/**
 * The pill's word for an intent: `intent.<Name>` from the app's i18n table for the language, upper-cased in
 * English (the pill is caps) and as written in Hebrew. An intent the table does not know (or a word already
 * typed as it should appear, "DATE") comes back as given, upper-cased in English.
 */
function intentWord(intent, lang) {
  const raw = intent === undefined || intent === null ? '' : String(intent).trim();
  if (!raw) return '';
  const word = i18n(lang || 'en')['intent.' + raw] || raw;
  return (lang || 'en') === 'he' ? word : word.toUpperCase();
}

/**
 * The app's own wording for one change (tried.change_swap / change_added / change_removed in i18n), with the
 * category's label as the app prints it (items.cat_<category>, an unknown one as items.cat_other; taste.js).
 */
function changeLine(change, lang) {
  const t = i18n(lang || 'en');
  const key = change.from && change.to ? 'tried.change_swap' : change.to ? 'tried.change_added' : 'tried.change_removed';
  const template = t[key] || (key === 'tried.change_swap' ? '{category}: {from} → {to}' : key === 'tried.change_added' ? '{category}: added {to}' : '{category}: {from} gone');
  const category = t['items.cat_' + (CATEGORIES.includes(change.category) ? change.category : 'other')] || change.category;
  return template.replace('{category}', category).replace('{from}', change.from || '').replace('{to}', change.to || '');
}

/* ------------------------------------------------------------------ the episode */
function checkScore(where, v, key) {
  if (typeof v !== 'number' || !isFinite(v) || Math.round(v) !== v || v < 0 || v > 10) {
    fail(where + ': "' + key + '" is ' + JSON.stringify(v) + '. A score is a whole number from 0 to 10.');
  }
  return v;
}
function idsEqual(a, b) { return !!a && !!b && String(a).toLowerCase() === String(b).toLowerCase(); }
function moment(s) { const t = Date.parse(s); return isNaN(t) ? null : t; }

/**
 * A side as an episode JSON spells it: inline fields, a `check` file (a CheckDto or an export row), or an `id`
 * looked up in the episode's `export`. The inline fields win over the file, so a headline can be shortened to fit
 * without touching what the app wrote. `readJson(rel)` is handed in by the caller (the renderer resolves it
 * against the episode's folder); `exportJson` is the parsed export when the episode names one.
 */
function sideFromSpec(spec, opts) {
  const where = (opts && opts.where) || 'the side';
  if (!spec || typeof spec !== 'object' || Array.isArray(spec)) {
    fail(where + ': it must be an object, e.g. {"photo": "...", "id": "<guid>"} or {"photo": "...", "check": "before.json"}.');
  }
  let base;
  if (spec.check) {
    if (!opts || typeof opts.readJson !== 'function') fail(where + ': "check" names a file but nothing here can read one.');
    base = readSide(opts.readJson(spec.check), where + ' > ' + spec.check);
  } else if (spec.id && opts && opts.exportJson) {
    base = readSide(findCheck(opts.exportJson, spec.id, where), where + ' > export');
  } else {
    base = side({});
  }
  for (const k of ['id', 'createdAt', 'status', 'intent', 'occasion', 'score', 'headline', 'tip']) {
    if (spec[k] !== undefined && spec[k] !== null) base[k] = spec[k];
  }
  if (spec.breakdown !== undefined) base.breakdown = breakdownOf(spec.breakdown);
  if (spec.items !== undefined) base.items = itemsOf(spec.items);
  return base;
}

function findCheck(exportJson, id, where) {
  const rows = exportJson && Array.isArray(exportJson.checks) ? exportJson.checks : null;
  if (!rows) fail((where || 'export') + ': the export has no "checks" list. It is the file Settings → Download your data saves.');
  const row = rows.find(r => r && idsEqual(r.id, id));
  if (!row) {
    fail((where || 'export') + ': no check with the id ' + id + ' in the export (' + rows.length + ' checks in it; ' +
      '--list-checks <export.json> prints them with their ids).');
  }
  return row;
}

/**
 * The episode data for the `before-after` variant from two sides (raw or read), the optional pair, and the
 * optional overrides (the tip that was tried, the changes). Guards, each a readable failure: no verdict on a
 * side, the same check twice, an after that is not newer, a side with no breakdown, a pair that is not of these
 * two checks, a score that is not a whole number from 0 to 10.
 */
function fromApp(input) {
  const where = input.where || 'before-after';
  const lang = input.lang === 'he' ? 'he' : 'en';
  const before = readSide(input.before, where + ' > before');
  const after = readSide(input.after, where + ' > after');

  for (const [name, s] of [['before', before], ['after', after]]) {
    const w = where + ' > ' + name;
    const label = s.id ? 'check ' + s.id : 'the ' + name + ' check';
    if (s.status !== 'ok') fail(w + ': ' + label + ' has no verdict (its status is "' + s.status + '"). Only a scored check can be a side.');
    if (s.intent === null || s.intent === '') fail(w + ': "intent" is missing on ' + label + '.');
    checkScore(w, s.score, 'score');
    if (!s.headline) fail(w + ': "headline" is missing on ' + label + '.');
    if (!s.breakdown) {
      fail(w + ': ' + label + ' was scored before the breakdown existed — type "breakdown" by hand, e.g. ' +
        '{"fit": 7, "color": 8, "accessories": 4}, or pick a newer check.');
    }
    ['fit', 'color', 'accessories'].forEach(k => checkScore(w, s.breakdown[k], 'breakdown.' + k));
  }
  if (idsEqual(before.id, after.id)) fail(where + ': before and after are the same check (' + before.id + '). The after is the second check, made after the tip.');
  const tb = moment(before.createdAt), ta = moment(after.createdAt);
  if (tb !== null && ta !== null && ta <= tb) {
    fail(where + ': the after check (' + after.id + ', ' + after.createdAt + ') is not newer than the before check (' +
      before.id + ', ' + before.createdAt + '). The two are the wrong way round.');
  }

  const pair = input.pair;
  if (pair) {
    if (!pair.before || !pair.after) fail(where + ' > pair: not a pair the app wrote (it has "before" and "after").');
    const pb = pair.before.id, pa = pair.after.id;
    if (!idsEqual(pb, before.id) || !idsEqual(pa, after.id)) {
      fail(where + ' > pair: this pair is of ' + pb + ' → ' + pa + ', not of these two checks (' + before.id + ' → ' + after.id + ').');
    }
  }

  const tip = blank(input.tip) || before.tip;
  if (!tip) fail(where + ': the before check has no tip, and "tip" was not given. The tip is the thing that was tried.');
  /* "What changed": given by hand, else computed from the two checks' items, else (two pair sides, which carry no
     items) copied from the pair the server computed. */
  const asChange = c => ({ category: c.category, from: c.from === undefined ? null : c.from, to: c.to === undefined ? null : c.to });
  let changed;
  if (Array.isArray(input.changes)) changed = input.changes.map(asChange);
  else if (before.items.length === 0 && after.items.length === 0 && pair && Array.isArray(pair.changed)) changed = pair.changed.map(asChange);
  else changed = changes(before.items, after.items);

  const sideOut = s => ({
    id: s.id, createdAt: s.createdAt, intent: intentWord(s.intent, lang), intentKey: s.intent, score: s.score,
    headline: s.headline, occasion: s.occasion, breakdown: s.breakdown, tip: s.tip
  });
  return {
    variant: 'before-after', lang: lang,
    before: sideOut(before), after: sideOut(after),
    tip: tip, changes: changed, changeLines: changed.map(c => changeLine(c, lang)), delta: after.score - before.score
  };
}

/** The same, from the export alone and the two ids read off --list-checks. */
function fromExport(exportJson, beforeId, afterId, lang, where) {
  where = where || 'export';
  return fromApp({
    before: findCheck(exportJson, beforeId, where + ' > before'),
    after: findCheck(exportJson, afterId, where + ' > after'),
    lang: lang, where: where
  });
}

/** Every scored check of an export, newest first: { index, createdAt, intent, score, headline, id }. */
function listChecks(exportJson) {
  const rows = exportJson && Array.isArray(exportJson.checks) ? exportJson.checks : [];
  return rows
    .filter(r => r && r.status === 'ok' && typeof r.score === 'number')
    .slice()
    .sort((a, b) => (moment(b.createdAt) || 0) - (moment(a.createdAt) || 0))
    .map((r, i) => ({ index: i + 1, createdAt: r.createdAt, intent: r.intent, score: r.score, headline: r.headline || '', id: r.id,
      breakdown: !!breakdownOf(r.breakdown) }));
}

/** The list as --list-checks prints it, one check a line. */
function listChecksText(exportJson) {
  const rows = listChecks(exportJson);
  if (!rows.length) return 'no scored checks in this export.';
  const lines = rows.map(r =>
    String(r.index).padStart(3) + '  ' + String(r.createdAt || '').slice(0, 16).replace('T', ' ') + '  ' +
    String(r.intent || '').padEnd(10) + ' ' + String(r.score).padStart(2) + '/10  ' + (r.breakdown ? '' : '(no breakdown)  ') +
    r.headline + '\n     ' + r.id);
  return rows.length + ' scored check' + (rows.length === 1 ? '' : 's') + ', newest first:\n' + lines.join('\n');
}

module.exports = {
  BeforeAfterError, CATEGORIES, PIECE_MAX,
  readSide, changes, intentWord, changeLine, sideFromSpec, findCheck, fromApp, fromExport, listChecks, listChecksText,
  clean, keep
};
