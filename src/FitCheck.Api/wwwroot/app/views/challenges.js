// Challenges: the Open / Ended list, one challenge (brief, prize, winner, leaderboard with crowd votes) and the
// brand's "new challenge" form. Ported from the Phase 2 monolith's challengesView / challengeCard / enterButton /
// challengeView / newChallengeView onto the Phase 3 kit: top bar with a back arrow, flush lists, requireSignIn()
// before every write, localized server errors shown in an alert, focus kept on the same entry after a vote.
import {
  register, state, t, api, el, icon, avatar, brandMark, postCard, setTopBar, navigate, requireSignIn, signInPrompt, emptyState, errorBlock, toast, isBrand, INTENTS, intentLabel, fmtNumber, relative, showAlert
} from '../core.js';

// The few rules the shared stylesheet does not have: an inset section inside a flush view, the ended countdown,
// the title link on a card, 44px vote buttons.
const CSS = `
.ch-section { padding-inline: 16px; }
.ch-section > * + * { margin-block-start: 10px; }
@media (min-width: 560px) { .ch-section { padding-inline: 0; } }
.challenge-card .ch-title { display: block; text-decoration: none; color: inherit; }
.challenge-card .challenge-title { overflow-wrap: anywhere; }
.challenge-meta .ch-by { color: var(--ink-2); text-decoration: none; }
.challenge-meta .avatar.sm { margin-inline-end: -4px; }
.countdown.over { color: var(--ink-3); }
.thumbs a { display: block; border-radius: 8px; overflow: hidden; }
.lb .vote { min-block-size: 44px; }
.lb-who .name { text-decoration: none; display: inline-block; max-inline-size: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; vertical-align: bottom; }
.lb-who .tag { margin-inline-start: 6px; vertical-align: middle; }
.ch-hint { font-size: 13px; color: var(--ink-3); }
.ch-form input[type="url"] { direction: ltr; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const listPath = (tab) => '#/challenges' + (tab === 'ended' ? '/ended' : '');
const challengePath = (id) => '#/challenge/' + encodeURIComponent(id);
const profilePath = (handle) => '#/u/' + encodeURIComponent(handle);
const lookLabel = (post) => t('a11y.look_by', { intent: intentLabel(post.intent), name: post.user.name });

// ---------- pieces ----------

function countdown(c) {
  return c.isOpen
    ? el('div', { class: 'countdown', text: t('challenges.ends', { when: relative(c.endsAt) }) })
    : el('div', { class: 'countdown over', text: t('challenges.ended_at', { when: relative(c.endsAt) }) });
}

/** Who set it and what it is for: the brand's avatar, "by NEXOR" (a link to the brand), the intent tag. */
function byLine(c, extra) {
  const brand = c.brand || { handle: '?', name: '?' };
  return el('div', { class: 'challenge-meta' }, [
    avatar(brand, 'sm'),
    el('a', { class: 'ch-by', href: profilePath(brand.handle), text: t('challenges.by', { name: brand.name }) }),
    el('span', { class: 'tag', text: intentLabel(c.intent) })
  ].concat(extra || []));
}

function prizeBlock(c) {
  return el('div', { class: 'prize' }, [
    icon('trophy'),
    el('div', {}, [
      el('b', { text: t('challenges.prize') + ': ' }),
      c.prize,
      c.prizeUrl ? el('span', {}, [' · ', el('a', { href: c.prizeUrl, target: '_blank', rel: 'noopener', text: t('challenges.see_prize') })]) : null
    ])
  ]);
}

function counts(c) {
  return el('div', { class: 'challenge-meta' }, [
    el('span', { text: t('challenges.entries', { n: fmtNumber(c.entries) }) }),
    el('span', { text: t('challenges.votes', { n: fmtNumber(c.votes) }) })
  ]);
}

/**
 * "Enter with a check" for people while the challenge is open. The brand never sees it on its own challenge;
 * someone who already entered sees "You're in" (a link to their entry when we know it). The pending challenge
 * and its intent go into state.check before the sign-in gate, so a visitor who signs in lands on the check
 * with the challenge already attached.
 */
function enterButton(c) {
  if (!c.isOpen) return null;
  const viewer = c.viewer || {};
  if (viewer.hasEntered) {
    return viewer.myEntryId
      ? el('a', { class: 'tag accent', href: '#/post/' + encodeURIComponent(viewer.myEntryId), text: t('challenges.entered') })
      : el('span', { class: 'tag accent', text: t('challenges.entered') });
  }
  if (viewer.isBrand) return null;
  return el('button', {
    type: 'button', class: 'btn btn-secondary', 'data-enter': c.id, text: t('challenges.enter'),
    onclick: () => {
      state.check.challenge = { id: c.id, title: c.title, intent: c.intent };
      state.check.intent = c.intent;
      if (!requireSignIn('#/check')) return;
      navigate('#/check');
    }
  });
}

/** A challenge in the list: who, the title (a link), when it ends, the prize, the counts, the top three entries. */
function challengeCard(c) {
  const top = Array.isArray(c.top) ? c.top : [];
  return el('article', { class: 'challenge-card', 'data-challenge': c.id }, [
    byLine(c, c.winnerPostId ? [el('span', { class: 'tag rose', text: t('challenges.winner') })] : null),
    el('a', { class: 'ch-title', href: challengePath(c.id) }, [el('h3', { class: 'challenge-title', text: c.title })]),
    countdown(c),
    prizeBlock(c),
    counts(c),
    top.length ? el('div', { class: 'thumbs' }, top.map((p) => el('a', { href: '#/post/' + encodeURIComponent(p.id), 'aria-label': lookLabel(p) }, [
      el('img', { src: p.imageUrl, alt: '', loading: 'lazy', decoding: 'async' })
    ]))) : null,
    enterButton(c)
  ]);
}

function skeletonChallenge() {
  return el('div', { class: 'challenge-card', 'aria-hidden': 'true' }, [
    el('div', { class: 'skel', style: 'block-size: 12px; inline-size: 40%;' }),
    el('div', { class: 'skel', style: 'block-size: 24px; inline-size: 75%;' }),
    el('div', { class: 'skel', style: 'block-size: 12px; inline-size: 55%;' }),
    el('div', { class: 'skel', style: 'block-size: 48px; inline-size: 100%;' })
  ]);
}

/** The two segments. Tapping the other one navigates; the current one is a no-op. */
function segments(tab) {
  return el('div', { class: 'segments', role: 'group', 'aria-label': t('challenges.title') }, ['open', 'ended'].map((name) => el('button', {
    type: 'button', class: 'segment', 'data-tab': name, 'aria-pressed': String(name === tab), text: t('challenges.' + name),
    onclick: () => { if (name !== tab) navigate(listPath(name)); }
  })));
}

// ---------- list ----------

register('challenges', async (root, params, ctx) => {
  ensureStyle();
  const tab = params.tab === 'ended' ? 'ended' : 'open';
  root.classList.add('flush');
  setTopBar({ back: '#/explore', title: t('challenges.title') });
  root.appendChild(el('div', { class: 'sticky-tabs' }, [
    el('h1', { class: 'sr-only', text: t('challenges.title') }),
    segments(tab)
  ]));
  if (isBrand()) {
    root.appendChild(el('div', { class: 'ch-section' }, [el('a', { class: 'btn', id: 'new-challenge', href: '#/new-challenge', text: t('challenges.new') })]));
  }
  const holder = el('div', { class: 'stack' }, [skeletonChallenge(), skeletonChallenge()]);
  root.appendChild(holder);

  let items;
  try { items = await api('GET', '/api/challenges?state=' + tab); }
  catch (e) { if (ctx.stale()) return; holder.replaceWith(errorBlock(e)); return; }
  if (ctx.stale()) return;
  if (!Array.isArray(items)) items = [];
  if (!items.length) {
    holder.replaceWith(emptyState(t(tab === 'ended' ? 'challenges.empty_ended' : 'challenges.empty'), tab === 'ended' ? null : t('challenges.intro')));
    return;
  }
  holder.replaceWith(el('div', { class: 'stack', id: 'challenge-list' }, items.map(challengeCard)));
});

// ---------- one challenge ----------

register('challenge', async (root, params, ctx) => {
  ensureStyle();
  root.classList.add('flush');
  setTopBar({ back: true, title: t('challenges.title') });
  const id = params.id;
  const holder = el('div', { class: 'stack' }, [skeletonChallenge()]);
  root.appendChild(holder);

  let detail;
  try {
    // A malformed id never reaches the API's {id:guid} route (its bare 404 has no localized body), so answer it here.
    if (!id || !GUID.test(id)) throw Object.assign(new Error(t('common.not_found')), { status: 404 });
    detail = await api('GET', '/api/challenges/' + encodeURIComponent(id));
    if (!detail || !detail.challenge) throw Object.assign(new Error(t('common.not_found')), { status: 404 });
  } catch (e) {
    if (ctx.stale()) return;
    holder.replaceWith(e && e.status === 404 ? emptyState(e.message || t('common.not_found')) : errorBlock(e));
    return;
  }
  if (ctx.stale()) return;
  holder.remove();
  setTopBar({ back: true, title: detail.challenge.title });
  draw();

  /** The winner's look card, redrawn when it changes (featured / unfeatured); the page reloads if it is deleted. */
  function winnerCard(post) {
    const node = postCard(post, {
      inChallenge: true, votes: post.votes === undefined ? 0 : post.votes,
      onDelete: () => navigate(challengePath(id)), onChange: () => node.replaceWith(winnerCard(post))
    });
    return node;
  }

  /** Applies a confirmed vote to the data we already have: counts, the viewer's vote, the order. No second request. */
  function applyVote(result, entry, wasMine) {
    const c = detail.challenge;
    const entries = detail.entriesByVotes;
    const previous = c.viewer.votedPostId;
    if (wasMine) {
      entry.votes = result.votes;
    } else {
      entry.votes = result.votes;
      if (previous && previous !== entry.id) {
        const before = entries.find((x) => x.id === previous);
        if (before) before.votes = Math.max(0, before.votes - 1);
      }
    }
    c.viewer.votedPostId = result.votedPostId === undefined ? null : result.votedPostId;
    c.votes = entries.reduce((sum, x) => sum + (x.votes || 0), 0);
    entries.sort((a, b) => (b.votes - a.votes) || (new Date(a.createdAt) - new Date(b.createdAt)));
  }

  function voteControl(entry) {
    const c = detail.challenge;
    const viewer = c.viewer || {};
    const mine = viewer.votedPostId === entry.id;
    if (!c.isOpen && !mine) return null;   // an ended board only marks the entry you voted for
    const btn = el('button', {
      type: 'button', class: 'vote', 'data-post': entry.id, 'aria-pressed': String(mine),
      disabled: !c.isOpen || entry.isMine || viewer.isBrand,
      text: mine ? t('post.voted') : t('post.vote'),
      onclick: async () => {
        if (!requireSignIn()) return;
        btn.disabled = true;
        try {
          const result = mine
            ? await api('DELETE', '/api/challenges/' + encodeURIComponent(id) + '/vote')
            : await api('POST', '/api/challenges/' + encodeURIComponent(id) + '/vote', { postId: entry.id });
          if (ctx.stale()) return;
          applyVote(result || {}, entry, mine);
          draw();
          const again = root.querySelector('.vote[data-post="' + entry.id + '"]');
          if (again) again.focus({ preventScroll: true });
        } catch (e) {
          if (ctx.stale()) return;
          btn.disabled = false;
          toast(e.message);
        }
      }
    });
    return btn;
  }

  function leaderboardRow(entry, index) {
    const c = detail.challenge;
    const winner = c.winnerPostId === entry.id;
    return el('div', { class: 'lb-row' + (winner ? ' winner' : ''), 'data-entry': entry.id }, [
      el('span', { class: 'lb-rank', text: fmtNumber(index + 1) }),
      el('a', { href: '#/post/' + encodeURIComponent(entry.id), 'aria-label': lookLabel(entry) }, [
        el('img', { src: entry.imageUrl, alt: '', loading: 'lazy', decoding: 'async' })
      ]),
      el('div', { class: 'lb-who' }, [
        el('div', {}, [
          el('a', { class: 'name', href: profilePath(entry.user.handle) }, [entry.user.name, brandMark(entry.user)]),
          winner ? el('span', { class: 'tag rose', text: t('challenges.winner') }) : null
        ]),
        el('div', { class: 'votes' }, [
          t('post.votes', { n: fmtNumber(entry.votes === undefined ? 0 : entry.votes) }) + ' · ',
          el('bdi', { dir: 'ltr', text: fmtNumber(entry.score) + t('result.out_of') })
        ])
      ]),
      voteControl(entry)
    ]);
  }

  function draw() {
    const c = detail.challenge;
    const entries = Array.isArray(detail.entriesByVotes) ? detail.entriesByVotes : [];
    const nodes = [];

    nodes.push(el('article', { class: 'challenge-card', 'data-challenge': c.id }, [
      byLine(c),
      el('h1', { class: 'challenge-title', text: c.title }),
      countdown(c),
      el('div', {}, [el('h2', { text: t('challenges.brief') }), el('p', { class: 'lede', style: 'margin-block-start: 4px; white-space: pre-line; overflow-wrap: anywhere;', text: c.brief })]),
      prizeBlock(c),
      counts(c),
      enterButton(c)
    ]));

    if (!c.isOpen) {
      nodes.push(el('section', { class: 'ch-section', 'aria-labelledby': 'ch-winner-title' }, [
        el('h2', { id: 'ch-winner-title', text: t('challenges.winner') }),
        detail.winner || entries.length ? null : el('p', { class: 'muted', text: t('challenges.no_winner') })
      ]));
      if (detail.winner) nodes.push(winnerCard(detail.winner));
    }

    const board = el('section', { class: 'ch-section', 'aria-labelledby': 'ch-board-title' }, [
      el('h2', { id: 'ch-board-title', text: t('challenges.leaderboard') }),
      !state.me && c.isOpen && entries.length ? el('p', { class: 'ch-hint', text: t('challenges.vote_signin') }) : null,
      entries.length
        ? el('div', { class: 'lb', id: 'leaderboard' }, entries.map(leaderboardRow))
        : emptyState(t('challenges.no_entries'))
    ]);
    nodes.push(board);
    root.replaceChildren(...nodes);
  }
});

// ---------- new challenge (brands) ----------

register('new-challenge', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: true, title: t('newchallenge.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('newchallenge.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (!isBrand()) {
    // A person landed here: explain what a brand account is and point at the switch in settings.
    root.appendChild(el('div', { class: 'notice' }, [
      el('h3', { text: t('settings.brand') }),
      el('p', { class: 'muted', text: t('settings.brand_hint') }),
      el('a', { class: 'btn btn-secondary', href: '#/settings', style: 'margin-block-start: 14px;', text: t('profile.settings') })
    ]));
    return;
  }

  let intent = 'Casual';
  const chips = el('div', { class: 'chips', role: 'group', 'aria-label': t('newchallenge.intent') }, INTENTS.map((i) => el('button', {
    type: 'button', class: 'chip', 'data-intent': i, 'aria-pressed': String(i === intent), text: intentLabel(i),
    onclick: () => { intent = i; for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === intent)); }
  })));
  const title = el('input', { type: 'text', maxlength: '80', id: 'nc-title', autocomplete: 'off' });
  const brief = el('textarea', { maxlength: '500', id: 'nc-brief', placeholder: t('newchallenge.brief_placeholder') });
  const prize = el('input', { type: 'text', maxlength: '200', id: 'nc-prize', placeholder: t('newchallenge.prize_placeholder'), autocomplete: 'off' });
  const prizeUrl = el('input', { type: 'url', maxlength: '500', id: 'nc-url', placeholder: 'https://', inputmode: 'url', autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false' });
  const ends = el('input', { type: 'datetime-local', id: 'nc-ends' });
  // datetime-local wants local wall-clock time without a zone; default to three days from now, on the hour.
  const toLocal = (d) => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  const inThreeDays = new Date(Date.now() + 3 * 86400000);
  inThreeDays.setMinutes(0, 0, 0);
  ends.value = toLocal(inThreeDays);
  ends.min = toLocal(new Date(Date.now() + 3600000));
  ends.max = toLocal(new Date(Date.now() + 60 * 86400000));

  const error = el('p', { class: 'alert danger', role: 'alert', id: 'nc-error', tabindex: '-1', hidden: true });
  const submit = el('button', { type: 'submit', class: 'btn', id: 'nc-submit', text: t('newchallenge.submit') });
  let busy = false;
  const form = el('form', {
    class: 'stack ch-form', novalidate: true, id: 'new-challenge-form',
    onsubmit: async (event) => {
      event.preventDefault();
      if (busy) return;
      busy = true; submit.disabled = true; error.hidden = true;
      try {
        // An empty or unparsable date goes as null: the server answers with its localized "between 1 hour and 60 days" message.
        const when = ends.value ? new Date(ends.value) : null;
        const created = await api('POST', '/api/challenges', {
          title: title.value.trim(),
          brief: brief.value.trim(),
          intent,
          prize: prize.value.trim(),
          prizeUrl: prizeUrl.value.trim() || null,
          endsAt: when && !Number.isNaN(when.getTime()) ? when.toISOString() : null
        });
        navigate(challengePath(created.id));
      } catch (e) {
        busy = false; submit.disabled = false;
        if (ctx.stale()) return;
        showAlert(error, e.message);
        error.focus({ preventScroll: false });
      }
    }
  }, [
    el('div', { class: 'field' }, [el('label', { for: 'nc-title', text: t('newchallenge.name') }), title]),
    el('div', { class: 'field' }, [el('span', { class: 'label', id: 'nc-intent-label', text: t('newchallenge.intent') }), chips]),
    el('div', { class: 'field' }, [el('label', { for: 'nc-brief', text: t('newchallenge.brief') }), brief]),
    el('div', { class: 'field' }, [el('label', { for: 'nc-prize', text: t('newchallenge.prize') }), prize]),
    el('div', { class: 'field' }, [el('label', { for: 'nc-url', text: t('newchallenge.prize_url') }), prizeUrl]),
    el('div', { class: 'field' }, [el('label', { for: 'nc-ends', text: t('newchallenge.ends') }), ends, el('p', { class: 'hint', text: t('newchallenge.hint') })]),
    error,
    submit
  ]);
  root.appendChild(form);
});
