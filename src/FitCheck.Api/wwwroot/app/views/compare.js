// "Which one?": two photos for the same intent, the stylist picks. Filled in by the compare builder.
import { register, t, el, setTopBar } from '../core.js';

register('compare', async (root) => {
  setTopBar({ back: '#/check', title: t('compare.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('compare.title') }));
});
