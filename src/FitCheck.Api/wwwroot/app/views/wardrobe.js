// Your wardrobe (#/wardrobe): the pieces you kept, one tap at a time, from the checks that named them. A plain list —
// a name, what it is, and the looks it has been in — with a rename and a delete behind each row. Nothing here asks for
// a photo: the wardrobe is built by app/wardrobe.js on the result screen and read here.
//
// What it is FOR is the line at the top and the switch under it: with these names in front of it, the stylist can say
// "swap the black tights for the brown ones you wore on the 4th" instead of "buy sheer brown tights". On a server where
// that is Pro's (plans.wardrobe on /api/config) a free account still sees its whole wardrobe and is told plainly what
// Pro adds — the list has to build itself before it is worth anything, and a wall would stop it.
import { register, state, t, el, api, icon, setTopBar, signInPrompt, emptyState, errorBlock, toast, sheet, closeSheet, confirmSheet, fmtDate, relative, hasMessage } from '../core.js';
import { loadWardrobe, forgetWardrobe } from '../wardrobe.js';

let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '.wr-lede { margin-block-end: 4px; }',
    '.wr-switch { display: flex; align-items: center; gap: 12px; padding: 14px 16px; background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); }',
    '.wr-switch .wr-switch-text { flex: 1; min-inline-size: 0; }',
    '.wr-switch b { display: block; font-weight: 700; font-size: 16px; color: var(--ink); }',
    '.wr-switch p { margin-block-start: 3px; font-size: 14px; line-height: 1.45; color: var(--ink-2); }',
    '.wr-switch input { inline-size: 44px; block-size: 44px; flex: none; accent-color: var(--accent); }',
    '.wr-list { list-style: none; margin: 0; padding: 0; }',
    '.wr-item { display: flex; align-items: center; gap: 12px; min-block-size: 44px; padding-block: 14px; border-block-end: 1px solid var(--line-soft); }',
    '.wr-item:last-child { border-block-end: 0; }',
    '.wr-item .wr-dot { flex: none; inline-size: 34px; block-size: 34px; border-radius: 50%; display: grid; place-items: center; background: var(--surface-2); color: var(--ink-3); }',
    '.wr-item .wr-dot svg { inline-size: 17px; block-size: 17px; }',
    '.wr-item .wr-body { flex: 1; min-inline-size: 0; }',
    '.wr-item .wr-name { font-weight: 600; font-size: 16px; line-height: 1.3; color: var(--ink); unicode-bidi: plaintext; overflow-wrap: anywhere; }',
    '.wr-item .wr-meta { margin-block-start: 3px; font-size: 13px; color: var(--ink-3); }',
    '.wr-count { font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase; color: var(--ink-3); }',
    '.wr-looks { list-style: none; margin: 0; padding: 0; }',
    '.wr-looks li { border-block-end: 1px solid var(--line-soft); }',
    '.wr-looks li:last-child { border-block-end: 0; }',
    '.wr-looks a, .wr-looks span { display: flex; align-items: center; gap: 10px; min-block-size: 44px; font-size: 15px; color: var(--ink-2); text-decoration: none; }',
    '.wr-looks a { color: var(--accent); font-weight: 600; }',
    '.wr-pro { text-align: start; }'
  ].join('\n') }));
}

/** "shoes", "outerwear"… in the reader's language; an unknown word falls back to the plain one from the server. */
const categoryLabel = (category) => (hasMessage('wardrobe.category_' + category) ? t('wardrobe.category_' + category) : category);

/** One piece: its name, what it is, and how many looks it has been in, newest first. */
function itemRow(item, onChanged) {
  const looks = item.looks || [];
  const newest = looks.length > 0 ? looks[0].wornAt : item.lastSeenAt;
  const row = el('li', { class: 'wr-item', 'data-item': item.id }, [
    el('span', { class: 'wr-dot', 'aria-hidden': 'true' }, [icon('bag')]),
    el('div', { class: 'wr-body' }, [
      el('div', { class: 'wr-name', dir: 'auto', text: item.name }),
      el('div', { class: 'wr-meta', text: t('wardrobe.looks', { n: looks.length }) + (newest ? ' · ' + relative(newest) : '') })
    ]),
    el('button', {
      type: 'button', class: 'icon-btn', 'aria-label': t('wardrobe.manage', { piece: item.name }),
      onclick: () => openItemSheet(item, onChanged)
    }, [icon('more')])
  ]);
  return row;
}

