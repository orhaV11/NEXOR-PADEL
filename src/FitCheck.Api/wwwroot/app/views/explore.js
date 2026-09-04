// Explore, search and tag pages: what's trending, who to follow, the best looks this week, open brand challenges.
import {
  register, el, icon, t, api, avatar, followButton, userRow, postCard, postGrid, infiniteList, emptyState, skeletonCards,
  errorBlock, setTopBar, navigate, isMe, fmtNumber, fmtCompact, relative, intentLabel, PAGE, $
} from '../core.js';

// The few rules the shared stylesheet does not have: tag chips as links, the compact challenge card, the tag count line.
const CSS = `
.x-section > * + * { margin-block-start: 10px; }
.x-bleed { margin-inline: -16px; padding-inline: 16px; }
a.chip.x-tag { display: inline-flex; align-items: center; gap: 6px; min-block-size: 44px; text-decoration: none; }
.x-tag small { font-size: 12px; color: var(--ink-3); }
.x-challenge { display: block; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); padding: 14px; text-decoration: none; color: inherit; }
.x-challenge:active { background: var(--surface-2); }
.x-challenge > * + * { margin-block-start: 6px; }
.x-challenge .x-title { font-family: var(--font-display); font-size: 18px; line-height: 1.15; font-weight: 800; min-inline-size: 0; overflow-wrap: anywhere; }
.x-challenge .x-meta { display: flex; flex-wrap: wrap; gap: 4px 12px; font-size: 13px; color: var(--ink-3); }
.x-count { padding-inline: 16px; font-size: 14px; color: var(--ink-3); }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

// ---------- pieces ----------

/** The search box. Submitting goes to the results route; nothing happens while typing. */
function searchForm(value) {
  const input = el('input', {
    type: 'search', id: 'search', name: 'q', value: value || '', placeholder: t('explore.search_placeholder'), 'aria-label': t('a11y.search'),
    autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'search', maxlength: '40'
  });
  const onsubmit = (event) => {
    event.preventDefault();
    const q = input.value.trim().replace(/^[#@]+/, '').trim();
    if (!q) { input.focus(); return; }
    navigate('#/search/' + encodeURIComponent(q));
  };
  return el('form', { class: 'search', role: 'search', onsubmit }, [icon('search'), input]);
}

function section(title, content, action) {
  const head = action ? el('div', { class: 'section-head' }, [el('h2', { text: title }), action]) : el('h2', { text: title });
  return el('section', { class: 'x-section' }, [head].concat(content));
}

/** Tag chips linking to the tag page, with the look count as a suffix. Tags may be Hebrew, so each sits in its own bdi. */
function tagChips(tags) {
  return el('div', { class: 'chips' }, tags.map((item) => el('a', { class: 'chip x-tag', href: '#/tag/' + encodeURIComponent(item.tag) }, [
    el('bdi', { text: '#' + item.tag }),
    item.posts === undefined ? null : el('small', { text: t('tag.looks', { n: fmtCompact(item.posts) }) })
  ])));
}

/** A brand in the horizontal "Brands to follow" scroll: avatar, name, followers, follow button. card is a UserCardDto. */
function brandCard(card) {
  const user = card.user;
  const followers = (n) => t('profile.followers_n', { n: fmtCompact(n) });
  const sub = el('span', { class: 'sub', text: followers(card.followers) });
  return el('div', { class: 'brand-card' }, [
    avatar(user, 'lg'),
    el('a', { class: 'name', href: '#/u/' + encodeURIComponent(user.handle), text: user.name, style: 'text-decoration:none' }),
    sub,
    isMe(user.handle) ? null : followButton(user.handle, card.following, (result) => {
      card.following = result.following; card.followers = result.followers;
      sub.textContent = followers(result.followers);
    })
  ]);
}

/** A compact open-challenge card: title, intent, brand, when it ends. The whole card links to the challenge. */
function challengeCard(c) {
  return el('a', { class: 'x-challenge', href: '#/challenge/' + c.id }, [
    el('div', { class: 'between' }, [el('b', { class: 'x-title', text: c.title }), el('span', { class: 'tag', text: intentLabel(c.intent) })]),
    el('div', { class: 'x-meta' }, [
      el('span', { text: t('challenges.by', { name: c.brand.name }) }),
      el('span', { text: t('challenges.ends', { when: relative(c.endsAt) }) })
    ])
  ]);
}

/** A look card that redraws itself when it changes (featured / unfeatured) and drops out of the list when deleted. */
function lookCard(post, onDelete) {
  const node = postCard(post, { onDelete, onChange: () => node.replaceWith(lookCard(post, onDelete)) });
  return node;
}

// ---------- explore ----------

register('explore', async (root, params, ctx) => {
  ensureStyle();
  root.appendChild(el('h1', { text: t('explore.title') }));
  root.appendChild(searchForm(''));
  const holder = el('div', {}, [skeletonCards(1)]);
  root.appendChild(holder);

  let data;
  try { data = await api('GET', '/api/explore'); }
  catch (e) { if (ctx.stale()) return; holder.replaceWith(emptyState(t('explore.empty'))); return; }
  if (ctx.stale()) return;

  const tags = data.trendingTags || [];
  const brands = data.brands || [];
  const looks = data.topLooks || [];
  const challenges = data.challenges || [];
  const frag = document.createDocumentFragment();
  if (!tags.length && !brands.length && !looks.length) frag.appendChild(emptyState(t('explore.empty')));
  if (tags.length) frag.appendChild(section(t('explore.trending'), tagChips(tags)));
  if (brands.length) frag.appendChild(section(t('explore.brands'), el('div', { class: 'people-scroll x-bleed' }, brands.map(brandCard))));
  if (looks.length) frag.appendChild(section(t('explore.top'), postGrid(looks)));
  // The challenges section always shows, so "All challenges" stays one tap away even before the first brief.
  frag.appendChild(section(t('explore.challenges'), [
    el('p', { class: 'muted', text: t('challenges.intro') }),
    ...(challenges.length ? challenges.map(challengeCard) : [el('p', { class: 'muted', text: t('challenges.empty') })])
  ], el('a', { href: '#/challenges', text: t('explore.all_challenges') })));
  holder.replaceWith(frag);
});

// ---------- search ----------

register('search', async (root, params, ctx) => {
  ensureStyle();
  const q = (params.q || '').trim();
  const title = t('explore.results_title', { q });
  setTopBar({ back: true, title });
  root.appendChild(el('h1', { class: 'sr-only', text: title }));
  root.appendChild(searchForm(q));
  const holder = el('div', {}, [skeletonCards(1)]);
  root.appendChild(holder);
  const nothing = () => emptyState(t('explore.no_results', { q }));
  if (!q) { holder.replaceWith(nothing()); return; }

  let data;
  try { data = await api('GET', '/api/search?q=' + encodeURIComponent(q)); }
  catch (e) {
    if (ctx.stale()) return;
    holder.replaceWith(e.status === 400 || e.status === 501 ? nothing() : errorBlock(e));
    return;
  }
  if (ctx.stale()) return;

  const users = data.users || [];
  const tags = data.tags || [];
  if (!users.length && !tags.length) { holder.replaceWith(nothing()); return; }
  const frag = document.createDocumentFragment();
  if (users.length) frag.appendChild(section(t('explore.people'), el('div', {}, users.map((card) => userRow(card)))));
  if (tags.length) frag.appendChild(section(t('explore.tags'), tagChips(tags)));
  holder.replaceWith(frag);
});

// ---------- tag ----------

register('tag', async (root, params, ctx) => {
  ensureStyle();
  const tag = params.tag || '';
  root.classList.add('flush');
  setTopBar({ back: true, title: '#' + tag });
  // The tag decides the direction of its own title ("#datenight" must not become "datenight#" in Hebrew).
  const titleNode = $('top-inner').querySelector('.top-title');
  if (titleNode) titleNode.dir = 'auto';
  root.appendChild(el('h1', { class: 'sr-only', text: '#' + tag }));
  if (!tag) { root.appendChild(emptyState(t('common.not_found'))); return; }

  const countLine = el('p', { class: 'x-count', hidden: true });
  root.appendChild(countLine);
  let count = 0;
  let list = null;
  const refresh = () => { if (list) list.refresh(); };
  list = infiniteList(root, {
    load: async (offset) => {
      const page = await api('GET', '/api/tags/' + encodeURIComponent(tag) + '/posts?offset=' + offset + '&limit=' + PAGE);
      if (offset === 0) count = 0;
      count += page.items.length;
      countLine.textContent = t('tag.looks', { n: fmtNumber(count) });
      countLine.hidden = count === 0;
      return page;
    },
    render: (post) => lookCard(post, refresh),
    empty: () => emptyState(t('tag.empty')),
    stale: ctx.stale
  });
});
