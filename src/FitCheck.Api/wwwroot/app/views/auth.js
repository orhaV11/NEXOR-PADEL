// Auth and onboarding: sign in, join (handle, password and the 16+ line, nothing else) and the welcome screen
// that follows a signup: pick the styles you wear, follow a few brands, then in. Both auth pages are public;
// a signed-in person who lands on them is sent home. Ported from the Phase 2 authView onto the kit.
import {
  register, state, t, api, el, iconButton, navigate, renderShell, setTopBar, openLanguageSheet, getLocale, INTENTS, intentLabel, userRow, toast, isMe, redirect, showAlert
} from '../core.js';

// The few rules the shared stylesheet does not have: the two-line checkbox text, bigger onboarding steps and chips.
const CSS = `
.auth-check-text { display: grid; gap: 2px; }
.auth-switch { text-align: center; }
.auth-switch a { color: var(--accent); text-decoration: none; font-weight: 500; }
.auth-guidelines { margin-block-start: 0; padding-inline-start: 34px; }   /* under the 16+ text, past the box */
.auth-guidelines .btn-text { font-size: 14px; padding-block: 0; }
.w-step > * + * { margin-block-start: 10px; }
.w-step h2 { font-family: var(--font-display); font-size: 20px; line-height: 1.15; font-weight: 800; color: var(--ink); }
.w-chips .chip { min-block-size: 44px; padding-inline: 16px; }
.w-people { display: flex; flex-direction: column; }
.w-actions { display: flex; flex-direction: column; gap: 8px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

const AUTH_ROUTE = /^#\/(login|signup|welcome)(\/|$)/;
/** Where to go once signed in: the page that asked for the sign-in, unless that was an auth page itself. Clears it. */
function takeReturnTo() {
  const back = state.returnTo && !AUTH_ROUTE.test(state.returnTo) ? state.returnTo : null;
  state.returnTo = null;
  return back || '#/';
}
const langButton = () => iconButton('globe', t('lang.label'), openLanguageSheet, { id: 'lang' });

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
    const age = signup ? el('input', { type: 'checkbox', id: 'a-age', name: 'age' }) : null;
    const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
    const submit = el('button', { type: 'submit', class: 'btn', id: 'a-submit', text: t(signup ? 'auth.submit_signup' : 'auth.submit_login') });

    const onsubmit = async (event) => {
      event.preventDefault();
      if (submit.disabled) return;
      submit.disabled = true;
      error.hidden = true;
      try {
        const me = signup
          ? await api('POST', '/api/auth/signup', { handle: handle.value.trim(), password: password.value, confirmed16Plus: age.checked, language: getLocale() })
          : await api('POST', '/api/auth/login', { handle: handle.value.trim(), password: password.value });
        state.me = me;
        renderShell();
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
      signup ? el('label', { class: 'checkline', for: 'a-age' }, [
        age,
        el('span', { class: 'auth-check-text' }, [el('span', { text: t('auth.age') }), el('span', { class: 'hint', text: t('auth.privacy') })])
      ]) : null,
      // The rules they are agreeing to, a link outside the label so tapping it never toggles the box.
      signup ? el('p', { class: 'auth-guidelines' }, [el('a', { class: 'btn-text', id: 'a-guidelines', href: '#/guidelines', text: t('guidelines.link') })]) : null,
      error,
      submit,
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

  // Step 2 fills in once Explore answers; it stays hidden when there are no brands (or the endpoint is not there yet).
  const brands = el('section', { class: 'w-step', hidden: true });

  let busy = false;
  const leave = () => navigate(takeReturnTo());
  const done = el('button', { type: 'button', class: 'btn', id: 'w-done', text: t('welcome.done') });
  const skip = el('button', { type: 'button', class: 'btn btn-ghost', id: 'w-skip', text: t('common.skip'), onclick: () => { if (!busy) leave(); } });
  done.addEventListener('click', async () => {
    if (busy) return;
    busy = true; done.disabled = true; skip.disabled = true;
    const interests = INTENTS.filter((intent) => picked.has(intent));
    if (interests.length) {
      try {
        const me = await api('PATCH', '/api/users/me', { interests });
        if (me) state.me = me;
        toast(t('welcome.saved'));
      } catch (e) { toast(e.message); }
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
