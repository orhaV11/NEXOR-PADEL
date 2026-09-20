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
    ['activeUsers7d', 'dash.active7'], ['mentions', 'dash.mentions'], ['featured', 'dash.featured'], ['videos', 'dash.videos'], ['pushSubscriptions', 'dash.push'],
    ['videosMade', 'video.made']   // share videos made on the phone and shared or saved (POST /api/checks/{id}/shared-video)
  ];
  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.social') }),
    el('div', { class: 'dash-tiles', id: 'dash-social' }, socialTiles.map(([key, label]) => tile(t(label), fmtNumber(social[key] || 0))))
  ]));

  // Round 13: the stylist's own block (stylistSection, at the end of this file) sits between the community and the lists.
  if (m.stylist) root.appendChild(stylistSection(m.stylist));

  // Round 13 - money: the spend block (moneySection, at the end of this file), after the stylist and before the lists.
  if (m.spend) root.appendChild(moneySection(m.spend));

  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.by_language') }),
    countList('dash-languages', m.byLanguage, localeName)
  ]));
  root.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('dash.by_prompt') }),
    countList('dash-prompts', m.byPromptVersion)
  ]));

  // Round 13 — the growth loop: the funnel and the invites (funnelSection, at the end of this file), last before the footer.
  if (m.funnel) root.appendChild(funnelSection(m.funnel));

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

// ---------- Round 13: the verdict's own verdict ----------

// The stylist block: one hero-sized rate ("Tip landed"), the yes / no / unanswered tiles and the door's two counts in one
// section, then the split by intent and by language as two more sections of key → "3 yes · 1 no · 2 unanswered" lists,
// each under the page's own caps h2. Same tiles, same lists, same voice as the rest of the page; nothing new drawn.
const STYLIST_CSS = `
.dash-stylist .dash-hero .val { font-size: 40px; }
.dash-split dd { font: 500 14px/1.3 var(--font-body); color: var(--ink-2); text-align: end; direction: inherit; }
`;
let stylistStyled = false;
/** "{yes} yes · {no} no · {unanswered} unanswered" for one split, numbers in the reader's digits. */
const splitText = (split) => t('useful.dash_split', { yes: fmtNumber(split.yes || 0), no: fmtNumber(split.no || 0), unanswered: fmtNumber(split.unanswered || 0) });
/** A key → split list, most answered first; name() renders the key (the intent's label, the language's name). */
function splitList(id, entries, name) {
  const rows = Object.entries(entries || {}).sort((a, b) => (b[1].yes + b[1].no) - (a[1].yes + a[1].no) || b[1].unanswered - a[1].unanswered);
  return el('dl', { class: 'dash-list dash-split', id }, rows.map(([key, split]) => el('div', { 'data-key': key }, [
    el('dt', { text: name ? name(key) : key }),
    el('dd', { text: (split.rate === null || split.rate === undefined ? '' : percent(split.rate) + ' · ') + splitText(split) })
  ])));
}
function stylistSection(stylist) {
  if (!stylistStyled) { stylistStyled = true; document.head.appendChild(el('style', { text: STYLIST_CSS })); }
  const overall = stylist.useful || { yes: 0, no: 0, unanswered: 0, rate: null };
  const answered = (overall.yes || 0) + (overall.no || 0);
  const fragment = document.createDocumentFragment();
  fragment.appendChild(el('section', { class: 'dash-section dash-stylist', id: 'dash-stylist' }, [
    el('h2', { text: t('useful.dash_title') }),
    el('p', { class: 'hint', text: t('useful.dash_hint') }),
    el('div', { class: 'dash-hero', id: 'dash-useful-rate' }, [
      el('span', { class: 'lbl', text: t('useful.dash_rate') }),
      el('span', { class: 'val', text: answered ? percent(overall.rate) : '–' }),
      el('p', { class: 'sub', text: answered ? splitText(overall) : t('useful.dash_none') })
    ]),
    el('div', { class: 'dash-tiles', id: 'dash-useful' }, [
      tile(t('useful.dash_yes'), fmtNumber(overall.yes || 0)),
      tile(t('useful.dash_no'), fmtNumber(overall.no || 0)),
      tile(t('useful.dash_unanswered'), fmtNumber(overall.unanswered || 0)),
      tile(t('nooutfit.dash'), fmtNumber(stylist.notOutfit || 0)),
      tile(t('nooutfit.dash_rejected'), fmtNumber(stylist.rejected || 0))
    ])
  ]));
  fragment.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('useful.dash_by_intent') }),
    splitList('dash-useful-intents', stylist.byIntent, (intent) => t('intent.' + intent))
  ]));
  fragment.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('useful.dash_by_language') }),
    splitList('dash-useful-languages', stylist.byLanguage, localeName)
  ]));
  return fragment;
}

