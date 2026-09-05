// OREVOSH entry point: every view registers its routes on import, then the core boots.
import { boot, el } from './core.js';
import './views/feed.js';
import './views/post.js';
import './views/explore.js';
import './views/challenges.js';
import './views/check.js';
import './views/activity.js';
import './views/profile.js';
import './views/auth.js';
import './views/settings.js';
import './views/camera.js';
import './views/admin.js';
import './views/pages.js';

boot().catch((e) => {
  console.error(e);
  document.body.appendChild(el('p', { class: 'alert', style: 'margin: 20px;', text: 'OREVOSH could not start. Reload the page.' }));
});
