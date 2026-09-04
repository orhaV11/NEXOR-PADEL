// Placeholder: replaced by the real check view.
import { register, el, t } from '../core.js';
const placeholder = async (root) => { root.appendChild(el('p', { class: 'empty', text: t('common.loading') })); };

register('check', placeholder);
register('result', placeholder);
