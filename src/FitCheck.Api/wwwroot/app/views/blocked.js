// Blocked accounts (#/settings/blocked): the list from GET /api/users/me/blocks with Unblock on each row
// (DELETE /api/users/{handle}/block). Filled in by the block-client builder, together with the "Block" entry in the
// profile's "…" menu (confirmSheet with block.confirm_title/body, then POST /api/users/{handle}/block, toast
// block.done), "Unblock" when viewer.blocked is true, the Settings row (settings.blocked) that opens this page, and the
// removal of a blocked account's looks and comments from every list the client draws.
import { register, state, t, el, setTopBar, signInPrompt, emptyState } from '../core.js';

register('settings-blocked', async (root) => {
  setTopBar({ back: '#/settings', title: t('block.blocked_title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('block.blocked_title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  root.appendChild(emptyState(t('block.coming')));
});
