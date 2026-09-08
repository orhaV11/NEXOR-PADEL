// Terms of use and the privacy policy, in the app, in both languages. Filled in by the legal builder.
import { register, t, el, setTopBar } from '../core.js';

for (const page of ['terms', 'privacy']) {
  register(page, async (root) => {
    setTopBar({ back: '#/', title: t('legal.' + page + '_title') });
    root.appendChild(el('h1', { text: t('legal.' + page + '_title') }));
  });
}
