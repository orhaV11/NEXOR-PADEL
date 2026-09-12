// The weekly flames board (#/board: Looks, People, Rising, By intent, Stylist's picks; the sponsor header; "closes in";
// the reader's own row) and the hall of winners (#/board/hall). Filled in by the board-client builder from
// GET /api/board?week= and GET /api/board/hall; the Explore "This week" strip, the reset-day feed card, the profile
// badge and the activity line belong to the same builder.
import { register, t, el, setTopBar, emptyState } from '../core.js';

register('board', async (root) => {
  setTopBar({ back: '#/explore', title: t('board.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('board.title') }));
  root.appendChild(emptyState(t('board.coming')));
});

register('board-hall', async (root) => {
  setTopBar({ back: '#/board', title: t('hall.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('hall.title') }));
  root.appendChild(emptyState(t('hall.coming')));
});
