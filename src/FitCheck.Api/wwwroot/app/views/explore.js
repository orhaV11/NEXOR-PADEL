// Explore, search and tag pages. Explore is the week's front page: kicker, title and dateline, the search rule, the
// hero look, trending tags as a numbered index, the brands band, the staggered wall of top looks and the open
// challenges as quiet tickets. All the rules live in app.css; this module only builds the DOM.
import {
  register, el, icon, t, api, avatar, followButton, userRow, postCard, postGrid, infiniteList, emptyState, skeletonCards, errorBlock, setTopBar, navigate, isMe, fmtNumber, fmtCompact, fmtDate, relative, intentLabel, PAGE, $, scoreBadge
} from '../core.js';
import { boardStrip } from './board.js';

/** The count a plural key wants: the number 1 (so the _one form fires) or the compact figure. */
const countArg = (n) => (n === 1 ? 1 : fmtCompact(n));

// ---------- pieces ----------

/** The search rule. Submitting goes to the results route; nothing happens while typing. */
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

/** The front block: THIS WEEK · Explore · Issue · <today>, with the date in the locale's own words. */
function front() {
  return el('div', { class: 'x-front' }, [
    el('p', { class: 'kicker', text: t('explore.kicker') }),
    el('h1', { text: t('explore.title') }),
    el('p', { class: 'dateline', text: t('explore.issue', { date: fmtDate(new Date().toISOString()) }) })
  ]);
}

function section(title, content, action) {
  const head = action ? el('div', { class: 'section-head' }, [el('h2', { text: title }), action]) : el('h2', { text: title });
  return el('section', { class: 'x-section' }, [head].concat(content));
}

/** The brass score stamp a print carries, the same one postGrid and the card use. */
function scoreStamp(post) {
  return scoreBadge(post.score);
}

/** This week's look: a split hero from the first top look. The whole block is one link to the post. */
function heroCard(post) {
  const intent = intentLabel(post.intent);
  const name = post.user.name;
  return el('a', { class: 'x-hero', href: '#/post/' + post.id, 'aria-label': t('a11y.look_by', { intent, name }) }, [
    el('figure', {}, [
      el('img', { src: post.imageUrl, alt: '', decoding: 'async' }),
      scoreStamp(post)
    ]),
    el('figcaption', {}, [
      el('span', { class: 'kicker', text: t('explore.hero_kicker') }),
      el('p', { class: 'x-hero-title', text: post.headline }),
      el('p', { class: 'x-hero-by' }, [el('b', {}, [el('bdi', { text: name })]), ' · ', el('span', { text: intent })]),
      el('span', { class: 'x-hero-more', text: t('explore.read_look') })
    ])
  ]);
}

/** Tags as a numbered index: rank, #tag, a dotted leader, the look count. Tags may be Hebrew, so each sits in its own bdi. */
function tagIndex(tags) {
  return el('ol', { class: 'index' }, tags.map((item, i) => el('li', {}, [
    el('a', { href: '#/tag/' + encodeURIComponent(item.tag) }, [
      el('span', { class: 'num', text: String(i + 1).padStart(2, '0') }),
      el('bdi', { class: 'tag-name', text: '#' + item.tag }),
      el('span', { class: 'lead', 'aria-hidden': 'true' }),
      item.posts === undefined ? null : el('span', { class: 'count', text: t('tag.looks', { n: countArg(item.posts) }) })
    ])
  ])));
}

/** A brand in the "Brands to follow" band: the portrait (the large avatar), name, followers, follow label. card is a UserCardDto. */
function brandCard(card) {
  const user = card.user;
  const followers = (n) => t('profile.followers_n', { n: countArg(n) });
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

/** An open-challenge ticket: the hashtag first, then title and intent, then brand and when it ends. The whole ticket links to the challenge. */
function challengeCard(c) {
  return el('a', { class: 'x-challenge', href: '#/challenge/' + c.id }, [
    c.tag ? el('bdi', { class: 'x-hash', dir: 'auto', text: '#' + c.tag }) : null,
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
  root.appendChild(front());
  root.appendChild(searchForm(''));
  root.appendChild(boardStrip(ctx));   // "This week": the top three of the looks board (views/board.js); hidden while the board is empty
  const holder = el('div', {}, [skeletonCards(1)]);
  root.appendChild(holder);

  let data;
  try { data = await api('GET', '/api/explore'); }
  catch (e) { if (ctx.stale()) return; holder.replaceWith(e.status === 501 ? emptyState(t('explore.empty')) : errorBlock(e)); return; }
  if (ctx.stale()) return;

  const tags = data.trendingTags || [];
  const brands = data.brands || [];
  const looks = data.topLooks || [];
  const challenges = data.challenges || [];
  const frag = document.createDocumentFragment();
  if (!tags.length && !brands.length && !looks.length) frag.appendChild(emptyState(t('explore.empty')));
  // The first top look is the cover, right under the search; the wall starts from the second so nothing repeats.
  if (looks[0]) frag.appendChild(heroCard(looks[0]));
  if (tags.length) frag.appendChild(section(t('explore.trending'), tagIndex(tags)));
  if (brands.length) frag.appendChild(section(t('explore.brands'), el('div', { class: 'band' }, brands.map(brandCard))));
  if (looks.length > 1) frag.appendChild(section(t('explore.top'), postGrid(looks.slice(1, 7), { wall: true, captions: true })));
  // The challenges section always shows, so "All challenges" stays one tap away even before the first brief.
  frag.appendChild(section(t('explore.challenges'), [
    el('p', { class: 'muted', text: t('challenges.intro') }),
    ...(challenges.length ? challenges.map(challengeCard) : [el('p', { class: 'muted', text: t('challenges.empty') })])
  ], el('a', { href: '#/challenges', text: t('explore.all_challenges') })));
  holder.replaceWith(frag);
});

// ---------- search ----------

register('search', async (root, params, ctx) => {
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
  const posts = data.posts || [];
  if (!users.length && !tags.length && !posts.length) { holder.replaceWith(nothing()); return; }
  const frag = document.createDocumentFragment();
  if (users.length) frag.appendChild(section(t('explore.people'), el('div', {}, users.map((card) => userRow(card)))));
  if (tags.length) frag.appendChild(section(t('explore.tags'), tagIndex(tags)));
  // Looks whose stylist-named pieces match the term ("black boots"), a print grid under the people and the tags.
  if (posts.length) frag.appendChild(section(t('explore.looks_with', { q }), el('div', { id: 'search-looks', style: 'margin-inline: -14px;' }, [postGrid(posts)])));
  holder.replaceWith(frag);
});

// ---------- tag ----------

register('tag', async (root, params, ctx) => {
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
      return page;   // the page carries no total, so the count line stays hidden rather than lying
    },
    render: (post) => lookCard(post, refresh),
    empty: () => emptyState(t('tag.empty')),
    stale: ctx.stale
  });
});
