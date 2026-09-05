// The moderation queue for handles in Admin:Handles: reported looks and comments, hide/show/delete, suspend accounts.
// Filled in by the admin builder.
import { register, t, el, setTopBar } from '../core.js';

register('admin', async (root) => {
  setTopBar({ back: '#/settings', title: t('admin.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('admin.title') }));
});
