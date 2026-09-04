// Profiles: #/u/:handle[/community|/featured] and #/me, plus the two private lists that hang off my profile,
// #/saved and #/checks. The head (avatar, name, bio, website, six stats) is padded; the look grids under the
// tabs run edge to edge and page as you scroll. Tabs are routes, so back returns to the previous tab.
import {
  register, state, t, api, el, icon, avatar, brandMark, handleText, postGrid, followButton, infiniteList, setTopBar,
  navigate, signInPrompt, emptyState, errorBlock, signOut, isMe, fmtNumber, fmtCompact, fmtDate, intentLabel
} from '../core.js';

/** Three columns, so a page is whole rows; the server caps pages at 30 anyway. */
const GRID_PAGE = 30;

// The kit's empty, error and skeleton blocks are grid children here, so they span the row; check rows are this view's only own layout.
document.head.appendChild(el('style', { text: [
  '.profile-grid > .empty, .profile-grid > .notice { grid-column: 1 / -1; }',
  '.check-row { display: flex; align-items: center; gap: 12px; padding-block: 12px; border-block-end: 1px solid var(--line); }',
  '.check-row .num { flex: none; min-inline-size: 48px; text-align: center; font-family: var(--font-display); font-size: 28px; line-height: 1; font-weight: 800; color: var(--accent); direction: ltr; }',
  '.check-row .num small { font-size: 11px; color: var(--ink-3); font-weight: 600; margin-inline-start: 1px; }',
  '.check-row .info { flex: 1; min-inline-size: 0; }',
  '.check-row .info > * + * { margin-block-start: 4px; }',
  '.check-row .pill { flex: none; min-block-size: 44px; }'
].join('\n') }));

/** "@handle" as an isolated left-to-right run, so it never turns into "handle@" in a Hebrew top bar. */
const handleTitle = (handle) => '\u2066@' + handle + '\u2069';
const tabPath = (handle, tab) => '#/u/' + encodeURIComponent(handle) + (tab === 'looks' ? '' : '/' + tab);
const settingsAction = () => el('a', { class: 'icon-btn', href: '#/settings', 'aria-label': t('profile.settings') }, [icon('settings')]);

function stat(value, label, hot) {
  return el('div', { class: 'stat' + (hot ? ' hot' : '') }, [el('b', { text: value }), el('span', { text: label })]);
}

function headSkeleton() {
  return el('div', { 'aria-hidden': 'true', style: 'padding-inline: 16px;' }, [
    el('div', { class: 'profile-head' }, [
      el('div', { class: 'skel', style: 'inline-size: 84px; block-size: 84px; border-radius: 50%; flex: none;' }),
      el('div', { class: 'who' }, [
        el('div', { class: 'skel', style: 'block-size: 22px; inline-size: 55%;' }),
        el('div', { class: 'skel', style: 'block-size: 12px; inline-size: 35%; margin-block-start: 10px;' })
      ])
    ]),
    el('div', { class: 'stats', style: 'margin-block-start: 18px;' }, [0, 1, 2, 3, 4, 5].map(() => el('div', { class: 'skel', style: 'block-size: 58px;' })))
  ]);
}

function gridSkeleton() {
  return el('div', { class: 'grid', 'aria-hidden': 'true' }, [0, 1, 2, 3, 4, 5].map(() => el('div', { class: 'skel', style: 'aspect-ratio: 4 / 5; border-radius: 0;' })));
}

/** A paged three-column grid of looks over a FeedDto endpoint. opts: path, empty(), stale(). */
function gridList(container, opts) {
  const skel = gridSkeleton();
  container.appendChild(skel);
  return infiniteList(container, {
    className: 'grid profile-grid',
    skeleton: false,
    load: async (offset) => {
      const query = new URLSearchParams({ offset: String(offset), limit: String(GRID_PAGE) });
      try { return await api('GET', opts.path + '?' + query.toString()); } finally { skel.remove(); }
    },
    render: (post) => postGrid([post]).firstElementChild,
    empty: opts.empty,
    stale: opts.stale
  });
}

