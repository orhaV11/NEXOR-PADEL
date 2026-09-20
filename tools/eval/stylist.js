#!/usr/bin/env node
// Does the stylist say the same thing twice?
//
// The model this app runs on (claude-sonnet-5) has no temperature, top_p or top_k: the API removed them and answers 400
// to a request that carries one. So the only way to hold the scores steady is the rubric's anchored bands - and the only
// way to know whether they hold is to send the same photo again and look at the spread. That is what this does.
//
//   node tools/eval/stylist.js --photo look.jpg --occasion date --style streetwear --runs 8 --base http://127.0.0.1:5080
//
// It talks to a RUNNING OREVOSH over its real API, so every run is a real model call on a real key and costs real money
// (see tools/eval/README.md for the arithmetic). It signs in as an account you name, because a guest gets one check a
// day; that account needs an allowance of at least --runs (a Pro account, or a server with Plans__ProChecksPerDay
// raised). Nothing here talks to Anthropic directly: it is the app's own answer that is measured, rubric and all.
//
// Exit code 0 when every photo's spread is within --max-spread, 1 when one is not, 2 when the run could not be made.
'use strict';

const fs = require('fs');
const path = require('path');

const USAGE = `
Usage: node tools/eval/stylist.js --photo <file> [options]

  --photo <file>       The photo to send. Repeatable: each one gets its own table.
  --occasion <name>    everyday | date | office | party | formal | sport   (default: everyday)
  --style <name>       streetwear | old-money | minimal | classic | none   (default: none)
  --note <text>        The wearer's free line, as typed on the check screen.
  --runs <n>           How many times each photo is sent (default: 8). Each run is one model call.
  --base <url>         The running app (default: http://127.0.0.1:5080).
  --handle <handle>    The account to sign in as (default: $OREVOSH_EVAL_HANDLE).
  --password <pw>      Its password (default: $OREVOSH_EVAL_PASSWORD).
  --cookie <cookie>    A session cookie instead of signing in ("fc_session=...").
  --language <code>    The language the feedback is written in (default: en).
  --max-spread <n>     The most the score may move before this run counts as a failure (default: 2).
  --delay <ms>         Wait between runs (default: 0).
  --json               Print the raw rows as JSON instead of the tables.
  --help               This.
`.trim();

function parseArgs(argv) {
  const options = {
    photos: [], occasion: 'everyday', style: '', note: '', runs: 8,
    base: 'http://127.0.0.1:5080', handle: process.env.OREVOSH_EVAL_HANDLE || '',
    password: process.env.OREVOSH_EVAL_PASSWORD || '', cookie: process.env.OREVOSH_EVAL_COOKIE || '',
    language: 'en', maxSpread: 2, delay: 0, json: false
  };
  for (let i = 2; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      const next = argv[++i];
      if (next === undefined) throw new Error(`${arg} needs a value`);
      return next;
    };
    switch (arg) {
      case '--photo': options.photos.push(value()); break;
      case '--occasion': options.occasion = value(); break;
      case '--style': options.style = value(); break;
      case '--note': options.note = value(); break;
      case '--runs': options.runs = Number(value()); break;
      case '--base': options.base = value().replace(/\/$/, ''); break;
      case '--handle': options.handle = value(); break;
      case '--password': options.password = value(); break;
      case '--cookie': options.cookie = value(); break;
      case '--language': options.language = value(); break;
      case '--max-spread': options.maxSpread = Number(value()); break;
      case '--delay': options.delay = Number(value()); break;
      case '--json': options.json = true; break;
      case '--help': case '-h': console.log(USAGE); process.exit(0); break;
      default: throw new Error(`unknown option ${arg}`);
    }
  }
  if (!options.photos.length) throw new Error('at least one --photo is needed');
  if (!Number.isInteger(options.runs) || options.runs < 1) throw new Error('--runs must be a whole number of at least 1');
  if (!(options.maxSpread >= 0)) throw new Error('--max-spread must be a number');
  for (const photo of options.photos) {
    if (!fs.existsSync(photo)) throw new Error(`no such photo: ${photo}`);
  }

  return options;
}

