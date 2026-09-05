// The in-app camera: viewfinder, shutter (tap = photo, hold = clip), flip, timer, framing guide, preview, "Use it".
// Hands the capture to the check flow (state.check). Filled in by the capture builder.
import { register, t, el, setTopBar } from '../core.js';

register('camera', async (root) => {
  setTopBar({ back: '#/check', title: t('camera.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('camera.title') }));
});
