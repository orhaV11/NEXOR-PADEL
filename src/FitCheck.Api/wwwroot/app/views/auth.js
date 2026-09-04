// Placeholder: replaced by the real auth view.
import { register, el, t } from '../core.js';
const placeholder = async (root) => { root.appendChild(el('p', { class: 'empty', text: t('common.loading') })); };

register('login', placeholder);
register('signup', placeholder);
register('welcome', placeholder);
