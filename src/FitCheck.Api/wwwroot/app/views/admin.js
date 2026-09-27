// The moderation queue for handles in Admin:Handles: reported looks and comments, hide/show/delete, account suspension.
// The server is the gate (403 for everyone else); this screen only opens the door for people the /me call says are
// moderators. Every action reloads the queue: the list is what the server says is left to look at, nothing is kept here.
// Round 20 - owner tooling without a terminal: the Accounts section grew the flags a moderator acts on (verified, Pro,
// off the board, moderator) and their buttons, which do what --verify and --pro do from the box; and a read-only card
// says what the Board:Sponsor settings came to. The commands stay as the terminal fallback.
import {
  register, state, t, hasMessage, api, el, icon, avatar, brandMark, handleText, relative, fmtNumber, fmtCompact, fmtDate, setTopBar, signInPrompt, sheet, confirmSheet, toast,
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
.adm-users .who .name, .adm-users .who .sub { white-space: normal; }   /* several tags may follow the name: wrap rather than cut */
.adm-users .who .tag { margin-inline-start: 6px; min-block-size: 20px; font-size: 10.5px; vertical-align: middle; }
.adm-account { background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); padding: 4px 14px 14px; }
.adm-account .person { border-block-end: 0; }
.adm-account .adm-actions .btn { flex: 1 1 45%; }
.adm-sponsor { background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow-card); padding: 14px; display: grid; gap: 10px; }
.adm-sponsor .name { font-family: var(--font-display); font-weight: 700; font-size: 18px; }
.adm-sponsor a { display: inline-flex; align-items: center; gap: 6px; color: var(--accent); }
.adm-months { display: flex; flex-direction: column; gap: 8px; margin-block-start: 12px; }
.adm-months select { min-block-size: 44px; font: 500 16px/1 var(--font-body); padding-inline: 12px; border-radius: var(--radius-sm); border: 1px solid var(--line); background: var(--surface); color: var(--ink); }
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
/** A reason as the app sends it (a report.<key> string) reads in the moderator's language; anything else (older reports, the API) is shown as given. */
const reasonText = (r) => (/^[a-z_]+$/.test(r) && hasMessage('report.' + r) ? t('report.' + r) : r);

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
    ? el('div', { class: 'adm-reasons', role: 'group', 'aria-label': t('admin.reasons') }, item.reasons.map((r) => { const text = reasonText(r); return el('span', { class: 'adm-reason', title: text }, [el('bdi', { text })]); }))
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

/** Verify asks nothing (it is not punitive and the undo is one tap); the server names the moderator in its log line either way. */
function verifyButton(handle, verified, act) {
  return verified
    ? actionButton(t('admin.unverify'), () => act(() => api('POST', userPath(handle) + '/unverify')))
    : actionButton(t('admin.verify'), () => act(() => api('POST', userPath(handle) + '/verify')));
}

/**
 * Grant Pro opens a sheet with the months (1, 3, 6 or 12) - the first admin sheet with a select, so the backdrop's first
 * tap only drops the keyboard and does not lose the choice; Remove Pro confirms, since the person goes back to Free now.
 * The server refuses an account that pays through Stripe (its plan is changed there) and act() toasts that refusal as given.
 */
function proButton(handle, plan, act) {
  if (plan === 'pro') {
    return actionButton(t('admin.remove_pro'), async () => {
      if (!await confirmSheet(t('admin.remove_pro'), t('admin.confirm_remove_pro', { handle }), t('admin.remove_pro'), true)) return;
      await act(() => api('DELETE', userPath(handle) + '/pro'));
    }, true);
  }
  return actionButton(t('admin.grant_pro'), () => new Promise((resolve) => {
    let chosen = false;
    const select = el('select', { id: 'adm-months', name: 'months' }, [1, 3, 6, 12].map((n) => el('option', { value: String(n), text: t('admin.months_n', { n }) })));
    select.value = '3';
    const s = sheet({
      title: t('admin.grant_pro'), onClose: () => { if (!chosen) resolve(); },
      content: el('div', { class: 'adm-months' }, [
        el('label', { for: 'adm-months', class: 'label', text: t('admin.pro_months') }),
        select,
        el('button', { type: 'button', class: 'btn', text: t('admin.grant_pro'), onclick: async () => {
          chosen = true;
          const months = Number(select.value);
          s.close();
          await act(() => api('POST', userPath(handle) + '/pro', { months }));
          resolve();
        } })
      ])
    });
  }));
}

/** Exclude asks first (it takes every look off every board from now); putting back is the undo, so it does not. */
function boardButton(handle, excluded, act) {
  if (excluded) return actionButton(t('admin.include_board'), () => act(() => api('DELETE', userPath(handle) + '/board-exclusion')));
  return actionButton(t('admin.exclude_board'), async () => {
    if (!await confirmSheet(t('admin.exclude_board'), t('admin.confirm_exclude', { handle }), t('admin.exclude_board'), true)) return;
    await act(() => api('POST', userPath(handle) + '/board-exclusion', {}));
  }, true);
}

/**
 * An account block: the person row (name, brand mark, the flags as tags), the handle with the looks and reports against
 * them and the Pro end date, then the action row. Every button answers with the refreshed row from the server, and the
 * whole list is redrawn from it: nothing here guesses what a tap did.
 */
