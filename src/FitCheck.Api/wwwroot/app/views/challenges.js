// Placeholder: replaced by the real challenges view.
import { register, el, t } from '../core.js';
const placeholder = async (root) => { root.appendChild(el('p', { class: 'empty', text: t('common.loading') })); };

register('challenges', placeholder);
register('challenge', placeholder);
register('new-challenge', placeholder);
