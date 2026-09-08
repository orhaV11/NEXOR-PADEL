// The daily prompt page: today's looks and yours. Filled in by the today builder.
import { register, t, el, setTopBar } from '../core.js';

register('today', async (root) => {
  setTopBar({ back: '#/', title: t('today.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('today.title') }));
});
