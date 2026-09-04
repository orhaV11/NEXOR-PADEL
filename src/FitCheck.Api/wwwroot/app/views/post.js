// Post detail: the look, who was tagged in it, its challenge votes, the comments, and a sticky composer.
// Ported from the Phase 2 monolith's postView onto the Phase 3 kit (bottom sheets instead of window.confirm,
// flush edge-to-edge card, top bar with a back arrow).
import {
  register, state, t, api, el, avatar, userRow, postCard, setTopBar, navigate, requireSignIn,
  emptyState, skeletonCards, errorBlock, toast, confirmSheet, relative, fmtNumber, fmtCompact
} from '../core.js';

let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: [
    '.post-section { padding-inline: 16px; }',
    '.post-section > * + * { margin-block-start: 10px; }',
    '.post-card:focus { outline: none; }',   /* a landing target for focusHeading, not a control: no full-card ring */
    '.view.flush > .composer { padding-inline: 16px; }',
    '.comments .when { font-size: 12px; color: var(--ink-3); margin-block-start: 2px; }',
    '.comments .text .body { white-space: pre-line; }',
    '.comments .btn-text { flex: none; min-block-size: 44px; padding-inline: 8px; }',
    '.comments .comment-note { flex-direction: column; align-items: stretch; gap: 8px; border-block-end: 0; }',
    '.comments li.muted { justify-content: center; border-block-end: 0; padding-block: 24px; }'
  ].join('\n') }));
}