function userRow(row, act) {
  const user = row.user;
  const sub = [t('tag.looks', { n: countArg(row.posts) }), t('admin.reports_n', { n: countArg(row.reports) })];
  if (row.plan === 'pro' && row.proUntil) sub.push(t('admin.pro_until', { date: fmtDate(row.proUntil) }));
  return el('div', { class: 'adm-account', 'data-handle': user.handle }, [
    el('div', { class: 'person' }, [
      avatar(user),
      el('div', { class: 'who' }, [
        el('div', { class: 'name' }, [
          el('a', { href: '#/u/' + encodeURIComponent(user.handle), style: 'text-decoration:none; color: inherit;' }, [user.name, brandMark(user)]),
          row.suspended ? el('span', { class: 'tag rose', text: t('admin.suspended') }) : null,
          row.verified ? el('span', { class: 'tag accent', text: t('admin.verified') }) : null,
          row.plan === 'pro' ? el('span', { class: 'tag accent', text: t('pro.badge') }) : null,
          row.boardExcluded ? el('span', { class: 'tag rose', text: t('admin.board_excluded') }) : null,
          row.isAdmin ? el('span', { class: 'tag', text: t('admin.moderator') }) : null
        ]),
        el('div', { class: 'sub' }, [handleText(user.handle), ' · ' + sub.join(' · ')])
      ])
    ]),
    el('div', { class: 'adm-actions' }, [
      verifyButton(user.handle, row.verified, act),
      proButton(user.handle, row.plan, act),
      boardButton(user.handle, row.boardExcluded, act),
      isMe(user.handle) ? null : suspendButton(user.handle, row.suspended, act)
    ])
  ]);
}

/** The host of a link, for the sponsor card's link text; the URL itself came validated from the server. */
function hostOf(url) { try { return new URL(url).host.replace(/^www\./i, ''); } catch (e) { return url; } }

/**
 * The sponsor of the week as the server read it from its settings: not set, or the name, the prize, the link (or the
 * word that it was dropped for not being http(s)), and whether the handle is an account here and a verified brand. Read
 * only; the fix for an unverified handle is the Verify button one section up.
 */
function sponsorCard(s) {
  if (!s || !s.configured) return el('p', { class: 'muted', text: t('admin.sponsor_none') });
  const handle = s.handle;
  return el('div', { class: 'adm-sponsor' }, [
    el('div', { class: 'name' }, [el('bdi', { text: s.name || '' })]),
    s.prizeText ? el('p', { text: t('admin.sponsor_prize', { text: s.prizeText }) }) : null,
    s.url ? el('a', { href: s.url, target: '_blank', rel: 'noopener' }, [icon('link'), el('bdi', { dir: 'ltr', text: hostOf(s.url) })]) : null,
    s.urlDropped ? el('p', { class: 'alert', role: 'alert', text: t('admin.sponsor_link_dropped') }) : null,
    handle && s.handleExists ? el('a', { href: '#/u/' + encodeURIComponent(handle) }, [handleText(handle)]) : null,
    handle && s.handleExists === false ? el('p', { class: 'alert danger', role: 'alert', text: t('admin.sponsor_handle_missing', { handle }) }) : null,
    handle && s.handleExists && !s.handleVerified ? el('p', { class: 'alert', role: 'alert', text: t('admin.sponsor_handle_unverified', { handle }) }) : null,
    el('p', { class: 'hint', text: t('admin.sponsor_readonly') })
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
  const people = el('div', { id: 'adm-users', class: 'stack' });
  const sponsor = el('div', { id: 'adm-sponsor' });
  // The pilot's numbers, one tap away for the same people who can read them.
  root.appendChild(el('p', {}, [el('a', { class: 'btn-text', id: 'adm-metrics', href: '#/admin/metrics', text: t('dash.open') })]));
  root.appendChild(stats);
  root.appendChild(el('section', { class: 'adm-section' }, [el('h2', { text: t('admin.queue') }), list]));
  root.appendChild(el('section', { class: 'adm-section adm-users' }, [
    el('h2', { text: t('admin.users') }),
    el('form', { class: 'search', role: 'search', onsubmit: (event) => { event.preventDefault(); loadUsers(input.value); } }, [icon('search'), input]),
    hint,
    people
  ]));
  root.appendChild(el('section', { class: 'adm-section' }, [el('h2', { text: t('admin.sponsor') }), sponsor]));

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
    const n = (count) => (count === 1 ? 1 : fmtNumber(count));   // 1 as a number, so the _one forms fire
    stats.textContent = [t('admin.stats_looks', { n: n(data.hiddenPosts) }), t('admin.stats_comments', { n: n(data.hiddenComments) }), t('admin.stats_suspended', { n: n(data.suspendedUsers) })].join(' · ');
    const items = data.items || [];
    list.replaceChildren(...(items.length ? items.map((item) => queueItem(item, act)) : [emptyState(t('admin.empty'))]));
  }
  async function loadSponsor() {
    let data;
    try { data = await api('GET', '/api/admin/sponsor'); }
    catch (e) { if (ctx.stale()) return; sponsor.replaceChildren(el('p', { class: 'alert danger', text: e.message })); return; }
    if (ctx.stale()) return;
    sponsor.replaceChildren(sponsorCard(data));
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
  /**
   * Runs one moderation call, then reloads the lists and the sponsor card: what is left to look at is the server's word,
   * never a local guess, and a Verify tap on the sponsor's handle is seen to clear the card's warning.
   */
  async function act(call) {
    try { await call(); } catch (e) { toast(e.message); return; }
    if (ctx.stale()) return;
    toast(t('admin.done'));
    await Promise.all([loadQueue(), loadUsers(query), loadSponsor()]);
  }

  await Promise.all([loadQueue(), loadUsers(''), loadSponsor()]);
});
