// Post detail: the look, who was tagged in it, its challenge votes, the comments, and a sticky composer.
// Ported from the Phase 2 monolith's postView onto the Phase 3 kit (bottom sheets instead of window.confirm,
// flush edge-to-edge card, top bar with a back arrow).
// Round 10, the items on the look: "The look" (#look-items) lists the pieces under the card (name · brand · model; a
// row opens the item sheet #item-sheet with brand, model, category, "Shop at {host}" (#item-shop → /api/items/{id}/out
// in a new tab) and the leaves-and-commission line); the dots (.item-dot[data-item]) sit over the photo behind the tag
// toggle (#items-toggle); the owner's "Edit items" (#items-edit) opens the same editor as the post sheet in #items-sheet
// and saves with PATCH /api/posts/{id}/items (#items-save).
import {
  register, state, t, api, el, icon, avatar, userRow, postCard, setTopBar, navigate, requireSignIn, sheet,
  emptyState, skeletonCards, errorBlock, toast, pickReportReason, relative, fmtNumber, fmtCompact, breakdownRow
} from '../core.js';
import { itemsEditor, itemLine, hostOf, hasDot, categoryLabel } from '../items.js';

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
    mountDots();
  }
  cardWrap.appendChild(postCard(post, cardOpts));
  root.appendChild(cardWrap);

  // ---- the look: the pieces under the caption, the dots over the photo behind the tag toggle ----
  let items = Array.isArray(post.items) ? post.items : [];
  let dotsShown = false;

  /**
   * The dots live beside the photo link (a button inside a link is not a thing), in the same .card-media wrapper a clip
   * uses, at their X/Y of the 4:5 box; physical left/top, since a photo does not mirror in Hebrew. Hidden until the tag
   * toggle at the photo's bottom-start corner shows them; the toggle carries the count the card's own badge showed.
   */
  function mountDots() {
    const photo = cardWrap.querySelector('.card-photo');
    if (!photo) return;
    let media = photo.closest('.card-media');
    if (!media) { media = el('div', { class: 'card-media' }); photo.replaceWith(media); media.appendChild(photo); }
    for (const old of media.querySelectorAll('.item-dots, .items-toggle, .item-count')) old.remove();
    const placed = items.filter(hasDot);
    if (!placed.length) return;
    const layer = el('div', { class: 'item-dots', id: 'item-dots', hidden: !dotsShown }, placed.map((item) => {
      const n = items.indexOf(item) + 1;
      return el('button', {
        type: 'button', class: 'item-dot', id: 'item-dot-' + n, 'data-item': item.id, style: 'left:' + (item.x * 100).toFixed(1) + '%;top:' + (item.y * 100).toFixed(1) + '%',
        'aria-label': t('items.dot_label', { n: fmtNumber(n), name: item.name }), onclick: () => openItemSheet(item)
      }, [el('span', { text: fmtNumber(n) })]);
    }));
    const toggle = el('button', { type: 'button', class: 'items-toggle', id: 'items-toggle', 'aria-pressed': String(dotsShown), 'aria-label': t(dotsShown ? 'items.toggle_hide' : 'items.toggle') }, [
      icon('tag'), el('span', { class: 'n', text: fmtNumber(items.length) })
    ]);
    toggle.addEventListener('click', () => {
      dotsShown = !dotsShown;
      layer.hidden = !dotsShown;
      toggle.setAttribute('aria-pressed', String(dotsShown));
      toggle.setAttribute('aria-label', t(dotsShown ? 'items.toggle_hide' : 'items.toggle'));
    });
    media.appendChild(layer);
    media.appendChild(toggle);
  }

  /** The item sheet: brand (a link to its looks), model, category, "Shop at {host}" through /api/items/{id}/out, and the honest line under it. */
  function openItemSheet(item) {
    const host = item.url ? hostOf(item) : '';
    const kv = [];
    if (item.brand) kv.push(el('dt', { text: t('items.brand') }), el('dd', {}, [el('a', { href: '#/items/' + encodeURIComponent(item.brand), text: item.brand })]));
    if (item.model) kv.push(el('dt', { text: t('items.model') }), el('dd', { text: item.model }));
    kv.push(el('dt', { text: t('items.category') }), el('dd', { text: categoryLabel(item.category) }));
    const content = el('div', { class: 'stack item-sheet' }, [
      el('dl', { class: 'item-kv' }, kv),
      item.source === 'Stylist' ? el('p', { class: 'hint', text: t('items.by_stylist') }) : null,
      // The link leaves through the server's one door, in a new tab, with no way back into this window.
      item.url && host ? el('a', { class: 'btn', id: 'item-shop', href: '/api/items/' + encodeURIComponent(item.id) + '/out', target: '_blank', rel: 'noopener' }, [icon('bag'), t('items.shop_at', { host })]) : null,
      // "Leaves OREVOSH" under every link; the commission line only while Affiliate:Disclosure is on (/api/config).
      item.url && host ? el('p', { class: 'hint item-leaves', id: 'item-leaves' }, [
        el('span', { text: t('items.leaves') }),
        ...(state.config.affiliate && state.config.affiliate.disclosure === false ? [] : [' · ', el('span', { id: 'item-disclosure', text: t('affiliate.disclosure') })])
      ]) : null,
      item.brand ? el('a', { class: 'btn-text', id: 'item-more', href: '#/items/' + encodeURIComponent(item.brand), text: t('items.more_looks', { brand: item.brand }) }) : null
    ]);
    const s = sheet({ title: item.name, content });
    s.panel.id = 'item-sheet';
    s.panel.dataset.item = item.id;
  }

  const lookSection = el('section', { class: 'post-section', id: 'look-items', 'aria-labelledby': 'look-items-title', hidden: true });
  root.appendChild(lookSection);
  function renderItems() {
    const own = !!post.isMine;
    lookSection.hidden = !items.length && !own;
    if (lookSection.hidden) { lookSection.replaceChildren(); mountDots(); return; }
    const head = el('div', { class: 'section-head' }, [
      el('h2', { id: 'look-items-title', text: t('items.title') }),
      own ? el('button', { type: 'button', class: 'btn-text', id: 'items-edit', text: t('items.edit_items'), onclick: openItemsEditor }) : null
    ]);
    const list = items.length
      ? el('ul', { class: 'look-items' }, items.map((item, i) => el('li', { 'data-item': item.id }, [
        el('button', { type: 'button', class: 'look-item' + (hasDot(item) ? ' placed' : ''), onclick: () => openItemSheet(item) }, [
          el('span', { class: 'num', 'aria-hidden': 'true', text: fmtNumber(i + 1) }),
          el('span', { class: 'txt' }, itemLine(item)),
          item.url ? icon('bag') : null
        ])
      ])))
      : el('p', { class: 'hint', text: t('items.empty_own') });
    lookSection.replaceChildren(head, list);
    mountDots();
  }

  /** "Edit items": the post sheet's editor over this look's rows and photo; the answer replaces the list here. */
  function openItemsEditor() {
    if (!requireSignIn()) return;
    const editor = itemsEditor({ items }, post.imageUrl);
    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    const save = el('button', { type: 'button', class: 'btn', id: 'items-save', text: t('items.save') });
    const cancel = el('button', { type: 'button', class: 'btn btn-ghost', text: t('common.cancel'), onclick: () => s.close() });
    const s = sheet({ title: t('items.edit_items'), content: el('div', { class: 'stack' }, [editor.node, error, el('div', { class: 'row' }, [save, cancel])]) });
    s.panel.id = 'items-sheet';
    save.addEventListener('click', async () => {
      save.disabled = true; error.hidden = true;
      try {
        const answer = await api('PATCH', '/api/posts/' + encodeURIComponent(id) + '/items', { items: editor.value() });
        if (ctx.stale()) return;
        items = Array.isArray(answer) ? answer : (answer && Array.isArray(answer.items)) ? answer.items : [];
        post.items = items;
        post.itemCount = items.length;
        s.close();
        toast(t('items.saved'));
        renderItems();
      } catch (e) {
        if (ctx.stale()) return;
        error.textContent = e && e.message ? e.message : t('error.generic');
        error.hidden = false;
        save.disabled = false;
      }
    });
  }
  renderItems();

  // ---- the breakdown (rubric v2): public like the score, right under the card's headline; a look from before v2 has none ----
  if (post.breakdown) {
    root.appendChild(el('section', { class: 'post-section', id: 'post-breakdown', 'aria-labelledby': 'post-breakdown-title' }, [
      el('h2', { id: 'post-breakdown-title', text: t('result.breakdown') }),
      breakdownRow(post.breakdown)
    ]));
  }

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
    const reason = await pickReportReason();
    if (!reason || ctx.stale()) return;
    if (button) button.disabled = true;
    try {
      await api('POST', '/api/comments/' + encodeURIComponent(c.id) + '/report', { reason });
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
