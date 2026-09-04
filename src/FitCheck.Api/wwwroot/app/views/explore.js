// Placeholder: replaced by the real explore view.
import { register, el, t } from '../core.js';
const placeholder = async (root) => { root.appendChild(el('p', { class: 'empty', text: t('common.loading') })); };

register('explore', placeholder);
register('search', placeholder);
register('tag', placeholder);
