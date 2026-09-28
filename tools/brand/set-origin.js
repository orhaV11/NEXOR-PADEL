#!/usr/bin/env node
/*
  Puts the real production origin everywhere the repository still says looks.example.com. That placeholder is in the
  link-preview tags the app serves, in both landing pages' canonical and hreflang links, in the phone wrapper's config,
  in the store listing's URLs and in the deployment examples, and every one of them has to be the real domain before
  launch: an og:url on the wrong host makes every share preview point at nothing, and a canonical link on the wrong host
  tells search engines the pages are someone else's.

      node tools/brand/set-origin.js https://orevosh.app     # rewrite them all
      node tools/brand/set-origin.js --check                 # exit 1 while any placeholder is left (CI, the runbook)
      node tools/brand/set-origin.js --check https://orevosh.app
      node tools/brand/set-origin.js --dry-run https://x.app # print what would change, write nothing
      node tools/brand/set-origin.js http://localhost:8080 --allow-http

  Idempotent: running it twice changes nothing the second time, and it prints the files it touched with a count per
  file. It refuses an origin that is not https unless --allow-http is given, because everything this rewrites is a
  public URL a browser or a store reviewer will follow. It rewrites two shapes, longest first:

      https://looks.example.com  ->  the origin as given (scheme, host and port)
      looks.example.com          ->  the origin's host only (DOMAIN= for Caddy, allowNavigation, an email address)

  A launch origin has no port, so the two shapes agree. Give it one anyway (a staging box on :8443) and it says so: the
  bare form then carries the port too, which is right for allowNavigation and for Caddy, and wrong for the sample email
  addresses in .env.example and STORE.md, which are yours to fix by hand.

  It deliberately leaves DECISIONS.md (a record of what was decided, not a file to deploy), the test fixtures (a test
  that pins the placeholder is testing the placeholder) and Options.cs's doc comment (an example of the shape of the
  setting, not the setting). Nothing outside the list below is read or written.
*/
'use strict';
const fs = require('fs');
const path = require('path');

/** The placeholder the repository ships with. Everything below is about replacing exactly this. */
const PLACEHOLDER_HOST = 'looks.example.com';

/** Repository-relative, and each one is allowed to be absent (a file a later round removes is not an error). */
const TARGETS = [
  // What a share, a search engine and a phone wrapper read.
  'src/FitCheck.Api/wwwroot/index.html',
  'src/FitCheck.Api/wwwroot/landing/index.html',
  'src/FitCheck.Api/wwwroot/landing/index.he.html',
  'mobile/capacitor.config.json',
  'mobile/README.md',
  // What a person reads while deploying or filling in a store listing.
  'STORE.md',
  'MARKETING.md',
  'brand-kit/README.md',
  'DEPLOY.md',
  'README.md',
  '.env.example'
];

const repoRoot = path.resolve(__dirname, '..', '..');

function usage(message) {
  if (message) {
    process.stderr.write(`set-origin: ${message}\n\n`);
  }

  process.stderr.write(
    'Usage: node tools/brand/set-origin.js <https://your.domain> [--dry-run] [--allow-http]\n' +
    '       node tools/brand/set-origin.js --check [<origin>]\n');
  return 2;
}

/**
 * The origin as it will be written: scheme, host and port, with any path, query and trailing slash dropped, so
 * "https://orevosh.app/" and "https://orevosh.app" are the same thing and nothing ends up doubled in a URL.
 */
function parseOrigin(text, allowHttp) {
  let url;
  try {
    url = new URL(text);
  } catch {
    return { error: `"${text}" is not a URL. Write the whole origin, e.g. https://orevosh.app` };
  }

  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    return { error: `"${text}" is not http(s).` };
  }

  if (url.protocol !== 'https:' && !allowHttp) {
    return { error: `"${text}" is not https. Every URL this rewrites is public; pass --allow-http if you really mean it.` };
  }

  if (url.username || url.password) {
    return { error: 'an origin with a user name or a password in it is never what you want here.' };
  }

  if (url.hostname === PLACEHOLDER_HOST) {
    return { error: `that is the placeholder itself. Give the domain you are launching on.` };
  }

  return { origin: url.origin, host: url.host };
}

