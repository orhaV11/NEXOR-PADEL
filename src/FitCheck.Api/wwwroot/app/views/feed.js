// Home: the "For you" / "Your circle" switch, an intent chip row under it, then an edge-to-edge look list that
// pages as you scroll and refreshes when you pull down. The intent filter lives in state.feed so it survives a
// trip to a post and back; the tab comes from the route (#/ or #/feed/following).
import {
  register, state, t, api, el, INTENTS, PAGE, intentLabel, postCard, infiniteList, pullToRefresh, installBanner, signInPrompt, emptyState, announce, onLeave, feedVersion
} from '../core.js';

const feedPath = (tab) => (tab === 'following' ? '#/feed/following' : '#/');
// The last list per tab and filter, with its scroll position, so Back from a look lands where the reader was.
const cache = new Map();
const CACHE_TTL = 10 * 60 * 1000;

function feedQuery(tab, intent, offset) {
  const q = new URLSearchParams({ tab, offset: String(offset), limit: String(PAGE) });
  if (intent) q.set('intent', intent);
  return '/api/feed?' + q.toString();
}

// The feed switch is built from the two halves of the mark: the flame is "For you" (what's catching fire), the ring is
// "Your circle" (the people you follow). The current one is lit with the gradient; the other sits cool beside it. No
// underline, no sliding thumb: switching re-renders the view, and the newly lit one pops in.
const GLYPH = { foryou: 'flameFill', following: 'ring' };
let justSwitched = false;

/** The two segments. Tapping the other one navigates; tapping the current one is a no-op (the Home tab refreshes). */
function segments(tab) {
  const lit = justSwitched;
  justSwitched = false;
  return el('div', { class: 'segments', role: 'group', 'aria-label': t('feed.title') }, ['foryou', 'following'].map((name) => el('button', {
    type: 'button', class: 'segment' + (lit && name === tab ? ' just-lit' : ''), 'data-tab': name, 'aria-pressed': String(name === tab),
    onclick: () => { if (name !== tab) { justSwitched = true; location.hash = feedPath(name); } }
  }, [
    el('span', { class: 'glyph', icon: GLYPH[name] }),
    el('span', { text: t('feed.' + name) })
  ])));
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

  const cacheKey = () => tab + '|' + state.feed.intent;
  const remembered = cache.get(cacheKey());
  const forced = state.forceRefresh;
  state.forceRefresh = false;
  const restore = !!remembered && !forced && remembered.version === feedVersion.n && Date.now() - remembered.at < CACHE_TTL;
  if (!restore) cache.delete(cacheKey());

  let list = null;
  list = infiniteList(body, {
    load: (offset) => api('GET', feedQuery(tab, state.feed.intent, offset)),
    render: (post) => postCard(post, { onDelete: () => list.refresh() }),
    empty,
    key: (post) => post.id,
    initial: restore ? { items: remembered.items, nextOffset: remembered.nextOffset } : undefined,
    stale: ctx.stale
  });
  if (restore) requestAnimationFrame(() => requestAnimationFrame(() => { if (!ctx.stale()) window.scrollTo(0, remembered.scrollY); }));
  onLeave(() => {
    const snap = list.snapshot();
    if (snap.items.length) cache.set(cacheKey(), { ...snap, scrollY: window.scrollY, at: Date.now(), version: feedVersion.n });
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
