// The terms of use and the privacy policy as in-app pages, in both languages: a lede, up to ten headed sections, the
// version line and the way to the other page. The wording lives in the i18n files (legal.terms_N_title / legal.terms_N,
// legal.privacy_N_title / legal.privacy_N), written in each language rather than translated, so a section is added or
// dropped by adding or removing its keys: the page stops at the first missing title. Public, like the guidelines, so the
// signup form can link here. VERSION and DATED move together when the wording changes.
//
// OWNER: have a lawyer review the terms and the privacy policy before launch. The copy describes what the app actually
// does, in plain language; it is not legal advice, and the governing-law line in the terms is a placeholder.
import { register, t, el, hasMessage, setTopBar, intlLocale } from '../core.js';

const VERSION = '2';
const DATED = '2026-09-12';
const MAX_SECTIONS = 10;
// A calendar date, not a moment: formatted in UTC, so it does not slip to the day before west of Greenwich.
const dated = () => new Intl.DateTimeFormat(intlLocale(), { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(DATED + 'T00:00:00Z'));

// The guidelines' shape: the number in lilac at the start, a bold title, body text in the caption voice.
const CSS = `
.lg-intro { unicode-bidi: isolate; }
.lg-sections { counter-reset: section; padding: 0; margin: 0; list-style: none; }
.lg-sections > li { display: grid; grid-template-columns: 40px 1fr; gap: 12px; padding-block: 16px; border-block-end: 1px solid var(--line-soft); counter-increment: section; }
.lg-sections > li:first-child { padding-block-start: 4px; }
.lg-sections > li:last-child { border-block-end: 0; }
.lg-sections > li::before { content: counter(section); font-family: var(--font-display); font-weight: 800; font-size: 26px; line-height: 1; color: var(--accent); direction: ltr; font-variant-numeric: tabular-nums; }
.lg-sections h2 { display: block; font: 700 17px/1.25 var(--font-body); letter-spacing: 0; text-transform: none; color: var(--ink); }
.lg-sections h2::after { display: none; }
.lg-sections p { margin-block-start: 4px; font-size: 15px; line-height: 1.55; color: var(--ink-2); overflow-wrap: anywhere; }
.lg-foot { display: grid; gap: 8px; padding-block-start: 12px; border-block-start: 1px solid var(--line); font-size: 13px; color: var(--ink-3); }
.lg-foot .lg-links { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 16px; }
.lg-foot .btn-text { padding-block: 0; font-size: 14px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/** The sections that have a title, in order, up to MAX_SECTIONS: legal.<page>_1 … legal.<page>_10. */
function sections(page) {
  const items = [];
  for (let n = 1; n <= MAX_SECTIONS; n++) {
    const key = 'legal.' + page + '_' + n;
    if (!hasMessage(key + '_title')) break;
    items.push(el('li', {}, [el('div', {}, [el('h2', { text: t(key + '_title') }), el('p', { text: t(key) })])]));
  }
  return items;
}

for (const page of ['terms', 'privacy']) {
  const other = page === 'terms' ? 'privacy' : 'terms';
  register(page, async (root) => {
    ensureStyle();
    setTopBar({ back: '#/', title: t('legal.' + page + '_title') });
    root.appendChild(el('h1', { text: t('legal.' + page + '_title') }));
    root.appendChild(el('p', { class: 'lede lg-intro', text: t('legal.' + page + '_intro') }));
    root.appendChild(el('ol', { class: 'lg-sections', id: 'lg-' + page }, sections(page)));
    // Back goes where the top bar's arrow goes: the previous page when there is one, home otherwise.
    const back = (event) => { if (history.length > 1) { event.preventDefault(); history.back(); } };
    root.appendChild(el('footer', { class: 'lg-foot' }, [
      el('span', { id: 'lg-version', text: t('legal.version', { version: VERSION, date: dated() }) }),
      el('div', { class: 'lg-links' }, [
        el('a', { class: 'btn-text', id: 'lg-other', href: '#/' + other, text: t('legal.see_' + other) }),
        el('a', { class: 'btn-text', href: '#/guidelines', text: t('guidelines.link') }),
        el('a', { class: 'btn-text', href: '#/', text: t('common.back'), onclick: back })
      ])
    ]));
  });
}