register('post', async (root, params, ctx) => {
  ensureStyle();
  root.classList.add('flush');
  setTopBar({ back: true, title: t('post.title') });

  const id = params.id;
  const skeleton = skeletonCards(1);
  root.appendChild(skeleton);

  let post;
  try {
    if (!id) throw Object.assign(new Error(t('common.not_found')), { status: 404 });
    post = await api('GET', '/api/posts/' + encodeURIComponent(id));
  } catch (e) {
    if (ctx.stale()) return;
    skeleton.remove();
    root.appendChild(e && e.status === 404 ? emptyState(t('common.not_found')) : errorBlock(e));
    return;
  }
  if (ctx.stale()) return;
  skeleton.remove();

  // ---- the look ----
  // The wrapper takes focus after navigation (focusHeading looks for [tabindex="-1"]); the top bar has the back arrow.
  const cardWrap = el('div', { class: 'post-card', id: 'post-card', tabindex: '-1' });
  const cardOpts = { eager: true, onDelete: () => navigate('#/me'), onChange: () => refreshCard() };
  function refreshCard() {
    cardWrap.replaceChildren(postCard(post, cardOpts));
    syncCount();
  }
  cardWrap.appendChild(postCard(post, cardOpts));
  root.appendChild(cardWrap);

  // ---- tagged accounts ----
  if (Array.isArray(post.mentions) && post.mentions.length) {
    root.appendChild(el('section', { class: 'post-section', id: 'post-tagged', 'aria-labelledby': 'post-tagged-title' }, [
      el('h2', { id: 'post-tagged-title', text: t('post.tagged') }),
      el('div', { class: 'people' }, post.mentions.map((m) => userRow(m, { follow: false })))
    ]));
  }

  // ---- challenge votes ----
  if (post.challengeId) {
    const votes = post.votes === undefined || post.votes === null ? 0 : post.votes;
    const line = el('p', { class: 'muted post-section', id: 'post-votes', text: t('post.votes', { n: fmtNumber(votes) }) });
    root.appendChild(line);
  }

  // ---- comments ----
  let comments = [];
  const list = el('ul', { class: 'comments', id: 'comments' });
  root.appendChild(el('section', { class: 'post-section', 'aria-labelledby': 'comments-title' }, [
    el('h2', { id: 'comments-title', text: t('comments.title') }),
    list
  ]));

  function syncCount() {
    post.commentCount = comments.length;
    const count = root.querySelector('.card a.action .count');
    if (count) count.textContent = fmtCompact(comments.length);
  }

  function commentRow(c) {
    let action = null;
    if (c.canDelete) {
      action = el('button', { type: 'button', class: 'btn-text', 'data-action': 'delete', text: t('comments.delete'), onclick: () => removeComment(c, action) });
    } else if (state.me && !c.isMine) {
      action = el('button', { type: 'button', class: 'btn-text', 'data-action': 'report', text: t('comments.report'), onclick: () => reportComment(c, action) });
    }
    return el('li', { 'data-comment': c.id }, [
      avatar(c.user, { size: 'sm' }),
      el('div', { class: 'text' }, [
        el('b', { text: c.user.name }),
        ' ',   // a real space: the name's margin-inline-end lands outside the LTR run inside an RTL paragraph
        el('span', { class: 'body', text: c.text }),
        el('div', { class: 'when', text: relative(c.createdAt) })
      ]),
      action
    ]);
  }

  function renderComments() {
    list.replaceChildren();
    if (comments.length === 0) list.appendChild(el('li', { class: 'muted', text: t('comments.empty') }));
    for (const c of comments) list.appendChild(commentRow(c));
    syncCount();
  }

  async function loadComments() {
    list.replaceChildren(el('li', { class: 'muted', text: t('common.loading') }));
    try {
      const result = await api('GET', '/api/posts/' + encodeURIComponent(id) + '/comments');
      if (ctx.stale()) return;
      comments = Array.isArray(result) ? result : [];
      renderComments();
    } catch (e) {
      if (ctx.stale()) return;
      list.replaceChildren(el('li', { class: 'comment-note' }, [
        el('p', { class: 'alert danger', text: e && e.message ? e.message : t('error.generic') }),
        el('button', { type: 'button', class: 'btn btn-secondary', text: t('common.retry'), onclick: loadComments })
      ]));
    }
  }

  async function removeComment(c, button) {
    if (!requireSignIn()) return;
    if (button) button.disabled = true;
    try {
      await api('DELETE', '/api/comments/' + encodeURIComponent(c.id));
      if (ctx.stale()) return;
      comments = comments.filter((x) => x.id !== c.id);
      renderComments();
    } catch (e) {
      if (ctx.stale()) return;
      if (button) button.disabled = false;
      toast(e.message);
    }
  }

  async function reportComment(c, button) {
    if (!requireSignIn()) return;
    if (!await confirmSheet(t('comments.report'), t('comments.report_confirm'), t('comments.report'), true)) return;
    if (ctx.stale()) return;
    if (button) button.disabled = true;
    try {
      await api('POST', '/api/comments/' + encodeURIComponent(c.id) + '/report', { reason: 'reported from app' });
      if (ctx.stale()) return;
      toast(t('post.reported'));
    } catch (e) {
      if (ctx.stale()) return;
      toast(e.message);
    } finally {
      if (button) button.disabled = false;
    }
  }

  // ---- composer ----
  if (state.me) {
    const input = el('input', {
      type: 'text', id: 'comment-input', maxlength: '200', autocomplete: 'off', enterkeyhint: 'send',
      placeholder: t('comments.placeholder'), 'aria-label': t('comments.title')
    });
    const send = el('button', { type: 'button', class: 'btn', id: 'comment-send', text: t('comments.send') });
    let busy = false;
    async function submit() {
      const text = input.value.trim();
      if (!text || busy) return;
      if (!requireSignIn()) return;
      busy = true; send.disabled = true;
      try {
        const created = await api('POST', '/api/posts/' + encodeURIComponent(id) + '/comments', { text });
        if (ctx.stale()) return;
        input.value = '';
        if (created && created.id) { comments.push(created); renderComments(); }
        else await loadComments();
      } catch (e) {
        if (ctx.stale()) return;
        toast(e.message);
      } finally {
        busy = false; send.disabled = false;
        if (!ctx.stale() && document.activeElement === send) input.focus({ preventScroll: true });
      }
    }
    send.addEventListener('click', submit);
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.isComposing) { event.preventDefault(); submit(); }
    });
    root.appendChild(el('div', { class: 'composer' }, [input, send]));
  } else {
    root.appendChild(el('p', { class: 'post-section', id: 'comment-signin' }, [
      el('a', { class: 'btn-text', href: '#/login', text: t('comments.signin'), onclick: () => { state.returnTo = location.hash; } })
    ]));
  }

  await loadComments();
});
