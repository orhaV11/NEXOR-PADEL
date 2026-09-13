// Looks by item (Round 10): #/items/<brand> and #/items/<brand>/<category> (a brand page, a category under it) and
// #/items?q=<term> (a free search by piece, brand or model), from GET /api/items?brand=&category=&q=. A header "Looks
// with {brand · category}", a search rule (#items-search, submits to #/items?q=), the category chips on a brand page
// (#items-cats), a link to the brand's own account when one matches the name (#items-more, from GET /api/items/brands?q=),
// the grid of looks (#items-grid, a print grid that pages as you scroll) and the empty state. Until the route is built the
// server answers 501 and the page says the feature is coming rather than showing an error.
import { register, t, api, el, icon, setTopBar, navigate, emptyState, errorBlock, skeletonCards, infiniteList, postGrid, avatar, brandMark, PAGE } from '../core.js';
import { CATEGORIES, categoryLabel } from '../items.js';

/** The search rule: submitting goes to #/items?q=<term>; nothing happens while typing. */
function searchForm(value) {
  const input = el('input', {
    type: 'search', id: 'items-search', name: 'q', value: value || '', placeholder: t('items.search_placeholder'), 'aria-label': t('items.search_placeholder'),
    autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'search', maxlength: '40'   // ItemEndpoints.QueryMaxLength, like Explore's search
  });
  const onsubmit = (event) => {
    event.preventDefault();
    const q = input.value.trim();
    if (!q) { input.focus(); return; }
    navigate('#/items?q=' + encodeURIComponent(q));
  };
  return el('form', { class: 'search', role: 'search', onsubmit }, [icon('search'), input]);
}

/** A brand page's categories as chips: "All" first, then the seven; the current one pressed. */
function categoryChips(brand, current) {
  const chip = (category) => el('a', {
    class: 'chip', 'data-category': category || 'all', 'aria-pressed': String((category || '') === current),
    href: '#/items/' + encodeURIComponent(brand) + (category ? '/' + category : ''), text: category ? categoryLabel(category) : t('items.all')
  });
  return el('div', { class: 'chips scroll items-cats', id: 'items-cats', role: 'group', 'aria-label': t('items.category') }, [chip(''), ...CATEGORIES.map(chip)]);
}

/** One print of the grid: the same cell postGrid draws, so the ring, the clip glyph and the labels match Explore's. */
const gridCell = (post) => postGrid([post]).firstElementChild;

const query = (params) => Object.entries(params).filter(([, v]) => v !== '' && v !== null && v !== undefined).map(([k, v]) => k + '=' + encodeURIComponent(v)).join('&');

register('items', async (root, params, ctx) => {
  const q = (new URLSearchParams(location.hash.split('?')[1] || '').get('q') || '').trim();
  const brand = (params.brand || '').trim();
  const category = CATEGORIES.includes(params.category) ? params.category : '';
  const titleFor = (name) => (name ? t('items.search_title', { q: name + (category ? ' · ' + categoryLabel(category) : '') }) : q ? t('items.search_title', { q }) : t('items.title'));
  const title = titleFor(brand);
  setTopBar({ back: '#/explore', title });
  const heading = el('h1', { class: 'sr-only', text: title });
  root.appendChild(heading);
  root.appendChild(searchForm(q));
  if (brand) root.appendChild(categoryChips(brand, category));
  const more = el('div', { id: 'items-more', hidden: true });
  root.appendChild(more);

  if (!brand && !q) { root.appendChild(emptyState(t('items.title'), t('items.search_hint'))); return; }

  // The brand's own account, when one matches the name: "More looks with {brand}" goes to its profile.
  if (brand) {
    api('GET', '/api/items/brands?q=' + encodeURIComponent(brand)).then((data) => {
      if (ctx.stale()) return;
      const match = ((data && data.items) || []).find((b) => b && b.account && typeof b.name === 'string' && b.name.toLowerCase() === brand.toLowerCase());
      if (!match) return;
      const user = match.account;
      more.appendChild(el('a', { class: 'items-more-link', href: '#/u/' + encodeURIComponent(user.handle) }, [
        avatar(user, { size: 'sm', noLink: true }),
        el('span', { class: 'txt' }, [el('b', { text: t('items.more_looks', { brand: match.name }) }), brandMark(user)]),
        el('span', { class: 'icon', icon: 'back' })
      ]));
      more.hidden = false;
    }).catch(() => { /* 501 until the route lands: no link */ });
  }

  const holder = el('div', { class: 'items-results', id: 'items-results' }, [skeletonCards(1)]);
  root.appendChild(holder);
  const load = async (offset) => {
    const page = await api('GET', '/api/items?' + query({ brand, category, q, offset, limit: PAGE }));
    return { items: (page && page.posts) || [], nextOffset: page ? page.nextOffset : null, brand: page && page.brand };
  };

  let first;
  try { first = await load(0); }
  catch (e) {
    if (ctx.stale()) return;
    // A 400 is the server refusing the term (too long, say): its message, not an empty state that would read as "no looks".
    holder.replaceChildren(e && e.status === 501 ? emptyState(t('items.coming')) : e && e.status === 400 ? el('p', { class: 'alert danger', role: 'alert', text: e.message || t('error.generic') }) : errorBlock(e));
    return;
  }
  if (ctx.stale()) return;
  holder.replaceChildren();
  // The server's own spelling of the brand ("Nike" for #/items/nike) names the page.
  if (brand && first.brand && first.brand !== brand) {
    const fixed = titleFor(first.brand);
    heading.textContent = fixed;
    setTopBar({ back: '#/explore', title: fixed });
  }
  infiniteList(holder, {
    className: 'grid',
    initial: first,
    load,
    render: gridCell,
    empty: () => emptyState(t('items.empty')),
    endText: false,
    stale: ctx.stale
  });
  const grid = holder.querySelector('.grid');
  if (grid) grid.id = 'items-grid';
});
