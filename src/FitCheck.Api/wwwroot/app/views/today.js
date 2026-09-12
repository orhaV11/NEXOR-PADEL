// "Today's look": the daily prompt, a hashtag a day, and the looks posted with it. The page (#/today) is the prompt,
// its hint, the grid of today's looks with the tag and the way in; the strip at the top of For you (todayStrip, drawn
// by feed.js) is the compact form with up to eight thumbnails. "Post yours" sends the prompt into the check the way a
// challenge does (state.check.challenge with the tag), so the caption comes pre-filled with the hashtag and the
// server links the look by the tag alone. Everything comes from GET /api/today; nothing about the prompt lives here.
import { register, state, t, api, el, setTopBar, scoreBadge, postGrid, emptyState, fmtDate, fmtCompact, intentLabel, navigate, feedVersion } from '../core.js';

/** Thumbnails on the strip; the page shows the whole day. */
const STRIP_LOOKS = 8;
/** How long a fetched prompt is reused on the strip, so Back from a look draws it at once, without a jump. */
const CACHE_TTL = 5 * 60 * 1000;

let cached = null;   // { data, at, version }

let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    /* the strip on Home: a card in the feed's rhythm */
    '.today-strip { display: grid; gap: 12px; margin: 0 14px 20px; padding: 14px 14px 12px; background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); }',
    '.install + .today-strip { margin-block-start: 12px; }',
    '.today-strip .today-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }',
    '.today-strip .today-head > div { min-inline-size: 0; }',
    '.today-strip .today-side { flex: none; display: flex; flex-direction: column; align-items: flex-end; gap: 6px; margin-block-start: 2px; }',
    '.today-kicker { display: block; font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--accent); }',
    '[dir="rtl"] .today-kicker { font-size: 12.5px; }',
    '.today-title { display: block; margin-block-start: 6px; font-family: var(--font-display); font-weight: 700; font-size: 21px; line-height: 1.15; color: var(--ink); text-decoration: none; overflow-wrap: anywhere; }',
    '.today-hint { margin-block-start: 4px; font-size: 14px; line-height: 1.45; color: var(--ink-2); }',
    '.today-looks { display: flex; gap: 8px; overflow-x: auto; padding-block: 2px 8px; margin-inline: -2px; padding-inline: 2px; scrollbar-width: none; -webkit-overflow-scrolling: touch; }',
    '.today-looks::-webkit-scrollbar { display: none; }',
    '.today-looks a { flex: none; position: relative; display: block; inline-size: 72px; block-size: 90px; border-radius: 10px; background: var(--surface-2); text-decoration: none; }',
    '.today-looks img { inline-size: 100%; block-size: 100%; object-fit: cover; border-radius: inherit; display: block; }',
    '.today-looks .score-badge { inline-size: 26px; block-size: 26px; border-width: 2px; font-size: 13px; gap: 0; inset-block-end: -6px; inset-inline-end: 4px; box-shadow: 0 4px 10px rgba(0, 0, 0, 0.35); }',
    '.today-looks .score-badge small { font-size: 5px; }',
    '.today-actions { display: flex; align-items: center; justify-content: space-between; gap: 10px; flex-wrap: wrap; }',
    '.today-actions .btn-sm { min-block-size: 44px; }',
    '.today-actions .btn-text { padding-block: 0; }',
    /* the page */
    '.today-front { display: grid; gap: 8px; padding-block-end: 16px; border-block-end: 1px solid var(--line); }',
    '.today-front .today-title { font-size: 28px; line-height: 1.05; font-weight: 800; margin-block-start: 0; }',
    '.today-front .today-hint { font-size: 16px; }',
    '.today-front .today-tags { display: flex; flex-wrap: wrap; gap: 8px; margin-block-start: 4px; }',
    '.today-front .today-tags .tag { min-block-size: 32px; }',
    '.today-page .today-cta { display: grid; gap: 8px; }',
    '.today-page .today-count { font-size: 14px; color: var(--ink-3); }',
    '.today-page .grid { padding-inline: 0; }',
    '.today-skel { block-size: 132px; border-radius: var(--radius); }'
  ].join('\n') }));
}

/** "Post yours": the prompt goes into the check the way a challenge does; the caption comes pre-filled with its hashtag. */
export function postYours(today) {
  state.check.challenge = { title: today.title, tag: today.tag, daily: true };
  if (!state.check.intent && today.intent) state.check.intent = today.intent;
  navigate('#/check');
}

/** The kicker line: TODAY · the date (the reader's own; the prompt itself turns over at midnight UTC). */
function kicker() {
  return el('span', { class: 'today-kicker', text: t('today.kicker') + ' · ' + fmtDate(new Date().toISOString()) });
}

