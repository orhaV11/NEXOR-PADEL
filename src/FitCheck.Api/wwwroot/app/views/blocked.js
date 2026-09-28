// Blocked accounts (#/settings/blocked, Round 11): the list from GET /api/users/me/blocks, one row per account
// (#blocked-list li[data-handle]) with its Unblock (button.unblock → DELETE /api/users/{handle}/block), and the empty
// state (#blocked-empty). The two moves every other screen shares live here too: blockAccount (the confirm sheet with
// block.confirm_title, POST /api/users/{handle}/block, the toast block.done) from the profile's "…" and the look's "…",
// and unblockAccount from the profile's "…" and this list. Both raise "orevosh:block" on the document so an open look
// page can leave and an open feed fetches again at once, and both bump feedVersion so every remembered feed is stale on
// its next visit; nothing here says who blocked whom to anyone else.
import {
  register, state, t, api, el, avatar, brandMark, handleText, setTopBar, signInPrompt, emptyState, errorBlock, confirmSheet,
  requireSignIn, toast, feedVersion
} from '../core.js';

const CSS = `
.blocked-list { list-style: none; margin: 0; padding: 0; }
.blocked-list .person .who { flex: 1; min-inline-size: 0; }
.blocked-list .person .name { color: var(--ink); font-weight: 700; text-decoration: none; }
.blocked-list .unblock { flex: none; min-block-size: 44px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/**
 * Tells the open views a block was added (blocked: true) or removed; cached feeds are stale either way. The bump comes
 * first, so a view that acts on the event reads the new version and stores that with whatever it fetches.
 */
function announceBlock(handle, blocked) {
  feedVersion.n += 1;
  document.dispatchEvent(new CustomEvent('orevosh:block', { detail: { handle, blocked } }));
}

/**
 * Block a person from anywhere their name shows: the confirm sheet (their name in the title, the danger button), then
 * POST. Resolves true once the block stands; a 409 is the state that was asked for, so it counts as done.
 */
export async function blockAccount(user) {
  if (!requireSignIn()) return false;
  const ok = await confirmSheet(t('block.confirm_title', { name: user.name || user.handle }), t('block.confirm_body'), t('block.block'), true);
  if (!ok) return false;
  try {
    await api('POST', '/api/users/' + encodeURIComponent(user.handle) + '/block');
  } catch (e) {
    if (e.status !== 409) { toast(e.message); return false; }
  }
  toast(t('block.done'));
  announceBlock(user.handle, true);
  return true;
}

/** Undo a block. No confirm: it is reversible, and the follow does not come back either way. A 404 is already "not blocked". */
export async function unblockAccount(handle) {
  if (!requireSignIn()) return false;
  try {
    await api('DELETE', '/api/users/' + encodeURIComponent(handle) + '/block');
  } catch (e) {
    if (e.status !== 404) { toast(e.message); return false; }
  }
  toast(t('block.undone'));
  announceBlock(handle, false);
  return true;
}

/** One blocked account: portrait, name, handle and its Unblock. The row leaves the list when the block does. */
function blockedRow(item, onChange) {
  const user = item.user;
  const unblock = el('button', { type: 'button', class: 'btn btn-sm btn-secondary unblock', text: t('block.unblock') });
  const row = el('li', { class: 'person', 'data-handle': user.handle }, [
    avatar(user),
    el('div', { class: 'who' }, [
      el('a', { class: 'name', href: '#/u/' + encodeURIComponent(user.handle) }, [user.name, brandMark(user)]),
      el('div', { class: 'sub' }, [handleText(user.handle)])
    ]),
    unblock
  ]);
  unblock.addEventListener('click', async () => {
    if (unblock.disabled) return;
    unblock.disabled = true;
    if (await unblockAccount(user.handle)) { row.remove(); onChange(); } else unblock.disabled = false;
  });
  return row;
}

register('settings-blocked', async (root, params, ctx) => {
  setTopBar({ back: '#/settings', title: t('block.blocked_title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('block.blocked_title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  ensureStyle();
  const skel = el('div', { 'aria-hidden': 'true' }, [0, 1].map(() => el('div', { class: 'person' }, [
    el('div', { class: 'skel', style: 'inline-size: 44px; block-size: 44px; border-radius: 50%; flex: none;' }),
    el('div', { class: 'skel', style: 'flex: 1; block-size: 16px;' })
  ])));
  root.appendChild(skel);

  let data;
  try { data = await api('GET', '/api/users/me/blocks'); }
  catch (e) { if (ctx.stale()) return; skel.remove(); root.appendChild(errorBlock(e)); return; }
  if (ctx.stale()) return;
  skel.remove();

  const items = data && Array.isArray(data.items) ? data.items : [];
  const list = el('ul', { class: 'blocked-list', id: 'blocked-list' });
  const empty = emptyState(t('block.blocked_empty'));
  empty.id = 'blocked-empty';
  const paint = () => { empty.hidden = list.children.length > 0; };
  for (const item of items) list.appendChild(blockedRow(item, paint));
  root.appendChild(list);
  root.appendChild(empty);
  paint();
});
