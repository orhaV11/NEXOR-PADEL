// Static pages: the community guidelines (and what we keep). Filled in by the share-card/guidelines builder.
import { register, t, el, setTopBar } from '../core.js';

register('guidelines', async (root) => {
  setTopBar({ back: '#/', title: t('guidelines.title') });
  root.appendChild(el('h1', { text: t('guidelines.title') }));
});
