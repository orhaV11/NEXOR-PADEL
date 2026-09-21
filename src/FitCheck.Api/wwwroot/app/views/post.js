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
  emptyState, skeletonCards, errorBlock, toast, pickReportReason, relative, fmtNumber, fmtCompact, breakdownRow, onLeave
} from '../core.js';
// Round 14 - before and after: the pair's own share format (app/sharecard.js draws the card, app/sharevideo.js the film).
import { openBeforeAfterShare } from '../sharevideo.js';
import { itemsEditor, itemLine, hostOf, hasDot, categoryLabel } from '../items.js';
// Round 13 — the growth loop: the look's public address, and the ?via capture this import starts at boot (main.js
// imports this view, this view imports that module, and the module reads ?via on import before any screen draws).
import { openLookLinkSheet, publicLookUrl, pretty } from '../invite.js';

/**
 * Round 14 — post the look, keep the grade: the choice at the moment of posting, as a field for the post sheet
 * (views/check.js, openPostSheet) beside the caption and the pieces:
 *
 *   const grade = gradeField();          // before the sheet's content is built
 *   ... after.node, grade.node, items.node ...
 *   api('POST', '/api/posts', { ..., scorePrivate: grade.value() });
 *
 * value() is false unless the person asked to keep the number, which is what posting has always done. The same switch
 * lives on the look itself afterwards (the grade row below), so nothing is decided once and for ever here.
 */
