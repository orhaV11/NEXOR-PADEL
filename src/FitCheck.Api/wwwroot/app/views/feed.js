// Home: "For you" / "Following" segments, an intent chip row under them, then an edge-to-edge look list that
// pages as you scroll and refreshes when you pull down. The intent filter lives in state.feed so it survives a
// trip to a post and back; the tab comes from the route (#/ or #/feed/following).
import {
  register, state, t, api, el, INTENTS, PAGE, intentLabel, postCard, infiniteList, pullToRefresh, installBanner,
  signInPrompt, emptyState, announce
} from '../core.js';

const feedPath = (tab) => (tab === 'following' ? '#/feed/following' : '#/');

function feedQuery(tab, intent, offset) {
  const q = new URLSearchParams({ tab, offset: String(offset), limit: String(PAGE) });
  if (intent) q.set('intent', intent);
  return '/api/feed?' + q.toString();
}

/** The two segments. Tapping the other one navigates; tapping the current one is a no-op (the Home tab refreshes). */
function segments(tab) {
  return el('div', { class: 'segments', role: 'group', 'aria-label': t('feed.title') }, ['foryou', 'following'].map((name) => el('button', {
    type: 'button', class: 'segment', 'data-tab': name, 'aria-pressed': String(name === tab), text: t('feed.' + name),
    onclick: () => { if (name !== tab) location.hash = feedPath(name); }
  })));
}

/** All + every intent as a horizontally scrolling chip row. onChange(intent) runs when the selection changes. */
function intentChips(onChange) {
  const chips = el('div', { class: 'chips scroll', role: 'group', 'aria-label': t('check.intent_label') });
  const paint = () => { for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === state.feed.intent)); };
  const pick = (intent) => {
    if (intent === state.feed.intent) return;
    state.feed.intent = intent;
    paint();
    onChange(intent);
  };
  chips.appendChild(el('button', { type: 'button', class: 'chip', 'data-intent': '', text: t('feed.all_intents'), onclick: () => pick('') }));
  for (const intent of INTENTS) {
    chips.appendChild(el('button', { type: 'button', class: 'chip', 'data-intent': intent, text: intentLabel(intent), onclick: () => pick(intent) }));
  }
  paint();
  return chips;
}

register('feed', async (root, params, ctx) => {
  const tab = params.tab === 'following' ? 'following' : 'foryou';
  state.feed.tab = tab;
  root.classList.add('flush');

  // The sticky block: a heading for screen readers and focus, the segments, the intent chips.
  const chips = intentChips(() => { if (list) list.refresh(); });
  root.appendChild(el('div', { class: 'sticky-tabs' }, [
    el('h1', { class: 'sr-only', text: t('feed.title') }),
    segments(tab),
    chips
  ]));

  // Following is personal; the rest of Home is public.
  if (tab === 'following' && !state.me) {
    chips.hidden = true;
    root.appendChild(el('div', { style: 'padding-inline: 16px;' }, [signInPrompt(undefined, t('feed.following_locked'))]));
    return;
  }

  const banner = installBanner();
  if (banner) root.appendChild(banner);

  // The pull indicator and the list share one block so the view rhythm adds a single gap under the chips.
  const body = el('div');
  const indicator = el('div', { class: 'ptr' });
  body.appendChild(indicator);
  root.appendChild(body);

  const empty = () => {
    if (tab !== 'following') return emptyState(t('feed.empty'));
    return el('div', {}, [
      emptyState(t('feed.empty_following')),
      el('div', { style: 'padding-inline: 16px; text-align: center;' }, [el('a', { class: 'btn btn-secondary btn-sm', href: '#/explore', text: t('explore.brands') })])
    ]);
  };

  let list = null;
  list = infiniteList(body, {
    load: (offset) => api('GET', feedQuery(tab, state.feed.intent, offset)),
    render: (post) => postCard(post, { onDelete: () => list.refresh() }),
    empty,
    stale: ctx.stale
  });

  pullToRefresh(indicator, async () => {
    await list.refresh();
    if (!ctx.stale()) announce(t('common.refreshed'));
  });

  // Bring a remembered filter into view when it sits off the end of the chip row.
  if (state.feed.intent) {
    requestAnimationFrame(() => {
      if (ctx.stale()) return;
      const pressed = chips.querySelector('.chip[aria-pressed="true"]');
      if (pressed && pressed.scrollIntoView) pressed.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  }
});
