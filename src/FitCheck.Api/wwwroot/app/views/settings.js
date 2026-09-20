// Settings: profile photo, the profile fields, language, the styles you wear, brand mode, sign out and the one
// delete for everything. Ported from the Phase 2 settingsView onto the kit: bottom sheets instead of confirm(),
// the avatar upload with the client-side square crop, interests as chips, brand mode as a switch.
import {
  register, state, t, api, el, avatar, setTopBar, signInPrompt, confirmSheet, toast, navigate, resetSession, renderShell, signOut, switchLocale, localeName, getLocale, pickFile, prepareImage, enabledLocales, AVATAR_EDGE, INTENTS, intentLabel, showAlert,
  proBadge, fmtDate, intlLocale, onLeave
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
.s-email-status { padding-inline: 2px; }
.s-email-status.ok { color: var(--ok); }
.s-email-resend { padding-block: 0; align-self: flex-start; }
.s-plan-row { display: flex; align-items: center; flex-wrap: wrap; gap: 10px 12px; min-block-size: 44px; }
.s-plan-row b { font-weight: 700; font-size: 16px; }
.s-plan-row .btn-text { margin-inline-start: auto; padding-block: 0; }
.s-plan-manage { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
.s-plan-manage .btn { min-block-size: 44px; }
.s-export { display: flex; flex-direction: column; gap: 10px; border-block-start: 1px solid var(--line); padding-block-start: 18px; }
.s-export .btn { min-block-size: 44px; }
.s-export-status { display: flex; flex-direction: column; gap: 10px; align-items: flex-start; }
.s-export-status p { color: var(--ok); font-weight: 600; }
.s-export-status .s-export-size { color: var(--ink-2); font-weight: 400; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const field = (id, label, control) => el('div', { class: 'field' }, [el('label', { for: id, text: label }), control]);

// ---------- the subscription ----------

/**
 * "Manage subscription": opens Stripe's Billing Portal in this tab (the card, the invoices, cancelling all happen
 * there; the portal comes back to #/settings, and the webhook is what changes the plan here). Only offered when the
 * plan is Pro and Stripe is live (plans.billing); a Pro switched on by hand shows billing.manual_hint instead, and the
 * server answers 404 with the same sentence should the button ever be tapped without a customer behind it.
 */
function manageButton(ctx) {
  const button = el('button', { type: 'button', class: 'btn btn-sm btn-secondary', id: 'billing-manage', text: t('billing.manage') });
  button.addEventListener('click', async () => {
    if (button.disabled) return;
    button.disabled = true;
    button.textContent = t('common.loading');
    try {
      const { url } = await api('POST', '/api/billing/portal');
      location.href = url;
    } catch (e) {
      if (ctx.stale()) return;
      toast(e.message || t('error.generic'));
      button.disabled = false;
      button.textContent = t('billing.manage');
    }
  });
  return button;
}

// ---------- the data export ----------

/** The file's size for a person: kB up to a megabyte, MB past it, in the locale's digits. */
function fmtBytes(bytes) {
  const mega = bytes >= 1024 * 1024;
  return new Intl.NumberFormat(intlLocale(), { style: 'unit', unit: mega ? 'megabyte' : 'kilobyte', unitDisplay: 'short', maximumFractionDigits: 1 })
    .format(bytes / (mega ? 1024 * 1024 : 1024));
}

/**
 * "Download your data": fetches the export (through api(), so the CSRF header, the language and a signed-out answer are
 * handled like everywhere else), hands the JSON to the browser as a file, and says so with the size, so the person sees
 * something happened even where a download cannot start on its own (an in-app browser, a sandbox): the "Save file" link
 * is the same file again. The server allows a few an hour; past that its own sentence is shown.
 */
function exportSection(ctx) {
  const button = el('button', { type: 'button', class: 'btn btn-secondary', id: 'export-data', text: t('settings.export') });
  // dir=ltr: "1.1 kB" stays one unit inside a Hebrew or Arabic line instead of reordering to "kB 1.1".
  const size = el('span', { class: 's-export-size', id: 'export-size', dir: 'ltr' });
  const ready = el('p', { role: 'status', id: 'export-ready' }, [t('export.ready'), ' ', size]);
  const save = el('a', { class: 'btn btn-sm btn-secondary', id: 'export-save', href: '#', text: t('export.save') });
  const status = el('div', { class: 's-export-status', id: 'export-status', hidden: true }, [ready, save]);
  let objectUrl = null;
  onLeave(() => { if (objectUrl) URL.revokeObjectURL(objectUrl); });

  button.addEventListener('click', async () => {
    if (button.disabled) return;
    button.disabled = true;
    button.textContent = t('common.loading');
    try {
      const data = await api('GET', '/api/users/me/export');
      if (ctx.stale()) return;
      const text = JSON.stringify(data, null, 2);
      const blob = new Blob([text], { type: 'application/json' });
      // The same name the server puts on the file: the handle and the day it was made (UTC), as the server's clock says.
      const day = (data.exportedAt ? new Date(data.exportedAt) : new Date()).toISOString().slice(0, 10).replace(/-/g, '');
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      objectUrl = URL.createObjectURL(blob);
      save.href = objectUrl;
      save.download = `orevosh-${state.me.handle}-${day}.json`;
      size.textContent = fmtBytes(blob.size);
      status.hidden = false;
      save.click();   // starts the download where the browser allows it; the link stays for a tap where it does not
    } catch (e) {
      if (ctx.stale()) return;
      toast(e.message || t('error.generic'));
    } finally {
      if (!ctx.stale()) { button.disabled = false; button.textContent = t('settings.export'); }
    }
  });

  return el('section', { class: 's-section s-export' }, [
    button,
    el('p', { class: 'hint', text: t('export.hint') }),
    status
  ]);
}

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
  // Round 13: only the live languages (Languages:Enabled, on /api/config), the same list as the switcher in the masthead.
  const lang = el('select', { id: 's-lang', name: 'language' }, enabledLocales().map((code) => el('option', { value: code, lang: code, text: localeName(code) })));
  lang.value = getLocale();

  // ---------- email (recovery only, never shown to anyone) ----------

  // No field where this server cannot mail the link: an address nobody can confirm would only be refused.
  const emailInput = state.config.email ? el('input', {
    type: 'text', id: 's-email', name: 'email', maxlength: '200', inputmode: 'email', autocomplete: 'email',
    autocapitalize: 'none', autocorrect: 'off', spellcheck: 'false', dir: 'ltr', placeholder: t('welcome.email_placeholder'), value: me.email || ''
  }) : null;
  const emailStatus = el('p', { class: 'hint s-email-status', id: 's-email-status', hidden: true });
  const resend = el('button', { type: 'button', class: 'btn-text s-email-resend', id: 's-email-resend', text: t('settings.email_resend'), hidden: true });
  const paintEmail = () => {
    const current = state.me;
    if (!emailInput || !current.email) { emailStatus.hidden = true; resend.hidden = true; return; }
    emailStatus.textContent = t(current.emailVerified ? 'settings.email_verified' : 'settings.email_unverified');
    emailStatus.classList.toggle('ok', !!current.emailVerified);
    emailStatus.hidden = false;
    resend.hidden = !!current.emailVerified;
  };
  resend.addEventListener('click', async () => {
    if (resend.disabled) return;
    resend.disabled = true;
    try {
      await api('POST', '/api/users/me/email/resend');
      toast(t('settings.email_sent'));
    } catch (e) {
      toast(e.message);
    } finally {
      resend.disabled = false;
    }
  });
  paintEmail();
  const emailField = el('div', { class: 'field s-email' }, [
    emailInput ? el('label', { for: 's-email', text: t('settings.email') }) : el('span', { class: 'label', text: t('settings.email') }),
    emailInput,
    el('p', { class: 'hint', text: t(emailInput ? 'settings.email_hint' : 'settings.email_off') }),
    emailStatus,
    resend
  ]);

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
        email: emailInput ? emailInput.value.trim() : null,   // null: leave it alone; "": clear it
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
      paintEmail();    // a new address starts unconfirmed, with the link on its way
    } catch (e) {
      showAlert(error, e.message);
    } finally {
      save.disabled = false;
    }
  };

  // ---------- plan ----------

  // The current plan and the door to the Pro screen. Read-only here: Checkout (or --pro) is what changes it. On Pro
  // with Stripe live, the door to Stripe's portal (change the card, cancel); on a Pro switched on by hand, a line saying
  // to write to us instead.
  const isPro = me.plan === 'pro';
  const billing = !!(state.config.plans && state.config.plans.billing);
  const planRow = el('div', { class: 'field s-plan', id: 's-plan' }, [
    el('span', { class: 'label', text: t('settings.plan') }),
    el('div', { class: 's-plan-row' }, [
      isPro ? proBadge(me) : el('b', { id: 's-plan-name', text: t('settings.plan_free') }),
      isPro && me.proUntil ? el('span', { class: 'hint', id: 's-plan-until', text: t('settings.plan_until', { date: fmtDate(me.proUntil) }) }) : null,
      el('a', { class: 'btn-text', id: 's-plan-link', href: '#/pro', text: t(isPro ? 'settings.plan_about' : 'settings.plan_go') })
    ]),
    isPro && billing ? el('div', { class: 's-plan-manage' }, [manageButton(ctx), el('span', { class: 'hint', text: t('billing.manage_hint') })]) : null,
    isPro && !billing ? el('p', { class: 'hint', id: 'billing-manual', text: t('billing.manual_hint') }) : null
  ]);

  root.appendChild(el('form', { class: 'stack', novalidate: true, onsubmit }, [
    planRow,
    field('s-name', t('settings.display_name'), name),
    field('s-bio', t('settings.bio'), bio),
    field('s-web', t('settings.website'), web),
    emailField,
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
  root.appendChild(exportSection(ctx));

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
  // The accounts you blocked (Round 11), and for moderators the door to the queue; the server decides who is one
  // (Admin:Handles), the client only shows the door.
  const links = el('div', { class: 'links' }, [
    el('a', { href: '#/settings/blocked', id: 'settings-blocked' }, [t('settings.blocked'), el('span', { class: 'icon', icon: 'x' })]),
    state.me.isAdmin ? el('a', { href: '#/admin', id: 'moderation' }, [t('settings.moderation'), el('span', { class: 'icon', icon: 'shield' })]) : null
  ]);
  root.appendChild(el('section', { class: 's-account' }, [links, logout, del, dangerError]));
});
