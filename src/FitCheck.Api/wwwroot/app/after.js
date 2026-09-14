// "After the tip": the picker in the post sheet. Under an "After the tip" label it lists the caller's last five looks as
// thumbnails with their score rings, one selectable, "Not a follow-up" first and picked by default; value() is the
// picked post id or null, and the post sheet sends it as beforePostId. The row stays hidden until the looks are in and
// disappears for good when there are none (a first look has nothing to improve on), so the sheet never shows an empty
// picker. Looks under review are left out: the server refuses them as a before.
import { state, t, api, el, scoreBadge, fmtNumber, intentLabel } from './core.js';

/** How many earlier looks are offered. Five is the last week or so for most people, and one row on a phone. */
const MAX_LOOKS = 5;

let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '.after-picker .after-row { display: flex; gap: 8px; overflow-x: auto; padding-block: 2px 12px; margin-inline: -2px; padding-inline: 2px; scrollbar-width: none; -webkit-overflow-scrolling: touch; }',
    '.after-picker .after-row::-webkit-scrollbar { display: none; }',
    '.after-picker .after-opt { flex: none; position: relative; display: flex; align-items: center; justify-content: center; min-inline-size: 44px; min-block-size: 44px; padding: 0; border: 1px solid var(--line); border-radius: var(--radius-sm); background: var(--surface-2); color: var(--ink-2); font: 500 14px/1.2 var(--font-body); cursor: pointer; }',
    '.after-picker .after-opt.none { padding-inline: 14px; block-size: 90px; text-align: center; max-inline-size: 120px; }',
    '.after-picker .after-opt.look { inline-size: 72px; block-size: 90px; overflow: visible; }',
    '.after-picker .after-opt.look img { inline-size: 100%; block-size: 100%; object-fit: cover; border-radius: inherit; display: block; }',
    '.after-picker .after-opt.look .score-badge { inline-size: 28px; block-size: 28px; border-width: 2px; font-size: 14px; gap: 0; inset-block-end: -6px; inset-inline-end: -6px; box-shadow: 0 4px 10px rgba(0, 0, 0, 0.35); }',
    '.after-picker .after-opt.look .score-badge small { font-size: 6px; }',
    '.after-picker .after-opt[aria-checked="true"] { border-color: var(--accent); box-shadow: 0 0 0 2px var(--accent); color: var(--ink); }',
    '.after-picker .after-opt.none[aria-checked="true"] { background: var(--accent-tint); font-weight: 700; }',
    '.after-picker .after-preview { min-block-size: 20px; font-size: 13px; font-weight: 600; color: var(--ink-3); unicode-bidi: plaintext; }',
    '.after-picker .after-preview.up { color: var(--ok); }'
  ].join('\n') }));
}

/**
 * afterPicker(result) → { node, value() }. result is the check being posted (its feedback.score previews the strip once a
 * look is picked); node goes into the post sheet under the caption field, value() is read when the caption is sent.
 *
 * The post sheet (views/check.js, openPostSheet) wires it like this:
 *   const after = afterPicker(result);                       // before the sheet's content is built
 *   ... el('div', { class: 'field' }, [caption ...]), after.node, productsField ...   // at the marker under the caption
 *   api('POST', '/api/posts', { checkId: result.id, caption: caption.value, products, beforePostId: after.value() });
 * value() is a post id (string) or null; the server answers 400 "error.before_invalid" when the id is not a visible look
 * of the caller's own, or is the look of this very check. The node starts hidden and never throws: signed out, on a
 * failed fetch, or with no earlier looks, value() stays null and the sheet looks as it always did.
 */
export function afterPicker(result) {
  let picked = null;
  const node = el('div', { class: 'field after-picker', id: 'after-picker', hidden: true });
  const value = () => picked;
  if (!state.me) return { node, value };
  ensureStyle();

  const after = result && result.feedback && typeof result.feedback.score === 'number' ? result.feedback.score : null;
  const row = el('div', { class: 'after-row', role: 'radiogroup', 'aria-label': t('after.title') });
  const preview = el('p', { class: 'after-preview', 'aria-live': 'polite' });
  const options = [];
  const paint = () => {
    for (const opt of options) opt.setAttribute('aria-checked', String((opt.dataset.post || null) === picked));
    const look = options.find((opt) => opt.dataset.post === picked);
    const before = look ? Number(look.dataset.score) : null;
    preview.textContent = look && after !== null ? t('after.strip', { a: fmtNumber(before), b: fmtNumber(after) }) : '';
    preview.classList.toggle('up', !!look && after !== null && after > before);
  };
  const option = (post) => {
    const opt = post
      ? el('button', { type: 'button', class: 'after-opt look', role: 'radio', 'data-post': post.id, 'data-score': String(post.score), 'aria-label': t('after.option', { intent: intentLabel(post.intent), score: fmtNumber(post.score) }) }, [
        el('img', { src: post.imageUrl, alt: '', loading: 'lazy', decoding: 'async' }),
        scoreBadge(post.score)
      ])
      : el('button', { type: 'button', class: 'after-opt none', role: 'radio', text: t('after.none') });
    opt.addEventListener('click', () => { picked = post ? post.id : null; paint(); });
    options.push(opt);
    return opt;
  };

  node.appendChild(el('span', { class: 'label', text: t('after.title') }));
  node.appendChild(row);
  node.appendChild(el('span', { class: 'hint', text: t('after.hint') }));
  node.appendChild(preview);
  row.appendChild(option(null));
  paint();

  api('GET', '/api/users/' + encodeURIComponent(state.me.handle) + '/posts?offset=0&limit=' + MAX_LOOKS)
    .then((page) => {
      const looks = ((page && page.items) || []).filter((post) => !post.hidden).slice(0, MAX_LOOKS);
      if (!looks.length) return;
      for (const post of looks) row.appendChild(option(post));
      paint();
      node.hidden = false;
    })
    .catch(() => { /* the picker is a nicety: the look posts without it */ });

  return { node, value };
}
