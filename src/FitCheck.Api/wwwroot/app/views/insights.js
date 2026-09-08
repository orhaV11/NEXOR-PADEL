// What your checks say about you. Filled in by the insights builder.
import { register, t, el, setTopBar } from '../core.js';

register('insights', async (root) => {
  setTopBar({ back: '#/me', title: t('insights.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('insights.title') }));
});