/** The sheet behind a piece: the looks it appeared in, a rename and a delete. */
function openItemSheet(item, onChanged) {
  const looks = item.looks || [];
  const body = el('div', { class: 'stack' }, [
    el('p', { class: 'hint', text: t('wardrobe.category') + ': ' + categoryLabel(item.category) }),
    el('span', { class: 'wr-count', text: t('wardrobe.looks', { n: looks.length }) }),
    el('ul', { class: 'wr-looks' }, looks.slice(0, 10).map((look) => el('li', {}, [
      look.postId
        ? el('a', { href: '#/post/' + encodeURIComponent(look.postId), onclick: () => closeSheet() }, [icon('image'), fmtDate(look.wornAt)])
        : el('span', {}, [icon('camera'), fmtDate(look.wornAt)])
    ]))),
    el('button', { type: 'button', class: 'btn btn-secondary', id: 'wardrobe-rename', text: t('wardrobe.rename'), onclick: () => openRenameSheet(item, onChanged) }),
    el('button', { type: 'button', class: 'btn btn-danger', id: 'wardrobe-delete', text: t('wardrobe.delete'), onclick: async () => {
      if (!await confirmSheet(t('wardrobe.delete_title', { piece: item.name }), t('wardrobe.delete_body'), t('wardrobe.delete'), true)) return;
      try {
        await api('DELETE', '/api/wardrobe/' + encodeURIComponent(item.id));
        forgetWardrobe();
        closeSheet();
        toast(t('wardrobe.deleted'));
        onChanged();
      } catch (e) {
        toast((e && e.message) || t('error.generic'));
      }
    } })
  ]);
  sheet({ title: item.name, content: body });
}

/** Rename: one field, the person's own word for the piece. */
function openRenameSheet(item, onChanged) {
  const input = el('input', { type: 'text', id: 'wardrobe-name', maxlength: '60', autocomplete: 'off', enterkeyhint: 'done', value: item.name, dir: 'auto' });
  const save = el('button', { type: 'submit', class: 'btn', id: 'wardrobe-rename-save', text: t('common.save') });
  const form = el('form', { class: 'stack', novalidate: true, onsubmit: async (event) => {
    event.preventDefault();
    if (save.disabled) return;
    save.disabled = true;
    try {
      await api('PATCH', '/api/wardrobe/' + encodeURIComponent(item.id), { name: input.value });
      forgetWardrobe();
      closeSheet();
      onChanged();
    } catch (e) {
      toast((e && e.message) || t('error.generic'));
      save.disabled = false;
    }
  } }, [
    el('label', { class: 'field' }, [el('span', { class: 'label', text: t('wardrobe.name') }), input]),
    save
  ]);
  sheet({ title: t('wardrobe.rename'), content: form });
  setTimeout(() => input.focus({ preventScroll: true }), 60);
}

/**
 * The switch that sends the names with your checks, or — on a plan that does not have it — the one line that says what
 * Pro adds and the way there. Never a dead toggle: a free account is told, not teased.
 */
function stylistRow(data, onChanged) {
  if (!data.stylistAvailable) {
    return el('div', { class: 'notice wr-pro', id: 'wardrobe-pro' }, [
      el('p', { text: t('wardrobe.pro_body') }),
      el('a', { class: 'btn-text', id: 'wardrobe-pro-link', href: '#/pro', text: t('wardrobe.pro_link') })
    ]);
  }

  const input = el('input', { type: 'checkbox', id: 'wardrobe-stylist', checked: data.toStylist });
  input.addEventListener('change', async () => {
    const on = input.checked;
    input.disabled = true;
    try {
      await api('POST', '/api/wardrobe/stylist', { on });
      forgetWardrobe();
      onChanged();
    } catch (e) {
      input.checked = !on;
      toast((e && e.message) || t('error.generic'));
    } finally {
      input.disabled = false;
    }
  });
  return el('label', { class: 'wr-switch', id: 'wardrobe-switch' }, [
    el('span', { class: 'wr-switch-text' }, [
      el('b', { text: t('wardrobe.stylist_title') }),
      el('p', { text: t('wardrobe.stylist_hint') })
    ]),
    input
  ]);
}

register('wardrobe', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: '#/me', title: t('wardrobe.title') });
  if (!state.me) { root.appendChild(signInPrompt()); return; }

  const body = el('div', { class: 'stack', id: 'wardrobe' });
  root.appendChild(body);

  const paint = async (force) => {
    const data = await loadWardrobe(force);
    if (ctx.stale()) return;
    body.replaceChildren();
    if (!data) { body.appendChild(errorBlock(new Error(t('error.generic')))); return; }
    body.appendChild(el('p', { class: 'lede wr-lede', text: t('wardrobe.lede') }));
    body.appendChild(stylistRow(data, () => paint(true)));
    const items = data.items || [];
    if (items.length === 0) {
      body.appendChild(emptyState(t('wardrobe.empty_title'), t('wardrobe.empty_body')));
      body.appendChild(el('a', { class: 'btn', href: '#/check', text: t('wardrobe.empty_go') }));
      return;
    }

    body.appendChild(el('span', { class: 'wr-count', id: 'wardrobe-count', text: t('wardrobe.count', { n: items.length, max: data.max }) }));
    body.appendChild(el('ul', { class: 'wr-list' }, items.map((item) => itemRow(item, () => paint(true)))));
  };

  await paint(true);
});
