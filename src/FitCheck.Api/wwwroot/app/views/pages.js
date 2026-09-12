// Static pages: the community guidelines and what we keep. The intro, five rules as a numbered index (the rank in
// lilac, the way Explore's trending index counts), the privacy section, and a colophon with the version, the date and a
// way back. Public: no sign-in needed, so the signup form can link here.
import { register, t, el, setTopBar, intlLocale } from '../core.js';

const VERSION = '1';
const DATED = '2026-09-05';   // both move together when the rules change
// A calendar date, not a moment: formatted in UTC, so it does not slip to the day before west of Greenwich.
const dated = () => new Intl.DateTimeFormat(intlLocale(), { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(DATED + 'T00:00:00Z'));

const CSS = `
.g-intro { unicode-bidi: isolate; }   /* UI text in the UI's language: .lede's plaintext would read the Latin "OREVOSH" it opens with as LTR */
.g-rules { counter-reset: rule; }
.g-rules li { display: grid; grid-template-columns: 40px 1fr; gap: 12px; padding-block: 16px; border-block-end: 1px solid var(--line-soft); counter-increment: rule; }
.g-rules li:first-child { padding-block-start: 4px; }
.g-rules li:last-child { border-block-end: 0; }
.g-rules li::before { content: counter(rule); font-family: var(--font-display); font-weight: 800; font-size: 26px; line-height: 1; color: var(--accent); direction: ltr; font-variant-numeric: tabular-nums; }
.g-rules b { display: block; font-weight: 700; font-size: 17px; line-height: 1.25; color: var(--ink); }
.g-rules p { margin-block-start: 4px; font-size: 15px; line-height: 1.5; color: var(--ink-2); }
.g-keep > * + * { margin-block-start: 10px; }
.g-keep p { font-size: 15px; line-height: 1.5; color: var(--ink-2); }
.g-foot { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; padding-block-start: 12px; border-block-start: 1px solid var(--line); font-size: 13px; color: var(--ink-3); }
.g-foot .btn-text { padding-block: 0; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

register('guidelines', async (root) => {
  ensureStyle();
  setTopBar({ back: '#/', title: t('guidelines.title') });
  root.appendChild(el('h1', { text: t('guidelines.title') }));
  root.appendChild(el('p', { class: 'lede g-intro', text: t('guidelines.intro') }));
  root.appendChild(el('ol', { class: 'g-rules' }, [1, 2, 3, 4, 5].map((n) => el('li', {}, [
    el('div', {}, [el('b', { text: t('guidelines.rule' + n + '_title') }), el('p', { text: t('guidelines.rule' + n) })])
  ]))));
  root.appendChild(el('section', { class: 'g-keep' }, [
    el('h2', { text: t('guidelines.privacy_title') }),
    el('p', { text: t('guidelines.privacy') })
  ]));
  // Back goes where the top bar's arrow goes: the previous page when there is one, home otherwise.
  const back = (event) => { if (history.length > 1) { event.preventDefault(); history.back(); } };
  root.appendChild(el('footer', { class: 'g-foot' }, [
    el('span', { text: t('guidelines.version', { version: VERSION, date: dated() }) }),
    el('a', { class: 'btn-text', href: '#/', text: t('common.back'), onclick: back })
  ]));
});