// ---------- Round 13 — money: the spend meter, the daily ceiling and the alerts ----------

// The money block: one hero (today's estimate in dollars), the day's tiles, a 14-day bar series, the prices the
// estimate was made at, and whether an alert channel is set at all. Same tiles, same bars, same voice as the rest of
// the page; nothing new drawn and no chart library. Every dollar here is an estimate, and the page says so out loud.
const MONEY_CSS = `
.dash-money .dash-hero .val { font-size: 40px; }
.dash-money .dash-bars li { grid-template-columns: 4.5em 1fr auto; }
.dash-money .dash-bars .lbl { font-size: 12px; text-align: start; }
.dash-money .resting { margin: 0; padding: 12px 14px; border-radius: var(--radius-sm); background: var(--surface); box-shadow: var(--shadow-card); font-size: 14px; line-height: 1.45; color: var(--ink); }
.dash-money .channels { display: flex; flex-wrap: wrap; gap: 8px; margin: 0; padding: 0; list-style: none; }
.dash-money .channels li { font-size: 13px; color: var(--ink-2); background: var(--surface); border-radius: 999px; padding: 8px 12px; box-shadow: var(--shadow-card); }
`;
let moneyStyled = false;
/** Dollars in the reader's digits, two decimals; the currency is USD because the prices are set in USD. */
const usd = (amount) => new Intl.NumberFormat(intlLocale(), { style: 'currency', currency: 'USD', maximumFractionDigits: 2 }).format(Number(amount) || 0);
/** "20260920" as a short date the reader knows; the raw key when it is not a day we can parse. */
function dayLabel(day) {
  const text = String(day || '');
  if (!/^\d{8}$/.test(text)) return text;
  const date = new Date(Date.UTC(+text.slice(0, 4), +text.slice(4, 6) - 1, +text.slice(6, 8)));
  return new Intl.DateTimeFormat(intlLocale(), { month: 'short', day: 'numeric', timeZone: 'UTC' }).format(date);
}
/** The 14-day series as bars, scaled to the costliest day; the row names itself for a screen reader. */
function spendBars(series) {
  const days = Array.isArray(series) ? series : [];
  const max = Math.max(...days.map((d) => Number(d.estimatedUsd) || 0), 0.0001);
  return el('ol', { class: 'dash-bars', id: 'dash-spend-days' }, days.map((day) => {
    const amount = Number(day.estimatedUsd) || 0;
    const calls = day.calls || 0;
    return el('li', {
      class: amount ? null : 'zero',
      'data-day': day.day,
      'data-calls': calls,
      'aria-label': t('money.series_row', { day: dayLabel(day.day), usd: usd(amount), n: countArg(calls) })
    }, [
      el('span', { class: 'lbl', 'aria-hidden': 'true', text: dayLabel(day.day) }),
      el('span', { class: 'track', 'aria-hidden': 'true' }, [amount ? el('span', { class: 'bar', style: 'inline-size: ' + Math.max(2, Math.round((amount / max) * 100)) + '%' }) : null]),
      el('span', { class: 'val', 'aria-hidden': 'true', text: usd(amount) })
    ]);
  }));
}
function moneySection(spend) {
  if (!moneyStyled) { moneyStyled = true; document.head.appendChild(el('style', { text: MONEY_CSS })); }
  const today = spend.today || { calls: 0, inputTokens: 0, outputTokens: 0, estimatedUsd: 0 };
  const ceiling = Number(spend.ceilingUsd) || 0;
  const fragment = document.createDocumentFragment();
  fragment.appendChild(el('section', { class: 'dash-section dash-money', id: 'dash-money' }, [
    el('h2', { text: t('money.title') }),
    el('p', { class: 'hint', text: t('money.hint') }),
    spend.resting ? el('p', { class: 'resting', id: 'dash-resting', text: t('money.resting') }) : null,
    el('div', { class: 'dash-hero', id: 'dash-spend-today' }, [
      el('span', { class: 'lbl', text: t('money.today') }),
      el('span', { class: 'val', text: usd(today.estimatedUsd) }),
      el('p', { class: 'sub', text: t('money.today_sub', { n: countArg(today.calls || 0), calls: fmtNumber(today.calls || 0), in: fmtNumber(today.inputTokens || 0), out: fmtNumber(today.outputTokens || 0) }) })
    ]),
    el('div', { class: 'dash-tiles', id: 'dash-spend-tiles' }, [
      tile(t('money.calls'), fmtNumber(today.calls || 0)),
      tile(t('money.tokens_in'), fmtNumber(today.inputTokens || 0)),
      tile(t('money.tokens_out'), fmtNumber(today.outputTokens || 0)),
      tile(t('money.ceiling'), ceiling > 0 ? usd(ceiling) : t('money.ceiling_off'))
    ]),
    el('p', { class: 'hint', id: 'dash-prices', text: t('money.prices', { in: usd(spend.priceInPerMillion), out: usd(spend.priceOutPerMillion) }) })
  ]));
  fragment.appendChild(el('section', { class: 'dash-section dash-money' }, [
    el('h2', { text: t('money.series') }),
    spendBars(spend.series)
  ]));
  fragment.appendChild(el('section', { class: 'dash-section dash-money' }, [
    el('h2', { text: t('money.alerts') }),
    spend.alertWebhook || spend.alertEmail
      ? el('ul', { class: 'channels', id: 'dash-alert-channels' }, [
        el('li', { text: t('money.alert_webhook') + ' · ' + t(spend.alertWebhook ? 'money.alert_on' : 'money.alert_off') }),
        el('li', { text: t('money.alert_email') + ' · ' + t(spend.alertEmail ? 'money.alert_on' : 'money.alert_off') })
      ])
      : el('p', { class: 'empty', id: 'dash-alerts-none', text: t('money.alert_none') })
  ]));
// ---------- Round 13 — the growth loop: the funnel and the invites ----------

// Fourteen days as a small table the thumb can push sideways (a funnel is a shape, and a shape wants its columns next
// to each other), then today's step-over-the-step-before as the page's own key → value list, then the invites: two
// tiles and the handles doing the inviting. Every number here is this server's own Counter rows or its own tables:
// no cookie is set for any of it and nothing third-party is asked.
const FUNNEL_CSS = `
.dash-scroll { overflow-x: auto; -webkit-overflow-scrolling: touch; margin-inline: -16px; padding-inline: 16px; }
.dash-table { border-collapse: collapse; inline-size: 100%; min-inline-size: 520px; font-variant-numeric: tabular-nums; }
.dash-table th, .dash-table td { padding: 8px 10px; text-align: end; white-space: nowrap; border-block-end: 1px solid var(--line-soft); }
.dash-table th { font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); }
.dash-table th:first-child, .dash-table td:first-child { text-align: start; }
.dash-table td { font-size: 14px; color: var(--ink-2); direction: ltr; }
.dash-table tbody th { font: 600 13px/1.2 var(--font-body); color: var(--ink-3); text-transform: none; letter-spacing: 0; }
.dash-table tbody tr:last-child td, .dash-table tbody tr:last-child th { border-block-end: 0; color: var(--ink); font-weight: 700; }
.dash-inviters dt { direction: ltr; unicode-bidi: isolate; }
`;
let funnelStyled = false;

/** The day as the reader's calendar writes it, short; the ISO string stays on the row for a test to find. */
function funnelDay(iso) {
  return new Intl.DateTimeFormat(intlLocale(), { month: 'short', day: 'numeric', timeZone: 'UTC' }).format(new Date(iso + 'T00:00:00Z'));
}

/** A step over the step before it as a percentage, or an en dash when the step before it never happened. */
const rate = (value) => (value === null || value === undefined ? '–' : percent(value));

function funnelSection(funnel) {
  if (!funnelStyled) { funnelStyled = true; document.head.appendChild(el('style', { text: FUNNEL_CSS })); }
  const days = Array.isArray(funnel.days) ? funnel.days : [];
  const today = funnel.today || {};
  const invites = funnel.invites || { sent: 0, accepted: 0, top: [] };
  const columns = [
    ['landing', 'funnel.landing'], ['guestChecks', 'funnel.guest_checks'], ['signups', 'funnel.signups'],
    ['firstPosts', 'funnel.first_posts'], ['lookArrivals', 'funnel.arrivals'], ['shareArrivals', 'funnel.share_arrivals'],
    ['invites', 'funnel.invites']
  ];

  const fragment = document.createDocumentFragment();
  fragment.appendChild(el('section', { class: 'dash-section', id: 'dash-funnel' }, [
    el('h2', { text: t('funnel.title') }),
    el('p', { class: 'hint', text: t('funnel.hint') }),
    days.length
      ? el('div', { class: 'dash-scroll' }, [
        el('table', { class: 'dash-table', id: 'dash-funnel-table' }, [
          el('thead', {}, [el('tr', {}, [el('th', { scope: 'col', text: t('funnel.day') }), ...columns.map(([, label]) => el('th', { scope: 'col', text: t(label) }))])]),
          el('tbody', {}, days.map((day) => el('tr', { 'data-day': day.day }, [
            el('th', { scope: 'row', text: funnelDay(day.day) }),
            ...columns.map(([key]) => el('td', { 'data-key': key, text: fmtNumber(day[key] || 0) }))
          ])))
        ])
      ])
      : el('p', { class: 'empty', text: t('funnel.none') })
  ]));

  fragment.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('funnel.today') }),
    el('dl', { class: 'dash-list', id: 'dash-funnel-today' }, [
      ['funnel.landing_to_check', today.landingToGuestCheck],
      ['funnel.check_to_signup', today.guestCheckToSignup],
      ['funnel.signup_to_post', today.signupToFirstPost],
      ['funnel.post_to_arrival', today.firstPostToArrival],
      ['funnel.arrival_from_share', today.arrivalFromShare]
    ].map(([label, value]) => el('div', { 'data-step': label }, [el('dt', { text: t(label) }), el('dd', { text: rate(value) })])))
  ]));

  fragment.appendChild(el('section', { class: 'dash-section' }, [
    el('h2', { text: t('funnel.invites_title') }),
    el('div', { class: 'dash-tiles', id: 'dash-invites' }, [
      tile(t('funnel.invites_sent'), fmtNumber(invites.sent || 0)),
      tile(t('funnel.invites_accepted'), fmtNumber(invites.accepted || 0))
    ]),
    el('h2', { text: t('funnel.top_inviters') }),
    (invites.top && invites.top.length)
      ? el('dl', { class: 'dash-list dash-inviters', id: 'dash-inviters' }, invites.top.map((row) => el('div', { 'data-handle': row.handle }, [
        el('dt', { text: '@' + row.handle }),
        el('dd', { text: fmtNumber(row.accepted || 0) })
      ])))
      : el('p', { class: 'empty', id: 'dash-inviters-empty', text: t('funnel.none') })
  ]));

  return fragment;
}