const MEDIA = { '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png', '.webp': 'image/webp' };
const mediaType = (file) => MEDIA[path.extname(file).toLowerCase()] || 'application/octet-stream';
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** The session cookie for the account the runs are spent from. A guest gets one check a day, which is not a sample. */
async function signIn(options) {
  if (options.cookie) return options.cookie;
  if (!options.handle || !options.password) {
    throw new Error('sign in with --handle and --password (or --cookie, or OREVOSH_EVAL_HANDLE / OREVOSH_EVAL_PASSWORD)');
  }

  const response = await fetch(`${options.base}/api/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'X-Requested-With': 'Orevosh' },
    body: JSON.stringify({ handle: options.handle, password: options.password })
  });
  if (!response.ok) {
    throw new Error(`could not sign in as ${options.handle}: ${response.status} ${await response.text()}`);
  }

  const cookies = (response.headers.getSetCookie ? response.headers.getSetCookie() : [response.headers.get('set-cookie')])
    .filter(Boolean).map((line) => line.split(';')[0]);
  if (!cookies.length) throw new Error('the server signed us in without a cookie');
  return cookies.join('; ');
}

/** One check: the same photo, the same two questions, a fresh model call. */
async function runOnce(options, cookie, photo, bytes) {
  const form = new FormData();
  form.append('occasion', options.occasion);
  form.append('style', options.style === 'none' ? '' : options.style);
  form.append('note', options.note);
  form.append('language', options.language);
  form.append('image', new Blob([bytes], { type: mediaType(photo) }), path.basename(photo));

  const started = Date.now();
  const response = await fetch(`${options.base}/api/checks`, {
    method: 'POST', headers: { 'X-Requested-With': 'Orevosh', cookie }, body: form
  });
  const text = await response.text();
  if (!response.ok) {
    let reason = text;
    try { reason = JSON.parse(text).error || text; } catch (e) { /* the body is not ours */ }
    throw new Error(`POST /api/checks answered ${response.status}: ${reason}`);
  }

  const check = JSON.parse(text);
  const feedback = check.feedback || {};
  const breakdown = feedback.breakdown || {};
  return {
    id: check.id,
    status: check.status,
    ms: Date.now() - started,
    score: feedback.score ?? null,
    intentMatch: feedback.intentMatch ?? null,
    fit: breakdown.fit ?? null,
    color: breakdown.color ?? null,
    accessories: breakdown.accessories ?? null,
    tipKind: feedback.tipKind || null,
    tip: feedback.oneTip || '',
    headline: feedback.headline || '',
    message: feedback.message || ''
  };
}

const mean = (values) => values.reduce((a, b) => a + b, 0) / values.length;
/** Population standard deviation: these runs are the whole sample, not a draw from a bigger one. */
function stdDev(values) {
  if (values.length < 2) return 0;
  const m = mean(values);
  return Math.sqrt(mean(values.map((v) => (v - m) ** 2)));
}
const one = (n) => (n === null ? ' -' : n.toFixed(1));

function summarise(rows) {
  const ok = rows.filter((r) => r.status === 'ok' && typeof r.score === 'number');
  const scores = ok.map((r) => r.score);
  const triples = ok.filter((r) => r.fit !== null).map((r) => `${r.fit}/${r.color}/${r.accessories}`);
  const distinct = new Set(triples);
  return {
    runs: rows.length,
    ok: ok.length,
    refused: rows.filter((r) => r.status !== 'ok').length,
    min: scores.length ? Math.min(...scores) : null,
    max: scores.length ? Math.max(...scores) : null,
    spread: scores.length ? Math.max(...scores) - Math.min(...scores) : 0,
    mean: scores.length ? mean(scores) : null,
    sd: scores.length ? stdDev(scores) : 0,
    matchSpread: ok.length ? Math.max(...ok.map((r) => r.intentMatch)) - Math.min(...ok.map((r) => r.intentMatch)) : 0,
    breakdownsSeen: distinct.size,
    breakdownMoved: triples.length ? triples.filter((t) => t !== triples[0]).length : 0,
    keeps: ok.filter((r) => r.tipKind === 'keep').length
  };
}

function printPhoto(photo, rows, summary, options) {
  console.log('');
  console.log(`PHOTO  ${photo}   ${options.occasion}${options.style && options.style !== 'none' ? ' · ' + options.style : ' · no style'}`);
  console.log('  run  score  fit  col  acc   match  tip     headline');
  rows.forEach((row, i) => {
    const n = String(i + 1).padStart(5);
    if (row.status !== 'ok') {
      console.log(`${n}      -    -    -    -       -  ${(row.status || 'error').padEnd(7)} ${row.message || ''}`);
      return;
    }
    console.log(`${n}  ${String(row.score).padStart(5)}  ${String(row.fit ?? '-').padStart(3)}  ${String(row.color ?? '-').padStart(3)}  ${String(row.accessories ?? '-').padStart(3)}  ${String(row.intentMatch).padStart(6)}  ${(row.tipKind || '-').padEnd(7)} ${row.headline}`);
  });

  console.log('');
  console.log(`  score      min ${summary.min ?? '-'}   max ${summary.max ?? '-'}   mean ${one(summary.mean)}   sd ${summary.sd.toFixed(2)}   spread ${summary.spread}`);
  console.log(`  breakdown  ${summary.breakdownsSeen} different fit/colour/accessories reading(s); it moved on ${summary.breakdownMoved} of ${summary.ok} run(s)`);
  console.log(`  intent     match moved ${summary.matchSpread} point(s) across the runs`);
  console.log(`  tip        ${summary.keeps} keep(s), ${summary.ok - summary.keeps} change(s)`);
  if (summary.refused) console.log(`  refused    ${summary.refused} run(s) came back as something other than a score`);

  console.log('');
  console.log('  THE TIPS, SIDE BY SIDE');
  rows.forEach((row, i) => {
    if (row.status !== 'ok') { console.log(`   ${i + 1}. (${row.status}) ${row.message || ''}`); return; }
    console.log(`   ${i + 1}. [${row.tipKind}] ${row.tip}`);
  });
}

async function main() {
  let options;
  try { options = parseArgs(process.argv); } catch (e) { console.error(e.message + '\n\n' + USAGE); process.exit(2); }

  let cookie;
  try { cookie = await signIn(options); } catch (e) { console.error(e.message); process.exit(2); }

  const results = [];
  for (const photo of options.photos) {
    const bytes = fs.readFileSync(photo);
    const rows = [];
    for (let i = 0; i < options.runs; i++) {
      if (i && options.delay) await sleep(options.delay);
      try {
        rows.push(await runOnce(options, cookie, photo, bytes));
      } catch (e) {
        // A run that never produced a verdict is reported, not hidden, and the rest still run.
        rows.push({ status: 'error', message: e.message, score: null, intentMatch: null, fit: null, color: null, accessories: null, tipKind: null, tip: '', headline: '', ms: 0 });
      }
    }

    results.push({ photo, rows, summary: summarise(rows) });
  }

  if (options.json) {
    console.log(JSON.stringify({
      base: options.base, occasion: options.occasion, style: options.style || 'none', note: options.note,
      runs: options.runs, maxSpread: options.maxSpread, photos: results
    }, null, 2));
  } else {
    console.log(`OREVOSH stylist eval · ${options.base} · ${options.runs} run(s) per photo · rubric answers only, no model call made from here`);
    for (const result of results) printPhoto(result.photo, result.rows, result.summary, options);
    if (results.length > 1) {
      console.log('');
      console.log('ACROSS THE PHOTOS');
      console.log('  photo                                     min  max   mean     sd  spread  keeps');
      for (const { photo, summary } of results) {
        const name = (photo.length > 40 ? '…' + photo.slice(-39) : photo).padEnd(40);
        console.log(`  ${name}  ${String(summary.min ?? '-').padStart(3)}  ${String(summary.max ?? '-').padStart(3)}  ${one(summary.mean).padStart(5)}  ${summary.sd.toFixed(2).padStart(5)}  ${String(summary.spread).padStart(6)}  ${String(summary.keeps).padStart(5)}`);
      }
    }
  }

  const loud = results.filter((r) => r.summary.spread > options.maxSpread);
  const empty = results.filter((r) => r.summary.ok === 0);
  if (!options.json) {
    console.log('');
    if (empty.length) console.log(`NO VERDICT: ${empty.length} photo(s) never came back with a score. Nothing was measured.`);
    else if (loud.length) console.log(`TOO WIDE: ${loud.map((r) => `${path.basename(r.photo)} spread ${r.summary.spread}`).join(', ')} (allowed: ${options.maxSpread}).`);
    else console.log(`WITHIN ${options.maxSpread}: every photo's score stayed inside the allowed spread.`);
  }

  process.exit(loud.length || empty.length ? 1 : 0);
}

main().catch((e) => { console.error(e && e.stack ? e.stack : e); process.exit(2); });
