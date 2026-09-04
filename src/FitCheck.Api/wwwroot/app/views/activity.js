// Activity: the signed-in person's notifications, newest first. One row per notification: the actor's avatar, a
// sentence that links to the look, the challenge or the actor, when it happened, and a thumbnail of the look when
// there is one. Unread rows stay tinted for this visit so what is new is visible; opening the page marks everything
// read and clears the tab badge. Pull down to refresh. Signed out, the page is a sign-in prompt.
import {
  register, state, t, api, el, avatar, relative, isMe, setTopBar, signInPrompt, emptyState, errorBlock,
  pullToRefresh, announce, toast, renderShell
} from '../core.js';

const KNOWN_TYPES = ['fire', 'comment', 'follow', 'vote', 'entry', 'ended', 'won', 'mention', 'featured'];

let styled = false;
/**
 * Two things app.css does not cover: the avatar is a link, and `.activity a { flex: 1 }` would stretch it across the
 * row, so it is pinned back to its circle; and skeleton rows shaped like the real ones (.skel itself is shared).
 */
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '.activity a.avatar { flex: none; }',
    '.activity-skel li { pointer-events: none; }',
    '.activity-skel .skel-round { inline-size: 40px; block-size: 40px; border-radius: 50%; flex: none; }',
    '.activity-skel .skel-lines { flex: 1; display: grid; gap: 8px; }',
    '.activity-skel .skel-lines .skel { block-size: 11px; inline-size: 72%; }',
    '.activity-skel .skel-lines .skel + .skel { inline-size: 28%; }',
    '.activity-pad { padding-inline: 16px; }'
  ].join('\n') }));
}

function skeletonRows(n) {
  const list = el('ul', { class: 'activity activity-skel', 'aria-hidden': 'true' });
  for (let i = 0; i < n; i++) {
    list.appendChild(el('li', {}, [
      el('div', { class: 'skel skel-round' }),
      el('div', { class: 'skel-lines' }, [el('div', { class: 'skel' }), el('div', { class: 'skel' })])
    ]));
  }
  return list;
}

/** "Your challenge ended" with yourself as the actor means nobody entered. Unknown types show their raw type. */
function sentenceKey(n) {
  if (n.type === 'ended' && isMe(n.actorHandle)) return 'activity.ended_empty';
  return KNOWN_TYPES.includes(n.type) ? 'activity.' + n.type : null;
}

/** The look when there is one, else the challenge, else the person who did it. */
function target(n) {
  if (n.postId) return '#/post/' + n.postId;
  if (n.challengeId) return '#/challenge/' + n.challengeId;
  return '#/u/' + encodeURIComponent(n.actorHandle);
}

function row(n) {
  const actor = n.actorName || n.actorHandle;
  const key = sentenceKey(n);
  const sentence = key ? t(key, { actor }) : actor + ' · ' + n.type;
  return el('li', { class: n.read ? null : 'unread' }, [
    avatar({ handle: n.actorHandle, name: actor, avatarUrl: n.actorAvatarUrl }),
    el('a', { href: target(n) }, [
      el('div', { text: sentence }),
      el('div', { class: 'when', text: relative(n.createdAt) })
    ]),
    // A thumbnail of the look. Deleted or hidden looks answer 404: the image just goes away.
    n.postId ? el('img', {
      class: 'thumb', src: '/api/posts/' + n.postId + '/image', alt: '', loading: 'lazy', decoding: 'async',
      onerror: (event) => { event.currentTarget.hidden = true; }
    }) : null
  ]);
}

function list(items) {
  return el('ul', { class: 'activity' }, items.map(row));
}

/** Everything on the page has been seen: tell the API, then clear the tab badge. Failure is silent; next visit retries. */
function markRead() {
  api('POST', '/api/notifications/read')
    .then(() => { if (state.me) { state.me.unreadNotifications = 0; renderShell(); } })
    .catch(() => { /* the badge stays until the next visit */ });
}

register('activity', async (root, params, ctx) => {
  setTopBar({ title: t('activity.title') });
  const heading = el('h1', { class: 'sr-only', text: t('activity.title') });
  if (!state.me) { root.appendChild(el('div', {}, [heading, signInPrompt()])); return; }

  ensureStyle();
  root.classList.add('flush');
  const indicator = el('div', { class: 'ptr' });
  const body = el('div', {}, [skeletonRows(5)]);
  // One wrapper so .view's sibling spacing does not open a gap above the list.
  root.appendChild(el('div', {}, [heading, indicator, body]));

  let seq = 0;
  async function load(initial) {
    const mine = ++seq;
    let data;
    try {
      data = await api('GET', '/api/notifications');
    } catch (e) {
      if (ctx.stale() || mine !== seq) return;
      if (e.status === 401) { root.classList.remove('flush'); body.replaceChildren(signInPrompt()); return; }
      if (initial) body.replaceChildren(el('div', { class: 'activity-pad' }, [errorBlock(e)]));
      else toast(e.message);
      return;
    }
    if (ctx.stale() || mine !== seq) return;
    const items = data && Array.isArray(data.items) ? data.items : [];
    body.replaceChildren(items.length ? list(items) : emptyState(t('activity.empty')));
    const unread = data && typeof data.unread === 'number' ? data.unread : 0;
    if (unread > 0) markRead();
    else if (state.me && state.me.unreadNotifications) { state.me.unreadNotifications = 0; renderShell(); }
  }

  pullToRefresh(indicator, async () => {
    await load(false);
    if (!ctx.stale()) announce(t('common.refreshed'));
  });

  await load(true);
});