function hashtag(today) {
  return el('a', { class: 'tag accent', href: '#/tag/' + encodeURIComponent(today.tag) }, [el('bdi', { dir: 'ltr', text: '#' + today.tag })]);
}

/** Up to eight of today's looks as small prints with their score rings; null when nobody has posted yet. */
function looksRow(today) {
  const posts = (today.posts || []).slice(0, STRIP_LOOKS);
  if (!posts.length) return null;
  return el('div', { class: 'today-looks', 'aria-label': t('today.looks_label') }, posts.map((p) => el('a', {
    href: '#/post/' + p.id, 'aria-label': t(p.videoUrl ? 'a11y.clip_by' : 'a11y.look_by', { intent: intentLabel(p.intent), name: p.user.name })
  }, [
    el('img', { src: p.imageUrl, alt: '', loading: 'lazy', decoding: 'async' }),
    scoreBadge(p.score)
  ])));
}

function postButton(today, id) {
  return el('button', { type: 'button', class: 'btn btn-sm', id, text: t('today.post'), onclick: () => postYours(today) });
}

/** The strip's content for one prompt. */
function stripContent(today) {
  return [
    el('div', { class: 'today-head' }, [
      el('div', {}, [
        kicker(),
        el('a', { class: 'today-title', id: 'today-title', href: '#/today', text: today.title }),
        el('p', { class: 'today-hint', text: today.hint })
      ]),
      el('div', { class: 'today-side' }, [hashtag(today), today.posted ? el('span', { class: 'tag', text: t('today.posted') }) : null])
    ]),
    looksRow(today),
    el('div', { class: 'today-actions' }, [
      postButton(today, 'today-post'),
      el('a', { class: 'btn-text', href: '#/today', text: t('today.all') })
    ])
  ];
}

/**
 * The Today strip for the top of For you. place(node) puts the built strip where the feed wants it; it is called at
 * once from a fresh cache (Back from a look lands where the reader was, nothing shifts) and otherwise when /api/today
 * answers. opts.force skips the cache (a pull to refresh, the Home tab tapped again). A failure calls nothing: the
 * feed goes on as if there were no prompt. Never throws.
 */
export function todayStrip(ctx, place, opts) {
  ensureStyle();
  const build = (today) => el('section', { class: 'today-strip', id: 'today-strip', 'aria-labelledby': 'today-title' }, stripContent(today));
  const fresh = !(opts && opts.force) && cached && cached.version === feedVersion.n && Date.now() - cached.at < CACHE_TTL;
  if (fresh) { place(build(cached.data)); return; }
  api('GET', '/api/today')
    .then((today) => {
      if (!today || !today.tag) return;
      cached = { data: today, at: Date.now(), version: feedVersion.n };
      if (ctx && ctx.stale && ctx.stale()) return;
      place(build(today));
    })
    .catch(() => { /* no prompt today, then; the feed does not care */ });
}

// ---------- the page ----------

register('today', async (root, params, ctx) => {
  setTopBar({ back: '#/', title: t('today.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('today.title') }));
  ensureStyle();
  const skel = el('div', { class: 'skel today-skel', 'aria-hidden': 'true' });
  root.appendChild(skel);

  let today;
  try { today = await api('GET', '/api/today'); }
  catch (e) { if (ctx.stale()) return; skel.remove(); throw e; }
  if (ctx.stale()) return;
  skel.remove();
  cached = { data: today, at: Date.now(), version: feedVersion.n };

  const posts = today.posts || [];
  const n = posts.length;
  root.appendChild(el('div', { class: 'stack today-page' }, [
    el('div', { class: 'today-front' }, [
      kicker(),
      el('p', { class: 'today-title', text: today.title }),
      el('p', { class: 'today-hint', text: today.hint }),
      el('div', { class: 'today-tags' }, [hashtag(today), today.intent ? el('span', { class: 'tag', text: intentLabel(today.intent) }) : null])
    ]),
    el('div', { class: 'today-cta' }, [
      today.posted ? el('span', { class: 'tag', style: 'justify-self: start;', text: t('today.posted') }) : null,
      postButton(today, 'today-post'),
      el('p', { class: 'hint', text: t('check.entering_hint', { tag: '#' + today.tag }) })
    ]),
    n ? el('p', { class: 'today-count', text: t('today.looks', { n: n === 1 ? 1 : fmtCompact(n) }) }) : null,
    n ? el('div', { id: 'today-grid' }, [postGrid(posts)]) : emptyState(t('today.empty'))
  ]));
});
