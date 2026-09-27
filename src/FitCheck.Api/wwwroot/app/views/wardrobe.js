// Your wardrobe (#/wardrobe): the pieces you kept, one tap at a time, from the checks that named them. A plain list —
// a name, what it is, and the looks it has been in — with a rename and a delete behind each row. Nothing here asks for
// a photo: the wardrobe is built by app/wardrobe.js on the result screen and read here.
//
// What it is FOR is the line at the top and the switch under it: with these names in front of it, the stylist can say
// "swap the black tights for the brown ones you wore on the 4th" instead of "buy sheer brown tights". On a server where
// that is Pro's (plans.wardrobe on /api/config) a free account still sees its whole wardrobe and is told plainly what
// Pro adds — the list has to build itself before it is worth anything, and a wall would stop it.
//
// Round 20 — filling it faster. Two things join the screen: the Pro moment (once per session, when the server says a free
// wardrobe has passed what its stylist sees: one numbered line and Go Pro, in place of the plain Pro notice for that
// paint), and "Keep from an older look" — the pieces the stylist named on the person's latest looks that were never kept
// (GET /api/wardrobe/unkept), each with a Keep that goes through the same POST /api/wardrobe as the keep row does.
import { register, state, t, el, api, icon, setTopBar, signInPrompt, emptyState, errorBlock, toast, sheet, closeSheet, confirmSheet, fmtDate, relative, hasMessage } from '../core.js';
import { loadWardrobe, forgetWardrobe, momentUnseen, momentNotice } from '../wardrobe.js';

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
    // Round 19: the photo of the look, posted or not, now that a private check has a photo route of its own.
    '.wr-looks .wr-thumb { flex: none; inline-size: 56px; block-size: 56px; border-radius: var(--radius-sm); overflow: hidden; background: var(--surface-2); display: grid; place-items: center; color: var(--ink-3); }',
    '.wr-looks .wr-thumb img { inline-size: 100%; block-size: 100%; object-fit: cover; display: block; }',
    '.wr-tomorrow { display: flex; align-items: center; justify-content: center; gap: 8px; }',
    '.wr-pro { text-align: start; }',
    // Round 20: the Pro moment and the unkept list. The moment is one sentence and one button on a line; an unkept row
    // carries the look's photo where the wardrobe row carries a mark, and its Keep on the end.
    '.wr-moment { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; text-align: start; }',
    '.wr-moment p { flex: 1; min-inline-size: 14ch; margin: 0; unicode-bidi: plaintext; }',
    '.wr-unkept h2 { margin-block-end: 2px; }',
    '.wr-unkept .hint { margin-block-end: 6px; }',
    '.wr-unkept .wr-thumb { flex: none; inline-size: 44px; block-size: 44px; border-radius: var(--radius-sm); overflow: hidden; background: var(--surface-2); display: grid; place-items: center; color: var(--ink-3); }',
    '.wr-unkept .wr-thumb img { inline-size: 100%; block-size: 100%; object-fit: cover; display: block; }',
    '.wr-unkept .wr-thumb svg { inline-size: 17px; block-size: 17px; }'
  ].join('\n') }));
}