/** Every occurrence of the placeholder in one file, counted before anything is written. */
function countPlaceholders(text) {
  return text.split(PLACEHOLDER_HOST).length - 1;
}

/**
 * The full-origin form first, so "https://looks.example.com" becomes the whole new origin (port and all) rather than
 * the new host stuck behind the old scheme; then whatever bare hosts are left.
 */
function rewrite(text, origin, host) {
  return text
    .split(`https://${PLACEHOLDER_HOST}`).join(origin)
    .split(`http://${PLACEHOLDER_HOST}`).join(origin)
    .split(PLACEHOLDER_HOST).join(host);
}

function main(argv) {
  const check = argv.includes('--check');
  const dryRun = argv.includes('--dry-run');
  const allowHttp = argv.includes('--allow-http');
  const positional = argv.filter(a => !a.startsWith('-'));
  if (argv.some(a => a.startsWith('-') && !['--check', '--dry-run', '--allow-http'].includes(a))) {
    return usage(`unknown option ${argv.find(a => a.startsWith('-') && !['--check', '--dry-run', '--allow-http'].includes(a))}`);
  }

  if (positional.length > 1) {
    return usage('one origin at a time.');
  }

  if (positional.length === 0 && !check) {
    return usage('no origin given.');
  }

  let target = null;
  if (positional.length === 1) {
    const parsed = parseOrigin(positional[0], allowHttp);
    if (parsed.error) {
      return usage(parsed.error);
    }

    target = parsed;
  }

  const found = [];
  const changed = [];
  const missing = [];
  for (const relative of TARGETS) {
    const file = path.join(repoRoot, relative);
    if (!fs.existsSync(file)) {
      missing.push(relative);
      continue;
    }

    const before = fs.readFileSync(file, 'utf8');
    const count = countPlaceholders(before);
    if (count === 0) {
      continue;
    }

    found.push({ relative, count });
    if (check || !target) {
      continue;
    }

    const after = rewrite(before, target.origin, target.host);
    if (after !== before && !dryRun) {
      fs.writeFileSync(file, after);
    }

    changed.push({ relative, count });
  }

  // --check is the gate: the runbook and CI want a non-zero exit while any placeholder is still in the tree.
  if (check) {
    if (found.length === 0) {
      process.stdout.write(`set-origin --check: no ${PLACEHOLDER_HOST} left in ${TARGETS.length - missing.length} file(s).\n`);
      return 0;
    }

    process.stdout.write(`set-origin --check: ${PLACEHOLDER_HOST} is still in ${found.length} file(s):\n`);
    for (const { relative, count } of found) {
      process.stdout.write(`  ${relative} (${count})\n`);
    }

    process.stdout.write(`Run: node tools/brand/set-origin.js https://your.domain\n`);
    return 1;
  }

  if (changed.length === 0) {
    process.stdout.write(`set-origin: nothing to do, no ${PLACEHOLDER_HOST} left in any of the ${TARGETS.length - missing.length} file(s).\n`);
    return 0;
  }

  const total = changed.reduce((sum, c) => sum + c.count, 0);
  process.stdout.write(`set-origin: ${PLACEHOLDER_HOST} -> ${target.host} in ${changed.length} file(s), ${total} occurrence(s)${dryRun ? ' (dry run, nothing written)' : ''}:\n`);
  for (const { relative, count } of changed) {
    process.stdout.write(`  ${relative} (${count})\n`);
  }

  if (missing.length > 0) {
    process.stdout.write(`  (not in this checkout: ${missing.join(', ')})\n`);
  }

  if (target.host !== new URL(target.origin).hostname) {
    process.stdout.write(
      `Note: ${target.host} carries a port, so the bare-host lines got it too. Read the sample email addresses ` +
      'in .env.example and STORE.md before you use them.\n');
  }

  process.stdout.write(
    dryRun
      ? 'Nothing was written. Run it again without --dry-run.\n'
      : 'Check the diff, then rebuild the image so the client files the app serves carry the new origin.\n');
  return 0;
}

if (require.main === module) {
  process.exitCode = main(process.argv.slice(2));
}

module.exports = { PLACEHOLDER_HOST, TARGETS, parseOrigin, rewrite, countPlaceholders, main };