function websiteLink(url) {
  return el('a', { class: 'challenge-link', href: url, target: '_blank', rel: 'noopener', style: 'display: inline-block; margin-block-start: 6px;', 'aria-label': t('profile.website') }, [
    el('bdi', { dir: 'ltr', text: url.replace(/^https?:\/\//i, '') })
  ]);
}

/** The Looks / Community / Featured strip. Tapping another tab navigates, so back returns here. */
function tabStrip(handle, available, current) {
  return el('div', { class: 'profile-tabs', role: 'tablist' }, available.map((name) => el('button', {
    type: 'button', role: 'tab', 'aria-selected': String(name === current), 'data-tab': name, text: t('profile.tab_' + name),
    onclick: () => { if (name !== current) navigate(tabPath(handle, name)); }
  })));
}

function tabEmpty(profile, tab, mine) {
  const brand = profile.accountType === 'Brand';
  if (tab === 'community') return emptyState(t('profile.community_empty', { handle: profile.handle }), mine ? null : t('profile.community_hint', { handle: profile.handle }));
  if (tab === 'featured') return emptyState(t(brand ? 'profile.featured_empty_brand' : 'profile.featured_empty_person'));
  return emptyState(t(mine ? 'profile.no_posts_me' : 'profile.no_posts'));
}

async function profileView(root, handle, tab, ctx) {
  const mine = isMe(handle);
  setTopBar({ back: !mine, title: handleTitle(handle || ''), actions: mine ? [settingsAction()] : [] });
  if (!handle) { root.appendChild(emptyState(t('profile.not_found'))); return; }
  root.classList.add('flush');
  const skel = headSkeleton();
  root.appendChild(skel);

  let profile;
  try { profile = await api('GET', '/api/users/' + encodeURIComponent(handle)); }
  catch (e) {
    if (ctx.stale()) return;
    skel.remove();
    if (e.status === 404) { root.appendChild(emptyState(t('profile.not_found'))); return; }
    root.appendChild(el('div', { style: 'padding-inline: 16px;' }, [errorBlock(e)]));
    return;
  }
  if (ctx.stale()) return;
  skel.remove();
  if (profile.handle !== handle) setTopBar({ back: !mine, title: handleTitle(profile.handle), actions: mine ? [settingsAction()] : [] });

  const brand = profile.accountType === 'Brand';
  const viewer = profile.viewer || {};
  const head = el('div', { class: 'profile-head' }, [
    avatar(profile, { size: 'lg', noLink: true }),
    el('div', { class: 'who' }, [
      el('h1', { class: 'name' }, [profile.name, brandMark(profile)]),
      el('div', { class: 'sub' }, [handleText(profile.handle)]),
      profile.bio ? el('p', { class: 'caption', dir: 'auto', style: 'margin-block-start: 6px;', text: profile.bio }) : null,
      profile.website ? websiteLink(profile.website) : null
    ])
  ]);

  // The followers figure is kept by hand so the follow button can update it without a reload.
  const followers = el('b', { text: fmtCompact(profile.followers) });
  const stats = el('div', { class: 'stats' }, [
    stat(fmtCompact(profile.posts), t('profile.posts')),
    el('div', { class: 'stat' }, [followers, el('span', { text: t('profile.followers') })]),
    stat(fmtCompact(profile.following), t('profile.following')),
    stat(fmtCompact(profile.fireReceived), t('profile.fire'), true),
    stat(profile.bestScore === undefined || profile.bestScore === null ? '–' : fmtNumber(profile.bestScore), t('profile.best')),
    stat(fmtNumber(profile.streak), t('profile.streak'), profile.streak > 1)
  ]);

  let actions;
  if (mine) {
    actions = el('div', { class: 'links' }, [
      el('a', { href: '#/saved' }, [t('profile.saved'), icon('bookmark')]),
      el('a', { href: '#/checks' }, [t('profile.checks'), icon('camera')]),
      el('button', { type: 'button', id: 'profile-logout', text: t('auth.logout'), onclick: () => signOut() })
    ]);
  } else {
    const follow = followButton(profile.handle, !!viewer.following, (result) => {
      profile.followers = result.followers;
      followers.textContent = fmtCompact(result.followers);
    });
    follow.id = 'follow';
    follow.classList.remove('btn-sm');   // full width under the stats, like the check button on a card
    actions = el('div', {}, [follow]);
  }
  root.appendChild(el('div', { class: 'stack', style: 'padding-inline: 16px;' }, [head, stats, actions]));

  // Looks for everyone; Community for brands; Featured for brands and for people a brand has featured.
  const available = ['looks'];
  if (brand) available.push('community');
  if (brand || (profile.featured || 0) > 0) available.push('featured');
  const current = available.includes(tab) ? tab : 'looks';
  const body = el('div', { role: 'tabpanel', 'aria-label': t('profile.tab_' + current) });
  root.appendChild(el('div', {}, [tabStrip(profile.handle, available, current), body]));
  gridList(body, {
    path: '/api/users/' + encodeURIComponent(profile.handle) + (current === 'looks' ? '/posts' : '/' + current),
    empty: () => tabEmpty(profile, current, mine),
    stale: ctx.stale
  });
}

register('user', (root, params, ctx) => profileView(root, params.handle, params.tab, ctx));

register('me', async (root, params, ctx) => {
  if (!state.me) {
    root.appendChild(el('h1', { text: t('nav.profile') }));
    root.appendChild(signInPrompt());
    return;
  }
  await profileView(root, state.me.handle, 'looks', ctx);
});

register('saved', async (root, params, ctx) => {
  setTopBar({ back: '#/me', title: t('saved.title') });
  const heading = el('h1', { class: 'sr-only', text: t('saved.title') });
  if (!state.me) { root.appendChild(heading); root.appendChild(signInPrompt()); return; }
  root.classList.add('flush');
  const body = el('div');
  root.appendChild(el('div', {}, [heading, body]));
  gridList(body, { path: '/api/users/me/saved', empty: () => emptyState(t('saved.empty')), stale: ctx.stale });
});

/** One ok check: score, intent, posted or private, headline, date, and the way to the look or to posting it. */
function checkRow(check) {
  const posted = !!check.postId;
  const headline = check.feedback && check.feedback.headline;
  return el('li', { class: 'check-row' }, [
    el('div', { class: 'num', role: 'img', 'aria-label': t('a11y.score', { score: fmtNumber(check.score) }) }, [fmtNumber(check.score), el('small', { text: t('result.out_of') })]),
    el('div', { class: 'info' }, [
      el('div', { class: 'chips', style: 'gap: 6px;' }, [
        el('span', { class: 'tag', text: intentLabel(check.intent) }),
        el('span', { class: 'tag' + (posted ? ' accent' : ''), text: posted ? t('result.posted') : t('checks.private') })
      ]),
      headline ? el('div', { class: 'headline', dir: 'auto', style: 'font-size: 15px;', text: headline }) : null,
      el('div', { class: 'hint', text: fmtDate(check.createdAt) })
    ]),
    posted
      ? el('a', { class: 'pill', href: '#/post/' + encodeURIComponent(check.postId), text: t('result.view_post') })
      : el('button', {
        type: 'button', class: 'pill accent', text: t('result.post'),
        onclick: () => { state.result = check; state.resultAnimated = true; state.resultPostId = null; navigate('#/result'); }
      })
  ]);
}

register('checks', async (root, params, ctx) => {
  setTopBar({ back: '#/me', title: t('checks.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('checks.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  const skel = el('div', { 'aria-hidden': 'true' }, [0, 1, 2].map(() => el('div', { class: 'check-row' }, [
    el('div', { class: 'skel', style: 'inline-size: 48px; block-size: 34px;' }),
    el('div', { class: 'info' }, [el('div', { class: 'skel', style: 'block-size: 14px; inline-size: 60%;' }), el('div', { class: 'skel', style: 'block-size: 12px; inline-size: 35%;' })])
  ])));
  root.appendChild(skel);

  let checks;
  try { checks = await api('GET', '/api/users/me/checks'); }
  catch (e) { if (ctx.stale()) return; skel.remove(); throw e; }
  if (ctx.stale()) return;
  skel.remove();

  // Only checks that produced a score can be posted or looked at; refusals stay out of sight.
  const ok = (checks || []).filter((check) => check.status === 'ok');
  if (ok.length === 0) { root.appendChild(emptyState(t('checks.empty'))); return; }
  root.appendChild(el('ul', {}, ok.map(checkRow)));
});
