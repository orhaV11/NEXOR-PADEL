// Placeholder: replaced by the real profile view.
import { register, el, t } from '../core.js';
const placeholder = async (root) => { root.appendChild(el('p', { class: 'empty', text: t('common.loading') })); };

register('user', placeholder);
register('me', placeholder);
register('saved', placeholder);
register('checks', placeholder);
