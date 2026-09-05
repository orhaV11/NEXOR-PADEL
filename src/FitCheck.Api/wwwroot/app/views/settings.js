// Settings: profile photo, the profile fields, language, the styles you wear, brand mode, sign out and the one
// delete for everything. Ported from the Phase 2 settingsView onto the kit: bottom sheets instead of confirm(),
// the avatar upload with the client-side square crop, interests as chips, brand mode as a switch.
import {
  register, state, t, api, el, avatar, setTopBar, signInPrompt, confirmSheet, toast, navigate, resetSession, renderShell, signOut, switchLocale, localeName, getLocale, pickFile, prepareImage, AVAILABLE_LOCALES, AVATAR_EDGE, INTENTS, intentLabel, showAlert
} from '../core.js';
import { pushSupport, getPushSubscription, enablePush, disablePush, syncPush, sendTestPush, madeWithCurrentKey, dropStalePush, unsubscribePush } from '../push.js';

// The few rules the shared stylesheet does not have: the photo row, taller chips, the two-line switch label, the push block.
const CSS = `
.s-section > * + * { margin-block-start: 10px; }
.s-photo { display: flex; align-items: center; gap: 16px; }
.s-photo .avatar { flex: none; }
.s-photo-actions { display: flex; flex-direction: column; gap: 8px; flex: 1; min-inline-size: 0; }
.s-photo-actions .btn { min-block-size: 44px; }
.s-chips .chip { min-block-size: 44px; padding-inline: 16px; }
.s-switch-text { display: grid; gap: 2px; min-inline-size: 0; }
.s-switch-text b { font-weight: 600; }
.s-push { border-block-start: 1px solid var(--line); padding-block-start: 18px; }
.s-push .switch:has(input:disabled) .s-switch-text { color: var(--ink-2); }
.s-push .switch input:disabled { opacity: 0.45; cursor: not-allowed; }
.s-push-status { padding-inline: 2px; }
.s-push-status.danger { color: var(--danger); }
.s-push-test { min-block-size: 44px; }
.s-account { display: flex; flex-direction: column; gap: 10px; border-block-start: 1px solid var(--line); padding-block-start: 18px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const field = (id, label, control) => el('div', { class: 'field' }, [el('label', { for: id, text: label }), control]);

// ---------- push ----------

/**
 * "Notifications on your phone": a switch that reads its state from this browser's own push subscription, never from
 * the server (which only knows endpoints). Off the happy path it explains itself in one line: install the app first on
 * iPhone, blocked in the browser, not set up on this server, or not possible here. A small test button when it is on.
 */
function pushSection(ctx) {
  const input = el('input', { type: 'checkbox', id: 's-push', name: 'push', disabled: true, 'aria-describedby': 's-push-status' });
  const status = el('p', { class: 'hint s-push-status', id: 's-push-status', hidden: true });
  const test = el('button', { type: 'button', class: 'btn btn-sm btn-secondary s-push-test', text: t('push.test'), hidden: true });
  const label = el('label', { class: 'switch', for: 's-push' }, [
    el('span', { class: 's-switch-text' }, [el('b', { text: t('push.title') }), el('span', { class: 'hint', text: t('push.hint') })]),
    input
  ]);
  const setStatus = (text, danger) => { status.textContent = text || ''; status.hidden = !text; status.classList.toggle('danger', !!danger); };
  const paint = (on) => { input.checked = on; test.hidden = !on; };
  let locked = true;   // stays true when the browser cannot do it, so a tap never flips the switch
  const setLocked = (value) => { locked = value; input.disabled = value; };

  const support = pushSupport();
  if (support === 'ready') {
    getPushSubscription().then(async (sub) => {
      // A subscription made with a VAPID key this server no longer uses would leave the switch on while every push fails:
      // it goes on both sides, and the switch is off with its "Turn on" affordance.
      if (sub && !madeWithCurrentKey(sub)) { await dropStalePush(sub); sub = null; }
      if (ctx.stale()) return;
      paint(!!sub);
      setLocked(false);
      if (sub) syncPush(sub);   // the endpoint follows whoever is signed in on this phone
    });
  } else {
    paint(false);
    const key = { ios_install: 'push.ios_hint', not_configured: 'push.not_configured', denied: 'push.denied' }[support] || 'push.unsupported';
    setStatus(t(key), support === 'denied');
  }

  input.addEventListener('change', async () => {
    if (locked) { input.checked = !input.checked; return; }
    const wantOn = input.checked;
    setLocked(true);
    try {
      if (wantOn) {
        const sub = await enablePush();
        if (ctx.stale()) return;
        if (!sub) { paint(false); return; }   // the permission prompt was dismissed: nothing changed
        paint(true);
        setStatus('');
        toast(t('push.enabled_toast'));
      } else {
        await disablePush();
        if (ctx.stale()) return;
        paint(false);
        toast(t('push.disabled_toast'));
      }
    } catch (e) {
      if (ctx.stale()) return;
      paint(!wantOn);
      if (typeof Notification !== 'undefined' && Notification.permission === 'denied') { setStatus(t('push.denied'), true); return; }
      toast(e.message || t('error.generic'));
    } finally {
      if (!ctx.stale() && !(typeof Notification !== 'undefined' && Notification.permission === 'denied')) setLocked(false);
    }
  });

  test.addEventListener('click', async () => {
    if (test.disabled) return;
    test.disabled = true;
    try {
      await sendTestPush();
      toast(t('push.test_sent'));
    } catch (e) {
      toast(e.message);
    } finally {
      test.disabled = false;
    }
  });

  return el('section', { class: 's-section s-push' }, [label, status, test]);
}

register('settings', async (root, params, ctx) => {
  setTopBar({ back: '#/me', title: t('settings.title') });
  // The top bar carries the visible title; this one is for the focus move and the outline.
  root.appendChild(el('h1', { class: 'sr-only', text: t('settings.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  ensureStyle();
  const me = state.me;

  // ---------- profile photo ----------

  const holder = el('div');
  const change = el('button', { type: 'button', class: 'btn btn-secondary', text: t('settings.avatar_change') });
  const remove = el('button', { type: 'button', class: 'btn btn-ghost', text: t('settings.avatar_remove') });
  const paintAvatar = () => {
    holder.replaceChildren(avatar(state.me, { size: 'lg', noLink: true }));
    remove.hidden = !state.me.avatarUrl;
  };
  let photoBusy = false;
  const setPhotoBusy = (busy) => {
    photoBusy = busy;
    change.disabled = busy; remove.disabled = busy;
    change.textContent = t(busy ? 'common.loading' : 'settings.avatar_change');
  };
  change.addEventListener('click', async () => {
    if (photoBusy) return;
    const file = await pickFile('avatar-file');
    if (!file || ctx.stale()) return;
    setPhotoBusy(true);
    try {
      const blob = await prepareImage(file, AVATAR_EDGE, true);
      if (ctx.stale()) return;
      const form = new FormData();
      form.append('image', blob, blob.type === 'image/jpeg' ? 'avatar.jpg' : (file.name || 'avatar'));
      const updated = await api('POST', '/api/users/me/avatar', form);
      if (updated) state.me = updated;
      toast(t('settings.avatar_saved'));
      if (ctx.stale()) return;
      paintAvatar();
    } catch (e) {
      toast(e.message);
    } finally {
      setPhotoBusy(false);
    }
  });
  remove.addEventListener('click', async () => {
    if (photoBusy) return;
    setPhotoBusy(true);
    try {
      const updated = await api('DELETE', '/api/users/me/avatar');
      state.me = updated || { ...state.me, avatarUrl: undefined };
      if (ctx.stale()) return;
      paintAvatar();
    } catch (e) {
      toast(e.message);
    } finally {
      setPhotoBusy(false);
    }
  });
  paintAvatar();

  root.appendChild(el('section', { class: 's-section' }, [
    el('h2', { text: t('settings.avatar') }),
    el('div', { class: 's-photo' }, [holder, el('div', { class: 's-photo-actions' }, [change, remove])])
  ]));

  // ---------- profile fields ----------

  // The name falls back to the handle server-side, so an unset name shows as an empty field with the handle as the hint.
  const name = el('input', {
    type: 'text', id: 's-name', name: 'nickname', maxlength: '40', autocomplete: 'nickname', enterkeyhint: 'next',
    placeholder: me.handle, value: me.name === me.handle ? '' : me.name
  });
  const bio = el('textarea', { id: 's-bio', name: 'bio', maxlength: '160', rows: '3' });
  bio.value = me.bio || '';
  const web = el('input', {
    type: 'url', id: 's-web', name: 'url', maxlength: '200', inputmode: 'url', autocomplete: 'url',
    autocapitalize: 'none', autocorrect: 'off', spellcheck: 'false', dir: 'ltr', placeholder: 'https://', value: me.website || ''
  });
  const lang = el('select', { id: 's-lang', name: 'language' }, AVAILABLE_LOCALES.map((code) => el('option', { value: code, lang: code, text: localeName(code) })));
  lang.value = getLocale();

  const picked = new Set((me.interests || []).filter((intent) => INTENTS.includes(intent)));
  const chips = el('div', { class: 'chips s-chips', role: 'group', 'aria-labelledby': 's-interests-label' }, INTENTS.map((intent) => {
    const chip = el('button', { type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(picked.has(intent)), text: intentLabel(intent) });
    chip.addEventListener('click', () => {
      if (picked.has(intent)) picked.delete(intent); else picked.add(intent);
      chip.setAttribute('aria-pressed', String(picked.has(intent)));
    });
    return chip;
  }));

  const brand = el('input', { type: 'checkbox', id: 's-brand', name: 'brand', checked: me.accountType === 'Brand' });
  const brandSwitch = el('label', { class: 'switch', for: 's-brand' }, [
    el('span', { class: 's-switch-text' }, [el('b', { text: t('settings.brand') }), el('span', { class: 'hint', text: t('settings.brand_hint') })]),
    brand
  ]);

  const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
  const save = el('button', { type: 'submit', class: 'btn', id: 's-save', text: t('settings.save') });
  const onsubmit = async (event) => {
    event.preventDefault();
    if (save.disabled) return;
    save.disabled = true;
    error.hidden = true;
    const language = lang.value;
    try {
      const updated = await api('PATCH', '/api/users/me', {
        displayName: name.value.trim(),
        bio: bio.value.trim(),
        website: web.value.trim(),
        language,
        interests: INTENTS.filter((intent) => picked.has(intent)),
        accountType: brand.checked ? 'Brand' : 'Person'
      });
      if (updated) state.me = updated;
      // A new language re-renders this view in it; the toast below already speaks it.
      if (language !== getLocale()) { try { await switchLocale(language); } catch (e) { toast(t('error.network')); } }
      renderShell();
      toast(t('settings.saved'));
      if (ctx.stale()) return;
      paintAvatar();   // the initials follow the display name
    } catch (e) {
      showAlert(error, e.message);
    } finally {
      save.disabled = false;
    }
  };

  root.appendChild(el('form', { class: 'stack', novalidate: true, onsubmit }, [
    field('s-name', t('settings.display_name'), name),
    field('s-bio', t('settings.bio'), bio),
    field('s-web', t('settings.website'), web),
    field('s-lang', t('settings.language'), lang),
    el('div', { class: 'field' }, [
      el('span', { class: 'label', id: 's-interests-label', text: t('settings.interests') }),
      el('span', { class: 'hint', text: t('settings.interests_hint') }),
      chips
    ]),
    brandSwitch,
    error,
    save
  ]));

  root.appendChild(pushSection(ctx));

  // ---------- sign out, delete ----------

  const logout = el('button', { type: 'button', class: 'btn btn-secondary', id: 'logout', text: t('auth.logout') });
  logout.addEventListener('click', () => { logout.disabled = true; signOut(); });
  const dangerError = el('p', { class: 'alert danger', role: 'alert', hidden: true });
  const del = el('button', { type: 'button', class: 'btn btn-danger', id: 'delete-account', text: t('settings.delete') });
  del.addEventListener('click', async () => {
    if (del.disabled) return;
    const ok = await confirmSheet(t('settings.delete'), t('settings.delete_confirm'), t('settings.delete'), true);
    if (!ok || ctx.stale()) return;
    del.disabled = true;
    dangerError.hidden = true;
    await unsubscribePush();   // this browser's subscription goes first, while the session cookie is still there
    try {
      await api('DELETE', '/api/users/me');
    } catch (e) {
      // 401 means the account is already gone; anything else keeps the person here with the reason.
      if (e.status !== 401) { showAlert(dangerError, e.message); del.disabled = false; return; }
    }
    state.me = null;
    resetSession();
    renderShell();
    navigate('#/');
  });
  // Moderators get the door to the queue here; the server decides who is one (Admin:Handles), the client only shows the door.
  const moderation = state.me.isAdmin
    ? el('div', { class: 'links' }, [el('a', { href: '#/admin', id: 'moderation' }, [t('settings.moderation'), el('span', { class: 'icon', icon: 'shield' })])])
    : null;
  root.appendChild(el('section', { class: 's-account' }, [moderation, logout, del, dangerError]));
});