/** The photo of one look, or the camera mark when the file is gone: never a broken image. */
function lookThumb(look) {
  const frame = el('span', { class: 'wr-thumb', 'aria-hidden': 'true' }, [icon('camera')]);
  if (!look.checkId) return frame;
  // In the page from the start: a lazily loaded image that is not in the document never loads at all.
  const img = el('img', { src: '/api/checks/' + encodeURIComponent(look.checkId) + '/image', alt: '', loading: 'lazy', decoding: 'async' });
  img.addEventListener('error', () => frame.replaceChildren(icon('camera')));
  frame.replaceChildren(img);
  return frame;
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
        ? el('a', { href: '#/post/' + encodeURIComponent(look.postId), onclick: () => closeSheet() }, [lookThumb(look), fmtDate(look.wornAt)])
        : el('span', {}, [lookThumb(look), fmtDate(look.wornAt)])
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
      // Round 20: the Pro page counts where it was opened from (POST /api/funnel/pro-opened), so the funnel can say how many came from here.
      el('a', { class: 'btn-text', id: 'wardrobe-pro-link', href: '#/pro?from=wardrobe', text: t('wardrobe.pro_link') })
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

/**
 * Round 20 — "Keep from an older look": the pieces the stylist named on the person's last looks (Plans:WardrobeUnkeptChecks
 * of them) that never made it into the wardrobe, newest first, each with the photo of the look and a Keep. Keeping goes
 * through the same POST /api/wardrobe as the keep row, with the check that named the piece, so the server's rule that
 * a piece comes from a check that named it holds unchanged. Null when there is nothing to offer.
 */
function unkeptSection(unkept, onChanged) {
  const pieces = (unkept && unkept.pieces) || [];
  if (pieces.length === 0) return null;
  const rows = pieces.map((piece) => {
    const keep = el('button', { type: 'button', class: 'btn btn-sm wardrobe-unkept-keep', text: t('wardrobe.keep_yes'), 'aria-label': t('wardrobe.unkept_keep', { piece: piece.name }) });
    keep.addEventListener('click', async () => {
      if (keep.disabled) return;
      keep.disabled = true;
      try {
        await api('POST', '/api/wardrobe', { checkId: piece.checkId, name: piece.name });
        forgetWardrobe();
        onChanged();
      } catch (e) {
        // The server's own sentence, the wardrobe-full one included; the row stays so the person can decide again.
        toast((e && e.message) || t('error.generic'));
        keep.disabled = false;
      }
    });
    return el('li', { class: 'wr-item', 'data-check': piece.checkId }, [
      lookThumb({ checkId: piece.checkId }),
      el('div', { class: 'wr-body' }, [
        el('div', { class: 'wr-name', dir: 'auto', text: piece.name }),
        el('div', { class: 'wr-meta', text: categoryLabel(piece.category) + (piece.wornAt ? ' · ' + relative(piece.wornAt) : '') })
      ]),
      keep
    ]);
  });
  return el('section', { class: 'wr-unkept', id: 'wardrobe-unkept' }, [
    el('h2', { class: 'rule', text: t('wardrobe.unkept_title') }),
    el('p', { class: 'hint', text: t('wardrobe.unkept_hint', { n: unkept.checks }) }),
    el('ul', { class: 'wr-list' }, rows)
  ]);
}

register('wardrobe', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: '#/me', title: t('wardrobe.title') });
  // Round 19: the heading a screen reader lands on (the top bar carries the visible title), as the check screen has.
  root.appendChild(el('h1', { class: 'sr-only', text: t('wardrobe.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }

  const body = el('div', { class: 'stack', id: 'wardrobe' });
  root.appendChild(body);

  const paint = async (force) => {
    // Round 20: the unkept list is read beside the wardrobe, never instead of it — a failure there hides the section.
    const [data, unkept] = await Promise.all([loadWardrobe(force), api('GET', '/api/wardrobe/unkept').catch(() => null)]);
    if (ctx.stale()) return;
    body.replaceChildren();
    if (!data) { body.appendChild(errorBlock(new Error(t('error.generic')))); return; }
    body.appendChild(el('p', { class: 'lede wr-lede', text: t('wardrobe.lede') }));
    // Round 20 — the Pro moment: the numbered, once-per-session version of the Pro notice, so the screen never says it
    // twice on one paint. The truth is the server's (proMoment); this tab only remembers having shown it.
    const moment = data.proMoment && momentUnseen();
    if (moment) body.appendChild(momentNotice(data, { box: 'wardrobe-moment', go: 'wardrobe-moment-go' }));
    if (!moment || data.stylistAvailable) body.appendChild(stylistRow(data, () => paint(true)));
    const items = data.items || [];
    const older = unkeptSection(unkept, () => paint(true));
    if (items.length === 0) {
      body.appendChild(emptyState(t('wardrobe.empty_title'), t('wardrobe.empty_body')));
      // Keeping from an older look is the better first step than another check, so it comes before the button.
      if (older) body.appendChild(older);
      body.appendChild(el('a', { class: 'btn', href: '#/check', text: t('wardrobe.empty_go') }));
      return;
    }

    // Round 19 — Tomorrow: the person looking at their pieces is one tap from using them. Two kinds of piece make an
    // outfit; below that the line says how far the closet is from its first one.
    const plans = (state.config && state.config.plans) || {};
    if (plans.tomorrow) {
      const kinds = new Set(items.map((item) => item.category).filter((c) => c && c !== 'other')).size;
      const needKinds = plans.suggestionMinCategories || 2;
      body.appendChild(kinds >= needKinds
        ? el('a', { class: 'btn wr-tomorrow', id: 'wardrobe-tomorrow', href: '#/tomorrow' }, [icon('calendar'), t('tomorrow.open')])
        : el('p', { class: 'hint', id: 'wardrobe-tomorrow-progress', text: t('tomorrow.progress', { n: kinds, of: needKinds }) }));
    }

    body.appendChild(el('span', { class: 'wr-count', id: 'wardrobe-count', text: t('wardrobe.count', { n: items.length, max: data.max }) }));
    body.appendChild(el('ul', { class: 'wr-list' }, items.map((item) => itemRow(item, () => paint(true)))));
    if (older) body.appendChild(older);
  };

  await paint(true);
});
