// Looks by item: #/items/<brand>/<category> (a brand page, a category under it) and #/items?q=<term> (a free search),
// from GET /api/items. Filled in by the items-client builder, together with the post sheet's tagging (chips, brand
// autocomplete from GET /api/items/brands?q=, the dot on the preview), the post view's "The look" list, the dots
// behind the tag toggle and the item sheet with "Shop at <host>" through /api/items/{id}/out.
import { register, t, el, setTopBar, emptyState } from '../core.js';

register('items', async (root, params) => {
  const q = new URLSearchParams((location.hash.split('?')[1] || '')).get('q') || '';
  const title = params.brand ? params.brand + (params.category ? ' · ' + t('items.cat_' + params.category) : '') : q ? t('items.search_title', { q }) : t('items.title');
  setTopBar({ back: '#/explore', title });
  root.appendChild(el('h1', { class: 'sr-only', text: title }));
  root.appendChild(emptyState(t('items.coming')));
});
