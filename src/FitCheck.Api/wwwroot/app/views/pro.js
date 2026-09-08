// The Pro screen: what you get, the price, checkout or the manual note. Filled in by the plans builder.
import { register, t, el, setTopBar } from '../core.js';

register('pro', async (root) => {
  setTopBar({ back: '#/me', title: t('pro.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('pro.title') }));
});
