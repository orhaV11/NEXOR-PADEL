// Auth and onboarding: sign in, join (handle, password and the date of birth, nothing else) and the welcome screen
// that follows a signup: pick the styles you wear, an email in case you get locked out, follow a few brands, then in.
// Recovery lives here too: forgot (a handle or email, always "the link is on its way"), reset (the link from the mail,
// a new password, signed in), verify (the link from the mail, confirmed). Both auth pages are public; a signed-in
// person who lands on them is sent home. Ported from the Phase 2 authView onto the kit.
import {
  register, state, t, api, el, iconButton, navigate, renderShell, setTopBar, openLanguageSheet, getLocale, INTENTS, intentLabel, userRow, toast, isMe, redirect, showAlert, resetSession, claimGuestChecks
} from '../core.js';

// The few rules the shared stylesheet does not have: the date field, the agreement line, bigger onboarding steps and chips.
const CSS = `
.auth-switch { text-align: center; }
.auth-switch a { color: var(--accent); text-decoration: none; font-weight: 500; }
/* the date field, dressed like the text fields (the shared rule lists types and a date input is not one): a date is a
   numeral, so its fields keep their order in Hebrew and sit at the end edge; color-scheme keeps the picker dark */
.auth-dob { inline-size: 100%; min-block-size: 50px; border: 0; border-radius: var(--radius-sm); background: var(--surface-2); padding-block: 10px; padding-inline: 14px; font-size: 16px; font-family: inherit; color: var(--ink); outline: none; -webkit-appearance: none; appearance: none; color-scheme: dark; direction: ltr; text-align: start; }
[dir="rtl"] .auth-dob { text-align: end; }
.auth-dob:focus-visible { outline: none; }
.auth-dob::-webkit-calendar-picker-indicator { cursor: pointer; opacity: 0.8; }
.auth-dob::-webkit-date-and-time-value { text-align: inherit; }
.auth-agree { text-align: center; line-height: 1.5; }
.auth-agree a { color: var(--accent); text-decoration: underline; text-underline-offset: 3px; text-decoration-color: rgba(179, 157, 255, 0.5); font-weight: 500; white-space: nowrap; }
.w-step > * + * { margin-block-start: 10px; }
.w-step h2 { font-family: var(--font-display); font-size: 20px; line-height: 1.15; font-weight: 800; color: var(--ink); }
.w-chips .chip { min-block-size: 44px; padding-inline: 16px; }
.w-people { display: flex; flex-direction: column; }
.w-actions { display: flex; flex-direction: column; gap: 8px; }
.auth-links { display: flex; flex-direction: column; align-items: center; gap: 4px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const AUTH_ROUTE = /^#\/(login|signup|welcome|forgot|reset|verify)(\/|$)/;
/** Where to go once signed in: the page that asked for the sign-in, unless that was an auth page itself. Clears it. */
function takeReturnTo() {
  const back = state.returnTo && !AUTH_ROUTE.test(state.returnTo) ? state.returnTo : null;
  state.returnTo = null;
  return back || '#/';
}
const langButton = () => iconButton('globe', t('lang.label'), openLanguageSheet, { id: 'lang' });
/** Today as the date input wants it (yyyy-MM-dd), in the phone's own calendar day: nobody is stopped on their birthday. */
function isoToday() {
  const d = new Date();
  return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
}
/** A message with elements in its {slots}: the text around them stays text, so the translation orders the links. */
function richText(key, slots) {
  const nodes = [];
  for (const part of t(key).split(/(\{\w+\})/)) {
    const slot = part.length > 2 && part.startsWith('{') && part.endsWith('}') ? part.slice(1, -1) : null;
    if (slot && slots[slot]) nodes.push(slots[slot]);
    else if (part) nodes.push(part);
  }
  return nodes;
}

// ---------- sign in / join ----------

function authView(mode) {
  const signup = mode === 'signup';
  return async (root, params, ctx) => {
    if (state.me) { redirect('#/'); return; }
    ensureStyle();
    setTopBar({ back: true, actions: [langButton()] });

    const handle = el('input', {
      type: 'text', id: 'a-handle', name: 'username', maxlength: '40', autocomplete: 'username',
      autocapitalize: 'none', autocorrect: 'off', spellcheck: 'false', enterkeyhint: 'next'
    });
    const password = el('input', {
      type: 'password', id: 'a-password', name: 'password', maxlength: '200',
      autocomplete: signup ? 'new-password' : 'current-password', enterkeyhint: 'go'
    });
    // The date of birth replaces the 16+ checkbox: the server decides (16 on the day; "yyyy-MM-dd" is what the picker
    // sends whatever the locale) and its message shows here like any other. max stops the picker at today.
    const dob = signup ? el('input', {
      type: 'date', id: 'a-dob', name: 'bday', class: 'auth-dob', autocomplete: 'bday', max: isoToday(), min: '1900-01-01', enterkeyhint: 'go'
    }) : null;
    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    const submit = el('button', { type: 'submit', class: 'btn', id: 'a-submit', text: t(signup ? 'auth.submit_signup' : 'auth.submit_login') });

    const onsubmit = async (event) => {
      event.preventDefault();
      if (submit.disabled) return;
      submit.disabled = true;
      error.hidden = true;
      try {
        const me = signup
          ? await api('POST', '/api/auth/signup', { handle: handle.value.trim(), password: password.value, birthDate: dob.value, language: getLocale() })
          : await api('POST', '/api/auth/login', { handle: handle.value.trim(), password: password.value });
        state.me = me;
        renderShell();
        // A check made as a guest on this phone follows the person in (one cheap call; 0 is the usual answer).
        await claimGuestChecks();
        if (ctx.stale()) return;                         // they moved on meanwhile; the session is in place either way
        if (signup) navigate('#/welcome');               // returnTo stays for the welcome screen to honour
        else navigate(takeReturnTo());
      } catch (e) {
        showAlert(error, e.message);
        submit.disabled = false;
      }
    };

    const form = el('form', { class: 'stack', novalidate: true, onsubmit }, [
      el('div', { class: 'field' }, [
        el('label', { for: 'a-handle', text: t('auth.handle') }),
        handle,
        signup ? el('p', { class: 'hint', text: t('auth.handle_hint') }) : null
      ]),
      el('div', { class: 'field' }, [
        el('label', { for: 'a-password', text: t('auth.password') }),
        password,
        signup ? el('p', { class: 'hint', text: t('auth.password_hint') }) : null
      ]),
      signup ? el('div', { class: 'field' }, [
        el('label', { for: 'a-dob', text: t('auth.dob') }),
        dob,
        el('p', { class: 'hint', text: t('auth.dob_hint') })
      ]) : null,
      error,
      submit,
      // What they are agreeing to: the terms, the privacy policy and the guidelines, each one tap away and back.
      signup ? el('p', { class: 'hint auth-agree', id: 'a-agree' }, richText('auth.agree', {
        terms: el('a', { href: '#/terms', id: 'a-terms', text: t('auth.agree_terms') }),
        privacy: el('a', { href: '#/privacy', id: 'a-privacy', text: t('auth.agree_privacy') }),
        guidelines: el('a', { href: '#/guidelines', id: 'a-guidelines', text: t('guidelines.link') })
      })) : null,
      // The way back in without the password: only where this server can mail a link (the page says so otherwise).
      signup ? null : el('p', { class: 'hint auth-switch' }, [el('a', { href: '#/forgot', id: 'a-forgot', text: t('auth.forgot') })]),
      el('p', { class: 'hint auth-switch' }, [
        t(signup ? 'auth.have_account' : 'auth.no_account') + ' ',
        el('a', { href: signup ? '#/login' : '#/signup', text: t(signup ? 'auth.login' : 'auth.signup') })
      ])
    ]);

    root.appendChild(el('h1', { text: t(signup ? 'auth.signup_title' : 'auth.login_title') }));
    root.appendChild(el('p', { class: 'lede', text: t(signup ? 'auth.signup_intro' : 'auth.login_intro') }));
    root.appendChild(form);
    handle.focus();
  };
}

register('login', authView('login'));
register('signup', authView('signup'));

// ---------- welcome (after signup) ----------

register('welcome', async (root, params, ctx) => {
  if (!state.me) { redirect('#/signup'); return; }
  ensureStyle();
  setTopBar({ title: t('welcome.title'), actions: [langButton()] });

  // Step 1: the styles they wear. Pre-ticked from the account for anyone who comes back here.
  const picked = new Set((state.me.interests || []).filter((intent) => INTENTS.includes(intent)));
  const chips = el('div', { class: 'chips w-chips', role: 'group', 'aria-label': t('welcome.styles_title') }, INTENTS.map((intent) => {
    const chip = el('button', { type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(picked.has(intent)), text: intentLabel(intent) });
    chip.addEventListener('click', () => {
      if (picked.has(intent)) picked.delete(intent); else picked.add(intent);
      chip.setAttribute('aria-pressed', String(picked.has(intent)));
    });
    return chip;
  }));

  // Step 2: an address for getting back in, only where this server can mail the link. Optional, saved with the styles.
  const emailInput = state.config.email ? el('input', {
    type: 'text', id: 'w-email', name: 'email', maxlength: '200', inputmode: 'email', autocomplete: 'email',
    autocapitalize: 'none', autocorrect: 'off', spellcheck: 'false', dir: 'ltr', enterkeyhint: 'done',
    placeholder: t('welcome.email_placeholder'), 'aria-label': t('settings.email'), value: state.me.email || ''
  }) : null;
  const emailError = el('p', { class: 'alert danger', role: 'alert', hidden: true });

  // Step 3 fills in once Explore answers; it stays hidden when there are no brands (or the endpoint is not there yet).
  const brands = el('section', { class: 'w-step', hidden: true });

  let busy = false;
  const leave = () => navigate(takeReturnTo());
  const done = el('button', { type: 'button', class: 'btn', id: 'w-done', text: t('welcome.done') });
  const skip = el('button', { type: 'button', class: 'btn btn-ghost', id: 'w-skip', text: t('common.skip'), onclick: () => { if (!busy) leave(); } });
  done.addEventListener('click', async () => {
    if (busy) return;
    busy = true; done.disabled = true; skip.disabled = true;
    const interests = INTENTS.filter((intent) => picked.has(intent));
    const email = emailInput ? emailInput.value.trim() : '';
    const emailChanged = !!emailInput && email !== (state.me.email || '');
    if (interests.length || emailChanged) {
      const body = {};
      if (interests.length) body.interests = interests;
      if (emailChanged) body.email = email;
      try {
        const me = await api('PATCH', '/api/users/me', body);
        if (me) state.me = me;
        toast(t('welcome.saved'));
      } catch (e) {
        if (ctx.stale()) return;
        // Stay, with the reason: an address with a typo is fixed here, not lost.
        if (emailChanged) showAlert(emailError, e.message); else toast(e.message);
        busy = false; done.disabled = false; skip.disabled = false;
        return;
      }
      if (ctx.stale()) return;
    }
    leave();
  });

  root.appendChild(el('h1', { text: t('welcome.title') }));
  root.appendChild(el('section', { class: 'w-step' }, [
    el('h2', { text: t('welcome.styles_title') }),
    el('p', { class: 'hint', text: t('welcome.styles_hint') }),
    chips
  ]));
  if (emailInput) {
    root.appendChild(el('section', { class: 'w-step', id: 'w-email-step' }, [
      el('h2', { text: t('welcome.email_title') }),
      el('p', { class: 'hint', text: t('welcome.email_hint') }),
      el('div', { class: 'field' }, [emailInput]),
      emailError
    ]));
  }
  root.appendChild(brands);
  root.appendChild(el('div', { class: 'w-actions' }, [done, skip]));

  // Not awaited: the page is usable at once and the brands slide in when they arrive.
  (async () => {
    let data = null;
    try { data = await api('GET', '/api/explore'); } catch (e) { return; }
    if (ctx.stale()) return;
    const cards = ((data && data.brands) || []).filter((card) => card && card.user && !isMe(card.user.handle)).slice(0, 6);
    if (!cards.length) return;
    brands.appendChild(el('h2', { text: t('welcome.brands_title') }));
    brands.appendChild(el('p', { class: 'hint', text: t('welcome.brands_hint') }));
    brands.appendChild(el('div', { class: 'w-people' }, cards.map((card) => userRow(card))));
    brands.hidden = false;
  })();
});

// ---------- forgot / reset / verify ----------

const loginLink = () => el('p', { class: 'hint auth-switch' }, [el('a', { href: '#/login', text: t('auth.login') })]);

// A handle or an email, and always the same answer: the server never says whether it knows the account.
register('forgot', async (root, params, ctx) => {
  if (state.me) { redirect('#/'); return; }
  ensureStyle();
  setTopBar({ back: '#/login', actions: [langButton()] });
  root.appendChild(el('h1', { text: t('auth.forgot_title') }));

  if (!state.config.email) {
    root.appendChild(el('p', { class: 'alert', id: 'f-disabled', text: t('auth.forgot_disabled') }));
    root.appendChild(loginLink());
    return;
  }

  const key = el('input', {
    type: 'text', id: 'f-key', name: 'username', maxlength: '200', autocomplete: 'username', inputmode: 'email',
    autocapitalize: 'none', autocorrect: 'off', spellcheck: 'false', enterkeyhint: 'send'
  });
  const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
  const submit = el('button', { type: 'submit', class: 'btn', id: 'f-submit', text: t('auth.forgot_submit') });
  const onsubmit = async (event) => {
    event.preventDefault();
    const value = key.value.trim();
    if (submit.disabled) return;
    if (!value) { key.focus(); return; }
    submit.disabled = true;
    error.hidden = true;
    try {
      await api('POST', '/api/auth/forgot', { handleOrEmail: value });
      if (ctx.stale()) return;
      const sent = el('p', { class: 'alert', id: 'f-sent', role: 'status' , text: t('auth.forgot_sent') });
      form.replaceWith(el('div', { class: 'stack' }, [sent, loginLink()]));
      sent.setAttribute('tabindex', '-1');
      sent.focus();
    } catch (e) {
      if (ctx.stale()) return;
      showAlert(error, e.message);
      submit.disabled = false;
    }
  };
  const form = el('form', { class: 'stack', novalidate: true, onsubmit }, [
    el('div', { class: 'field' }, [el('label', { for: 'f-key', text: t('auth.forgot_field') }), key]),
    error,
    submit,
    loginLink()
  ]);
  root.appendChild(el('p', { class: 'lede', text: t('auth.forgot_hint') }));
  root.appendChild(form);
  key.focus();
});

// The link from the mail: a new password, then in. A link the server refuses gets the way to a new one.
register('reset', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: '#/login', actions: [langButton()] });
  root.appendChild(el('h1', { text: t('auth.reset_title') }));
  const token = params.token || '';

  const invalid = () => {
    root.appendChild(el('p', { class: 'alert danger', id: 'r-invalid', role: 'alert', text: t('auth.reset_invalid') }));
    root.appendChild(el('p', { class: 'auth-links' }, [el('a', { class: 'btn-text', href: '#/forgot', id: 'r-again', text: t('auth.reset_again') })]));
  };
  if (!token) { invalid(); return; }

  const password = el('input', { type: 'password', id: 'r-password', name: 'password', maxlength: '200', autocomplete: 'new-password', enterkeyhint: 'go' });
  const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
  const submit = el('button', { type: 'submit', class: 'btn', id: 'r-submit', text: t('auth.reset_submit') });
  const onsubmit = async (event) => {
    event.preventDefault();
    if (submit.disabled) return;
    // The same rule as signup, checked here first: a server 400 then means the link, not the password.
    if (password.value.length < 8) { showAlert(error, t('auth.password_hint')); return; }
    submit.disabled = true;
    error.hidden = true;
    try {
      const me = await api('POST', '/api/auth/reset', { token, password: password.value });
      // Whoever was signed in on this phone is not this person any more: their private state goes with them.
      if (state.me && me && state.me.id !== me.id) resetSession();
      state.me = me;
      renderShell();
      toast(t('auth.reset_done'));
      if (ctx.stale()) return;
      navigate('#/');
    } catch (e) {
      if (ctx.stale()) return;
      if (e.status === 400) { form.remove(); invalid(); return; }
      showAlert(error, e.message);
      submit.disabled = false;
    }
  };
  const form = el('form', { class: 'stack', novalidate: true, onsubmit }, [
    el('div', { class: 'field' }, [
      el('label', { for: 'r-password', text: t('auth.password') }),
      password,
      el('p', { class: 'hint', text: t('auth.password_hint') })
    ]),
    error,
    submit
  ]);
  root.appendChild(form);
  password.focus();
});

// The link from the verification mail. Works signed out (the link names the account) and never signs anyone in.
register('verify', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: state.me ? '#/settings' : '#/', actions: [langButton()] });
  root.appendChild(el('h1', { text: t('auth.verify_title') }));
  const next = () => el('p', { class: 'auth-links' }, [
    el('a', { class: 'btn-text', href: state.me ? '#/settings' : '#/login', id: 'v-next', text: t(state.me ? 'settings.title' : 'auth.login') })
  ]);

  let me = null;
  try {
    me = await api('POST', '/api/auth/verify-email', { token: params.token || '' });
  } catch (e) {
    if (ctx.stale()) return;
    root.appendChild(el('p', { class: 'alert danger', id: 'v-invalid', role: 'alert', text: e.status === 400 ? t('auth.verify_invalid') : e.message }));
    root.appendChild(next());
    return;
  }
  if (ctx.stale()) return;
  // The confirmed account is the one signed in here: Settings shows it confirmed without a reload.
  if (me && state.me && state.me.id === me.id) { state.me = me; renderShell(); }
  root.appendChild(el('p', { class: 'alert', id: 'v-done', role: 'status', text: t('auth.verify_done') }));
  root.appendChild(next());
});
