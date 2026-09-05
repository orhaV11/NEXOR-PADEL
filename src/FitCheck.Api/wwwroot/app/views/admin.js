// The moderation queue for handles in Admin:Handles: reported looks and comments, hide/show/delete, account suspension.
// The server is the gate (403 for everyone else); this screen only opens the door for people the /me call says are
// moderators. Every action reloads the queue: the list is what the server says is left to look at, nothing is kept here.
import {
  register, state, t, api, el, icon, avatar, brandMark, handleText, relative, fmtNumber, fmtCompact, setTopBar, signInPrompt, confirmSheet, toast,
  postCard, emptyState, errorBlock, skeletonCards, isMe
} from '../core.js';

// The rules the shared stylesheet does not have: the counters line, the queue item (a look card or a comment card with
// a moderation foot under it), the reason chips, 44px actions, and the account rows.
const CSS = `
.adm-stats { font-size: 13px; color: var(--ink-3); padding-inline: 2px; }
.adm-list { margin-inline: -16px; }   /* the look card carries its own 14px side margins, as in the feed */
.adm-item { margin-block-end: 20px; }
.adm-item .card { margin-block-end: 0; border-end-start-radius: 0; border-end-end-radius: 0; }
.adm-item .card .card-body .alert { display: none; }   /* the author's "under review" line; the foot says it in the moderator's words */
.adm-comment { background: var(--surface); border-radius: var(--radius) var(--radius) 0 0; margin-inline: 14px; padding: 12px 14px 4px; box-shadow: var(--shadow-card); }
.adm-comment .person { border-block-end: 0; padding-block: 0 8px; }
.adm-comment .body { font-size: 15px; line-height: 1.5; color: var(--ink-2); overflow-wrap: anywhere; white-space: pre-line; margin-block: 0 8px; }
.adm-foot { margin-inline: 14px; background: var(--surface-2); border-radius: 0 0 var(--radius) var(--radius); padding: 12px 14px 14px; display: grid; gap: 10px; }
.adm-meta { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; font-size: 12.5px; color: var(--ink-3); }
.adm-meta .tag { min-block-size: 22px; font-size: 10.5px; }
.adm-meta b { color: var(--ink); font-family: var(--font-display); font-weight: 700; font-size: 14px; }
.adm-reasons { display: flex; flex-wrap: wrap; gap: 6px; }
.adm-reason { display: inline-flex; align-items: center; min-block-size: 26px; padding-inline: 10px; border-radius: var(--pill); border: 1px solid var(--line); color: var(--ink-2); font-size: 12.5px; max-inline-size: 100%; }
.adm-reason bdi { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.adm-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.adm-actions .btn { min-block-size: 44px; flex: 1 1 auto; }
.adm-section > * + * { margin-block-start: 12px; }
.adm-users .person .btn-sm { min-block-size: 44px; padding-inline: 12px; }
.adm-users .who .name, .adm-users .who .sub { white-space: normal; }   /* the button is wide: wrap the name and the counts rather than cut them */
.adm-users .who .tag { margin-inline-start: 8px; min-block-size: 20px; font-size: 10.5px; vertical-align: middle; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/** The count a plural key wants: the number 1 (so the _one form fires) or the compact figure. */
const countArg = (n) => (n === 1 ? 1 : fmtCompact(n));
const userPath = (handle) => '/api/admin/users/' + encodeURIComponent(handle);

/** A moderator's button: outlined, red for the destructive ones, 44px tall. Disabled while its call is in flight. */
function actionButton(text, onclick, danger) {
  const btn = el('button', { type: 'button', class: 'btn btn-sm ' + (danger ? 'btn-danger' : 'btn-secondary'), text });
  btn.addEventListener('click', async () => {
    if (btn.disabled) return;
    btn.disabled = true;
    try { await onclick(); } finally { btn.disabled = false; }
  });
  return btn;
}

/** Suspend asks first (it locks a person out); lifting is the undo, so it does not. */
function suspendButton(handle, suspended, act) {
  if (suspended) return actionButton(t('admin.unsuspend'), () => act(() => api('POST', userPath(handle) + '/unsuspend')));
  return actionButton(t('admin.suspend'), async () => {
    if (!await confirmSheet(t('admin.suspend'), t('admin.confirm_suspend', { handle }), t('admin.suspend'), true)) return;
    await act(() => api('POST', userPath(handle) + '/suspend'));
  }, true);
}

/** A reported comment: the author row and the text, in the card's shape. */
function commentCard(c) {
  const user = c.user;
  return el('div', { class: 'adm-comment' }, [
    el('div', { class: 'person' }, [
      avatar(user),
      el('div', { class: 'who' }, [
        el('a', { class: 'name', href: '#/u/' + encodeURIComponent(user.handle), style: 'text-decoration:none' }, [user.name, brandMark(user)]),
        el('div', { class: 'sub' }, [handleText(user.handle), ' · ' + relative(c.createdAt)])
      ])
    ]),
    el('p', { class: 'body', text: c.text })
  ]);
}

/** The moderation foot: kind, count and when, the hidden/suspended tags, the reasons people gave, the actions. */
function itemFoot(item, actions) {
  const meta = el('div', { class: 'adm-meta' }, [
    el('span', { class: 'tag', text: t(item.kind === 'post' ? 'admin.post' : 'admin.comment') }),
    el('b', { text: t('admin.reports_n', { n: countArg(item.reports) }) }),
    el('span', { text: relative(item.lastReportedAt) }),
    item.hidden ? el('span', { class: 'tag rose', text: t('admin.hidden') }) : null,
    item.authorSuspended ? el('span', { class: 'tag rose', text: t('admin.suspended') }) : null
  ]);
  const reasons = item.reasons && item.reasons.length
    ? el('div', { class: 'adm-reasons', role: 'group', 'aria-label': t('admin.reasons') }, item.reasons.map((r) => el('span', { class: 'adm-reason', title: r }, [el('bdi', { text: r })])))
    : null;
  return el('div', { class: 'adm-foot' }, [meta, reasons, el('div', { class: 'adm-actions' }, actions)]);
}

/** One queue item: the look card (compact, opens the post) or the comment card, and the foot with its actions. */
function queueItem(item, act) {
  const base = (item.kind === 'post' ? '/api/admin/posts/' : '/api/admin/comments/') + encodeURIComponent(item.id);
  const author = item.author;
  const actions = [
    item.hidden
      ? actionButton(t('admin.unhide'), () => act(() => api('POST', base + '/unhide')))
      : actionButton(t('admin.hide'), () => act(() => api('POST', base + '/hide'))),
    actionButton(t('admin.delete'), async () => {
      if (!await confirmSheet(t('admin.delete'), t('admin.confirm_delete'), t('admin.delete'), true)) return;
      await act(() => api('DELETE', base));
    }, true),
    author && !isMe(author.handle) ? suspendButton(author.handle, item.authorSuspended, act) : null
  ];
  const body = item.kind === 'post' && item.post
    ? postCard(item.post, { compact: true })
    : commentCard(item.comment || { user: author || { handle: '?', name: '?' }, text: '', createdAt: item.lastReportedAt });
  return el('div', { class: 'adm-item', 'data-kind': item.kind, 'data-id': item.id }, [body, itemFoot(item, actions)]);
}

/** An account row: name, handle, looks and reports against them, and the suspend/lift button (never for yourself). */
function userRow(row, act) {
  const user = row.user;
  return el('div', { class: 'person', 'data-handle': user.handle }, [
    avatar(user),
    el('div', { class: 'who' }, [
      el('div', { class: 'name' }, [
        el('a', { href: '#/u/' + encodeURIComponent(user.handle), style: 'text-decoration:none; color: inherit;' }, [user.name, brandMark(user)]),
        row.suspended ? el('span', { class: 'tag rose', text: t('admin.suspended') }) : null
      ]),
      el('div', { class: 'sub' }, [handleText(user.handle), ' · ' + t('tag.looks', { n: countArg(row.posts) }) + ' · ' + t('admin.reports_n', { n: countArg(row.reports) })])
    ]),
    isMe(user.handle) ? null : suspendButton(user.handle, row.suspended, act)
  ]);
}

register('admin', async (root, params, ctx) => {
  setTopBar({ back: '#/settings', title: t('admin.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('admin.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (!state.me.isAdmin) {
    root.appendChild(el('div', { class: 'notice' }, [el('h3', { text: t('admin.title') }), el('p', { class: 'muted', text: t('admin.forbidden') })]));
    return;
  }
  ensureStyle();

  const stats = el('p', { class: 'adm-stats', id: 'adm-stats' });
  const list = el('div', { class: 'adm-list', id: 'adm-queue' }, [skeletonCards(2)]);
  const input = el('input', {
    type: 'search', id: 'adm-q', name: 'q', placeholder: t('admin.search_users'), 'aria-label': t('admin.search_users'),
    autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'search', maxlength: '40'
  });
  const hint = el('p', { class: 'hint', text: t('admin.users_hint') });
  const people = el('div', { id: 'adm-users' });
  root.appendChild(stats);
  root.appendChild(el('section', { class: 'adm-section' }, [el('h2', { text: t('admin.queue') }), list]));
  root.appendChild(el('section', { class: 'adm-section adm-users' }, [
    el('h2', { text: t('admin.users') }),
    el('form', { class: 'search', role: 'search', onsubmit: (event) => { event.preventDefault(); loadUsers(input.value); } }, [icon('search'), input]),
    hint,
    people
  ]));

  let query = '';
  async function loadQueue() {
    let data;
    try { data = await api('GET', '/api/admin/queue'); }
    catch (e) {
      if (ctx.stale()) return;
      // 403 means the list changed under a signed-in session: say what the server said, without the retry.
      list.replaceChildren(e.status === 403 ? el('div', { class: 'notice' }, [el('p', { text: e.message })]) : errorBlock(e));
      return;
    }
    if (ctx.stale()) return;
    stats.textContent = t('admin.stats', { hidden: fmtNumber(data.hiddenPosts), comments: fmtNumber(data.hiddenComments), suspended: fmtNumber(data.suspendedUsers) });
    const items = data.items || [];
    list.replaceChildren(...(items.length ? items.map((item) => queueItem(item, act)) : [emptyState(t('admin.empty'))]));
  }
  async function loadUsers(q) {
    query = (q || '').trim().replace(/^@/, '');
    let rows;
    try { rows = await api('GET', '/api/admin/users?q=' + encodeURIComponent(query)); }
    catch (e) { if (ctx.stale()) return; people.replaceChildren(el('p', { class: 'alert danger', text: e.message })); return; }
    if (ctx.stale()) return;
    hint.hidden = !!query;
    rows = rows || [];
    people.replaceChildren(...(rows.length ? rows.map((row) => userRow(row, act)) : [el('p', { class: 'muted', text: t('admin.no_users') })]));
  }
  /** Runs one moderation call, then reloads both lists: what is left to look at is the server's word, never a local guess. */
  async function act(call) {
    try { await call(); } catch (e) { toast(e.message); return; }
    if (ctx.stale()) return;
    toast(t('admin.done'));
    await Promise.all([loadQueue(), loadUsers(query)]);
  }

  await Promise.all([loadQueue(), loadUsers('')]);
});
