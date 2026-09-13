// The pilot's numbers for moderators (#/admin/metrics): /api/metrics/pilot drawn as one hero figure (the return rate,
// the kill switch), a row of stat tiles, the score distribution as a bar list made of divs, the rubric averages, the
// community block as a tile grid, and two small lists (by language, by prompt version). No chart library. The server is
// the gate (403 to anyone else); this page opens the door for the people /me says are moderators and shows the sign-in
// prompt or the 403 line to everyone else.
import { register, state, t, el, api, fmtNumber, intlLocale, localeName, setTopBar, signInPrompt, errorBlock, skeletonCards } from '../core.js';

// Tiles in the card voice; one hue for the bars (lilac, the data), text tokens for every number and label; bars 14px
// thick with a 4px rounded data end and a square baseline, values at the tip, a hairline baseline, nothing else drawn.
const CSS = `
.dash > * + * { margin-block-start: 20px; }
.dash-intro { unicode-bidi: isolate; }
.dash-hero { background: var(--surface); border-radius: var(--radius); padding: 18px 16px 16px; box-shadow: var(--shadow-card); }
.dash-hero .lbl, .dash-tile .lbl { display: block; font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); }
[dir="rtl"] .dash-hero .lbl, [dir="rtl"] .dash-tile .lbl { font-size: 12.5px; letter-spacing: 0.02em; }
.dash-hero .val { display: block; margin-block-start: 6px; font: 800 48px/1 var(--font-display); color: var(--ink); direction: ltr; unicode-bidi: isolate; }
.dash-hero .sub { margin-block-start: 8px; font-size: 13px; line-height: 1.4; color: var(--ink-2); }
.dash-tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; }
.dash-tile { background: var(--surface); border-radius: var(--radius-sm); padding: 12px 14px; box-shadow: var(--shadow-card); min-inline-size: 0; }
.dash-tile .val { display: block; margin-block-start: 4px; font: 700 24px/1 var(--font-display); color: var(--ink); direction: ltr; unicode-bidi: isolate; }
.dash-tile .val small { font: 600 12px/1 var(--font-body); color: var(--ink-3); margin-inline-start: 3px; }
.dash-section > * + * { margin-block-start: 10px; }
.dash-bars { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
.dash-bars li { display: grid; grid-template-columns: 24px 1fr auto; align-items: center; gap: 10px; min-block-size: 22px; }
.dash-bars .lbl { font: 700 13px/1 var(--font-display); color: var(--ink-3); direction: ltr; text-align: end; font-variant-numeric: tabular-nums; }
.dash-bars .track { position: relative; block-size: 14px; border-inline-start: 1px solid var(--line); }
.dash-bars .bar { display: block; block-size: 100%; background: var(--accent); border-start-end-radius: 4px; border-end-end-radius: 4px; }
.dash-bars .val { font-size: 13px; color: var(--ink-2); direction: ltr; font-variant-numeric: tabular-nums; min-inline-size: 2ch; text-align: end; }
.dash-bars li.zero .val { color: var(--ink-3); }
.dash-list { margin: 0; display: grid; gap: 0; }
.dash-list div { display: flex; justify-content: space-between; align-items: center; gap: 12px; min-block-size: 44px; border-block-end: 1px solid var(--line-soft); }
.dash-list div:last-child { border-block-end: 0; }
.dash-list dt { font-size: 15px; color: var(--ink); min-inline-size: 0; overflow-wrap: anywhere; }
.dash-list dd { margin: 0; font: 700 16px/1 var(--font-display); color: var(--ink-2); direction: ltr; font-variant-numeric: tabular-nums; }
.dash-foot { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; padding-block-start: 12px; border-block-start: 1px solid var(--line); font-size: 13px; color: var(--ink-3); }
.dash-foot .btn-sm { min-block-size: 44px; padding-inline: 16px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const percent = (fraction) => new Intl.NumberFormat(intlLocale(), { style: 'percent', maximumFractionDigits: 0 }).format(fraction || 0);
/** Latency in the reader's units: under a second in milliseconds, otherwise seconds with one decimal. */
function latency(ms) {
  const n = ms || 0;
  return n < 1000
    ? new Intl.NumberFormat(intlLocale(), { style: 'unit', unit: 'millisecond', maximumFractionDigits: 0 }).format(n)
    : new Intl.NumberFormat(intlLocale(), { style: 'unit', unit: 'second', maximumFractionDigits: 1 }).format(n / 1000);
}
/** The count a plural key wants: the number 1 (so the _one form fires) or the formatted figure. */
const countArg = (n) => (n === 1 ? 1 : fmtNumber(n));

/** A stat tile: the label in the caps voice, the value in the display face, an optional unit after it. */
function tile(label, value, unit) {
  return el('div', { class: 'dash-tile' }, [
    el('span', { class: 'lbl', text: label }),
    el('span', { class: 'val' }, [String(value), unit ? el('small', { text: unit }) : null])
  ]);
}

/** The score distribution, 1 to 10, one bar each, scaled to the fullest score; the row names itself for a screen reader. */
function scoreBars(distribution) {
  const counts = Array.from({ length: 10 }, (_, i) => (distribution && distribution[String(i + 1)]) || 0);
  const max = Math.max(1, ...counts);
  return el('ol', { class: 'dash-bars', id: 'dash-scores' }, counts.map((n, i) => {
    const score = i + 1;
    return el('li', { class: n ? null : 'zero', 'aria-label': t('dash.score_row', { score, n: countArg(n) }), 'data-score': score, 'data-count': n }, [
      el('span', { class: 'lbl', 'aria-hidden': 'true', text: String(score) }),
      el('span', { class: 'track', 'aria-hidden': 'true' }, [n ? el('span', { class: 'bar', style: 'inline-size: ' + Math.round((n / max) * 100) + '%' }) : null]),
      el('span', { class: 'val', 'aria-hidden': 'true', text: fmtNumber(n) })
    ]);
  }));
}

/** A small key → count list, largest first; the language list names the languages the app has. */
function countList(id, entries, name) {
  const rows = Object.entries(entries || {}).sort((a, b) => b[1] - a[1]);
  return el('dl', { class: 'dash-list', id }, rows.map(([key, n]) => el('div', {}, [el('dt', { text: name ? name(key) : key }), el('dd', { text: fmtNumber(n) })])));
}

function draw(root, m, ctx, reload) {
  const social = m.social || {};
  const averages = m.breakdownAverages;
  const first = m.usersWithAtLeastOneCheck || 0;
  const second = m.usersWithSecondCheckWithin7Days || 0;

  root.appendChild(el('p', { class: 'hint dash-intro', text: t('dash.intro') }));
  root.appendChild(el('div', { class: 'dash-hero', id: 'dash-return' }, [
    el('span', { class: 'lbl', text: t('dash.return_rate') }),
    el('span', { class: 'val', text: percent(m.returnRate) }),
    el('p', { class: 'sub', text: t('dash.return_hint', { second: fmtNumber(second), first: fmtNumber(first) }) })
  ]));
  root.appendChild(el('div', { class: 'dash-tiles', id: 'dash-tiles' }, [
    tile(t('dash.checks'), fmtNumber(m.totalChecks || 0)),
    tile(t('dash.users_with_check'), fmtNumber(first)),
    tile(t('dash.avg_latency'), latency(m.avgLatencyMs))
  ]));

  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.scores') }),
    el('p', { class: 'hint', text: t('dash.scores_hint') }),
    m.totalChecks ? scoreBars(m.scoreDistribution) : el('p', { class: 'empty', id: 'dash-empty', text: t('dash.empty') })
  ]));

  if (averages) {
    root.appendChild(el('section', { class: 'dash-section' }, [
      el('h2', { text: t('dash.breakdown') }),
      el('p', { class: 'hint', text: t('dash.breakdown_hint', { n: countArg(averages.checks || 0) }) }),
      el('div', { class: 'dash-tiles', id: 'dash-breakdown' }, [
        tile(t('dash.fit'), fmtNumber(averages.avgFit), '/10'),
        tile(t('dash.color'), fmtNumber(averages.avgColor), '/10'),
        tile(t('dash.accessories'), fmtNumber(averages.avgAccessories), '/10')
      ])
    ]));
  }

  const socialTiles = [
    ['users', 'dash.users'], ['brands', 'dash.brands'], ['posts', 'dash.posts'], ['fires', 'dash.fires'], ['follows', 'dash.follows'],
    ['comments', 'dash.comments'], ['challengesOpen', 'dash.challenges_open'], ['challengesEnded', 'dash.challenges_ended'], ['votes', 'dash.votes'],
    ['activeUsers7d', 'dash.active7'], ['mentions', 'dash.mentions'], ['featured', 'dash.featured'], ['videos', 'dash.videos'], ['pushSubscriptions', 'dash.push']
  ];
  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.social') }),
    el('div', { class: 'dash-tiles', id: 'dash-social' }, socialTiles.map(([key, label]) => tile(t(label), fmtNumber(social[key] || 0))))
  ]));

  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.by_language') }),
    countList('dash-languages', m.byLanguage, localeName)
  ]));
  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.by_prompt') }),
    countList('dash-prompts', m.byPromptVersion)
  ]));

  const refresh = el('button', { type: 'button', class: 'btn btn-sm btn-secondary', id: 'dash-refresh', text: t('dash.refresh') });
  refresh.addEventListener('click', () => { if (!refresh.disabled) { refresh.disabled = true; reload(); } });
  root.appendChild(el('footer', { class: 'dash-foot' }, [
    el('span', { text: t('dash.as_of', { time: new Intl.DateTimeFormat(intlLocale(), { timeStyle: 'short' }).format(new Date()) }) }),
    refresh
  ]));
}

register('admin-metrics', async (root, params, ctx) => {
  setTopBar({ back: '#/admin', title: t('dash.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('dash.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (!state.me.isAdmin) {
    root.appendChild(el('div', { class: 'notice', id: 'dash-forbidden' }, [el('h3', { text: t('dash.title') }), el('p', { class: 'muted', text: t('admin.forbidden') })]));
    return;
  }
  ensureStyle();

  const body = el('div', { class: 'dash', id: 'dash' }, [skeletonCards(2)]);
  root.appendChild(body);
  async function load() {
    let m;
    try { m = await api('GET', '/api/metrics/pilot'); }
    catch (e) {
      if (ctx.stale()) return;
      // 403 means the flag went away under a signed-in session: say what the server said, without the retry.
      body.replaceChildren(e.status === 403 ? el('div', { class: 'notice', id: 'dash-forbidden' }, [el('p', { text: e.message })]) : errorBlock(e));
      return;
    }
    if (ctx.stale()) return;
    body.replaceChildren();
    draw(body, m || {}, ctx, load);
  }
  await load();
});
