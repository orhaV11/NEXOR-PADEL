// The pilot metrics as a page for moderators. Filled in by the ops builder.
import { register, t, el, setTopBar } from '../core.js';

register('admin-metrics', async (root) => {
  setTopBar({ back: '#/admin', title: t('dash.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('dash.title') }));
});
