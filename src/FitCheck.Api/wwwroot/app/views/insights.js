// What your checks say about you (#/insights): three tiles (how many checks, the average and the best score, the two
// scores in the score ring), the sentences the server wrote in your language as a short list, and the way to the next
// check. Under three checks the page is an invitation with a progress line instead. Private like the checks: signed
// out it is a sign-in prompt, and where the server keeps comparisons for Pro (plans.compareNeedsPro on /api/config)
// the insights are Pro's too, so a free account sees the same Pro card as #/compare. The numbers come ready from
// /api/users/me/insights; nothing is computed here.
import { register, state, t, api, el, setTopBar, signInPrompt, emptyState, scoreBadge, fmtNumber } from '../core.js';

/** Checks needed before the reading shows; the server sends no lines under it. */
const MIN_CHECKS = 3;

const isPro = () => !!state.me && state.me.plan === 'pro' && (!state.me.proUntil || new Date(state.me.proUntil) > new Date());
/** The server answers 403 to a free account exactly when it does so for comparisons; the card is drawn before asking. */
const needsPro = () => !!(state.config.plans && state.config.plans.compareNeedsPro) && !isPro();

// The profile's link to this page is drawn by profile.js; its rule lives here with the rest of the insights look, at
// module load so the profile never renders it unstyled.
document.head.appendChild(el('style', { text: [
  '.insights-link { display: flex; align-items: center; gap: 10px; min-block-size: 44px; margin-block-start: -6px; color: var(--accent); font-weight: 600; font-size: 15px; text-decoration: none; }',
  '.insights-link svg { inline-size: 20px; block-size: 20px; }'
].join('\n') }));

let styled = false;
/** The tiles and the sentence list: app.css has the ring, the caps label voice and the surfaces; the layout is this view's. */
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '.insights-tiles { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; list-style: none; margin: 0; padding: 0; }',
    '.insights-tiles li { display: flex; flex-direction: column; align-items: center; justify-content: flex-end; gap: 12px; min-block-size: 116px; padding: 16px 6px 14px; background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); }',
    '.insights-tiles .score-badge { position: static; box-shadow: none; inline-size: 58px; block-size: 58px; font-size: 24px; }',
    '.insights-tiles .score-badge small { font-size: 8px; }',
    '.insights-tiles .plain { display: inline-flex; align-items: center; justify-content: center; inline-size: 58px; block-size: 58px; font-family: var(--font-display); font-weight: 800; font-size: 30px; line-height: 1; color: var(--ink); direction: ltr; }',
    '.insights-tiles .lbl { font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); text-align: center; }',
    '[dir="rtl"] .insights-tiles .lbl { font-size: 12.5px; }',
    '.insights-lines { list-style: none; margin: 0; padding: 0; }',
    '.insights-lines li { display: flex; align-items: flex-start; gap: 12px; padding-block: 14px; border-block-end: 1px solid var(--line-soft); font-size: 16px; line-height: 1.45; color: var(--ink); }',
    '.insights-lines li:last-child { border-block-end: 0; }',
    '.insights-lines .dot { flex: none; inline-size: 6px; block-size: 6px; border-radius: 50%; background: var(--grad); margin-block-start: 9px; }',
    '.insights-skel { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }',
    '.insights-skel .skel { block-size: 116px; border-radius: var(--radius); }',
    '.insights-foot { text-align: center; }'
  ].join('\n') }));
}

/** One tile: the figure, a caps label, and what a screen reader hears when the figure is a bare ring. */
function tile(id, label, figure, spoken) {
  return el('li', { 'data-tile': id }, [figure, el('span', { class: 'lbl', text: label }), spoken ? el('span', { class: 'sr-only', text: spoken }) : null]);
}

/** The score ring as a figure in flow: the tile's own CSS takes it out of its corner. */
function ring(score) {
  return scoreBadge(score);
}

function tiles(data) {
  const avg = fmtNumber(data.avgScore);
  const best = fmtNumber(data.bestScore);
  return el('ul', { class: 'insights-tiles', 'aria-label': t('insights.title') }, [
    tile('checks', t('insights.checks'), el('span', { class: 'plain', text: fmtNumber(data.checks) })),
    tile('average', t('insights.average'), ring(data.avgScore), t('a11y.score', { score: avg })),
    tile('best', t('insights.best'), ring(data.bestScore), t('a11y.score', { score: best }))
  ]);
}

/** The sentences, one per row with a gradient dot; each is text from the server in the caller's language. */
function lineList(lines) {
  return el('ul', { class: 'insights-lines' }, lines.map((line) => el('li', {}, [
    el('span', { class: 'dot', 'aria-hidden': 'true' }),
    el('span', { dir: 'auto', text: line })
  ])));
}

const checkAnother = () => el('a', { class: 'btn', id: 'insights-again', href: '#/check', text: t('insights.again') });

register('insights', async (root, params, ctx) => {
  setTopBar({ back: '#/me', title: t('insights.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('insights.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (needsPro()) {
    root.appendChild(el('div', { class: 'notice', id: 'insights-pro' }, [
      el('h3', { text: t('insights.pro_title') }),
      el('p', { class: 'muted', text: t('insights.pro_body') }),
      el('a', { class: 'btn', href: '#/pro', style: 'margin-block-start: 14px;', text: t('insights.go_pro') })
    ]));
    return;
  }
  ensureStyle();
  const skel = el('div', { class: 'insights-skel', 'aria-hidden': 'true' }, [0, 1, 2].map(() => el('div', { class: 'skel' })));
  root.appendChild(skel);

  let data;
  try { data = await api('GET', '/api/users/me/insights'); }
  catch (e) { if (ctx.stale()) return; skel.remove(); throw e; }
  if (ctx.stale()) return;
  skel.remove();

  const lines = data.lines || [];
  if (data.checks < MIN_CHECKS || !lines.length) {
    root.appendChild(el('div', { class: 'stack', id: 'insights-empty' }, [
      emptyState(t('insights.empty'), t('insights.progress', { n: fmtNumber(data.checks) })),
      checkAnother()
    ]));
    return;
  }

  root.appendChild(el('div', { class: 'stack', id: 'insights-body' }, [
    tiles(data),
    el('section', {}, [el('h2', { text: t('insights.lines') }), lineList(lines)]),
    el('p', { class: 'hint insights-foot', text: t('insights.based_on', { n: fmtNumber(data.checks) }) }),
    checkAnother()
  ]));
});