export function gradeField() {
  let keep = false;
  const toggle = el('button', {
    type: 'button', class: 'chip', id: 'grade-keep', 'aria-pressed': 'false', text: t('share.grade_keep')
  });
  toggle.addEventListener('click', () => {
    keep = !keep;
    toggle.setAttribute('aria-pressed', String(keep));
  });
  const node = el('div', { class: 'field grade-field' }, [
    el('span', { class: 'label', text: t('share.grade_field') }),
    toggle,
    el('span', { class: 'hint', text: t('share.grade_keep_hint') })
  ]);
  return { node, value: () => keep };
}

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
    '.comments li.muted { justify-content: center; border-block-end: 0; padding-block: 24px; }',
    /* Round 13 — the growth loop: the public link row under the card. */
    '.post-link { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }',
    '.post-link .btn-sm { min-block-size: 44px; padding-inline: 16px; }',
    '.post-link code { min-inline-size: 0; overflow-wrap: anywhere; direction: ltr; font-size: 12px; color: var(--ink-3); }',
    /* Round 14 - post the look, keep the grade: the author's own switch, and the quiet note a reader sees instead of a number. */
    '.grade-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }',
    '.grade-row .grade-text { min-inline-size: 0; }',
    '.grade-row .grade-text b { display: block; }',
    '.grade-row .grade-text span { font-size: 13px; color: var(--ink-3); }',
    '.grade-row .btn-sm { min-block-size: 44px; padding-inline: 16px; margin-inline-start: auto; }',
    '.grade-note { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--ink-3); }',
    '.grade-note .icon { color: var(--accent); }',
    /* Round 14 - the comment box has a direction: two or three openers a tap fills in, quiet enough to ignore. */
    '.openers { display: flex; gap: 8px; overflow-x: auto; padding-block: 8px 2px; scrollbar-width: none; -webkit-overflow-scrolling: touch; }',
    '.openers::-webkit-scrollbar { display: none; }',
    '.openers .chip { flex: none; min-block-size: 44px; font-weight: 500; color: var(--ink-2); }'
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

  // ---- Round 13 — the growth loop: this look has a public address ----
  // One row under the card: the share sheet (or the clipboard where there is none) with /look/{id}, the page anyone can
  // open with no app and no account, and the address itself in small print so nobody has to trust a button.
  root.appendChild(el('section', { class: 'post-section post-link', id: 'post-link' }, [
    el('button', {
      type: 'button', class: 'btn btn-sm btn-secondary', id: 'post-link-share', text: t('link.copy'),
      onclick: () => openLookLinkSheet(post)
    }),
    el('code', { id: 'post-link-url', dir: 'ltr', text: pretty(publicLookUrl(post.id)) })
  ]));

  // ---- Round 14 — post the look, keep the grade; before and after ----
  // On your own look: one row that says where the grade stands and flips it, and, when this look follows an earlier one,
  // the before/after share beside it. On everyone else's: nothing at all when the number is public, and one quiet line
  // when it is not, so a reader is not left wondering where the ring went.
  const gradeSection = el('section', { class: 'post-section', id: 'post-grade' });
  root.appendChild(gradeSection);

  function beforeAfterButton() {
    if (!post.before) return null;
    return el('button', {
      type: 'button', class: 'btn btn-sm btn-secondary', id: 'before-after-share', text: t('share.before_after_action'),
      onclick: () => openBeforeAfterShare(post)
    });
  }

  function renderGrade() {
    gradeSection.replaceChildren();
    if (!post.isMine) {
      // Round 14: a look whose grade its author kept is not a look with a number missing. Say so once, plainly, and
      // only where a number would have been: the card, the breakdown and the match row simply are not drawn.
      if (post.scorePrivate) {
        gradeSection.appendChild(el('p', { class: 'grade-note', id: 'grade-note' }, [icon('shield'), el('span', { text: t('share.grade_note') })]));
      }
      return;
    }

    const flip = el('button', {
      type: 'button', class: 'btn btn-sm btn-secondary', id: 'grade-toggle',
      text: t(post.scorePrivate ? 'share.grade_show' : 'share.grade_hide')
    });
    flip.addEventListener('click', async () => {
      if (!requireSignIn()) return;
      const wanted = !post.scorePrivate;
      flip.disabled = true;
      try {
        const answer = await api('PATCH', '/api/posts/' + encodeURIComponent(id) + '/score-privacy', { scorePrivate: wanted });
        if (ctx.stale()) return;
        post.scorePrivate = !!(answer && answer.scorePrivate);
        // The card is drawn from this object, so the number goes or comes back with it: the author's own view keeps
        // the score either way (it is theirs), and the row below says what everybody else now sees.
        toast(t(post.scorePrivate ? 'share.grade_now_private' : 'share.grade_now_public'));
        renderGrade();
        renderBreakdown();
      } catch (e) {
        if (ctx.stale()) return;
        flip.disabled = false;
        toast(e.message);
      }
    });
    gradeSection.appendChild(el('div', { class: 'grade-row', id: 'grade-row' }, [
      icon('shield'),
      el('div', { class: 'grade-text' }, [
        el('b', { text: t(post.scorePrivate ? 'share.grade_private' : 'share.grade_public') }),
        el('span', { text: t(post.scorePrivate ? 'share.grade_private_hint' : 'share.grade_public_hint') })
      ]),
      flip
    ]));
    const share = beforeAfterButton();
    if (share) gradeSection.appendChild(el('div', { class: 'grade-row', id: 'before-after-row' }, [share]));
  }
  renderGrade();

  // Round 11: Block in the card's "…" (views/blocked.js raises the event) takes this look out of the viewer's world; leave it.
  const onBlock = (event) => {
    const detail = event.detail || {};
    if (!detail.blocked || String(detail.handle).toLowerCase() !== post.user.handle.toLowerCase()) return;
    if (history.length > 1) history.back(); else navigate('#/');
  };
  document.addEventListener('orevosh:block', onBlock);
  onLeave(() => document.removeEventListener('orevosh:block', onBlock));

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
  // Round 14: "public like the score" now means exactly that — a private grade takes the sub-scores with it for every
  // reader but the author (who keeps seeing their own) and a moderator. The server sends none at all to anybody else.
  const breakdownSection = el('section', { class: 'post-section', id: 'post-breakdown', 'aria-labelledby': 'post-breakdown-title', hidden: true });
  root.appendChild(breakdownSection);
  function renderBreakdown() {
    breakdownSection.hidden = !post.breakdown;
    if (!post.breakdown) { breakdownSection.replaceChildren(); return; }
    breakdownSection.replaceChildren(
      el('h2', { id: 'post-breakdown-title', text: t('result.breakdown') }),
      breakdownRow(post.breakdown)
    );
  }
  renderBreakdown();

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

    // ---- Round 14 — the comment box has a direction ----
    // The product's own pitch starts with a friend who says "fire" without looking, and the feed's one reaction is a
    // flame. The flame stays: it is appreciation, and it is the brand. Under it the box offers three openers a tap
    // fills in — the piece doing the most work, a swap to try, where something is from. They are starting points, not
    // templates: the text lands in the box with the cursor at its end and nothing is sent until the person sends it.
    // A tap is remembered only until the comment goes, and only as "an opener was used": the tally on the numbers page
    // counts all three together, so the owner can see whether this changed anything and nothing else is learned.
    const OPENERS = ['piece', 'swap', 'where'];
    let opener = null;
    const openers = el('div', { class: 'openers', id: 'comment-openers', role: 'group', 'aria-label': t('comment.openers_label') }, OPENERS.map((key) => el('button', {
      type: 'button', class: 'chip', 'data-opener': key, text: t('comment.opener_' + key),
      onclick: () => {
        opener = key;
        input.value = t('comment.opener_' + key) + ' ';
        input.focus();
        try { input.setSelectionRange(input.value.length, input.value.length); } catch (e) { /* a field that does not take a selection */ }
      }
    })));

    let busy = false;
    async function submit() {
      const text = input.value.trim();
      if (!text || busy) return;
      if (!requireSignIn()) return;
      busy = true; send.disabled = true;
      try {
        const created = await api('POST', '/api/posts/' + encodeURIComponent(id) + '/comments', { text, opener });
        if (ctx.stale()) return;
        input.value = '';
        opener = null;
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
    // The openers sit above the box, not inside it: .composer is sticky against the tab bar and takes its inline
    // padding from a direct-child rule, so nothing may wrap it.
    root.appendChild(el('div', { class: 'post-section' }, [openers]));
    root.appendChild(el('div', { class: 'composer' }, [input, send]));
  } else {
    root.appendChild(el('p', { class: 'post-section', id: 'comment-signin' }, [
      el('a', { class: 'btn-text', href: '#/login', text: t('comments.signin'), onclick: () => { state.returnTo = location.hash; } })
    ]));
  }

  await loadComments();
});
