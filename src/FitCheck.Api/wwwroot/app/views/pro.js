// The Pro screen: the pitch (the mark, a headline, three benefits), the price, and the way in. With Stripe live the
// button opens Checkout; while Pro is switched on by hand there is a calm note instead; someone who already has Pro
// sees the end date. Checkout returns to #/pro?checkout=success (thanks, and "me" is reloaded until the webhook has
// flipped the plan) or #/pro?checkout=cancel (just the screen again).
import { register, state, t, el, icon, api, setTopBar, signInPrompt, toast, loadMe, fmtDate, logoMark, proBadge, onLeave } from '../core.js';

const CSS = `
.pro-hero { display: grid; justify-items: center; text-align: center; gap: 14px; padding-block: 6px 4px; }
.pro-hero .pro-mark { inline-size: 84px; block-size: 84px; padding: 8px; border-radius: 50%; background: var(--bg); box-shadow: 0 0 0 6px rgba(179, 157, 255, 0.12), 0 10px 26px rgba(179, 157, 255, 0.35); }
.pro-hero .pro-mark svg { display: block; inline-size: 100%; block-size: 100%; }
.pro-hero h1 { font-size: 34px; }
.pro-hero .lede { max-inline-size: 30ch; }
.pro-benefits { list-style: none; margin: 0; padding: 0; border-block: 1px solid var(--line-soft); }
.pro-benefit { display: grid; grid-template-columns: 44px 1fr; gap: 14px; align-items: center; padding-block: 14px; border-block-end: 1px solid var(--line-soft); }
.pro-benefit:last-child { border-block-end: 0; }
.pro-benefit .pro-icon { inline-size: 44px; block-size: 44px; border-radius: 50%; display: grid; place-items: center; background: var(--accent-tint); color: var(--accent); }
.pro-benefit .pro-icon svg { inline-size: 22px; block-size: 22px; }
.pro-benefit b { display: block; font-weight: 700; font-size: 17px; line-height: 1.25; color: var(--ink); }
.pro-benefit p { margin-block-start: 3px; font-size: 14px; line-height: 1.45; color: var(--ink-2); }
.pro-price { text-align: center; }
.pro-price .pro-amount { display: block; font-family: var(--font-display); font-weight: 800; font-size: 32px; line-height: 1; color: var(--ink); direction: ltr; unicode-bidi: isolate; }
.pro-price .hint { display: block; margin-block-start: 8px; }
.pro-foot > * + * { margin-block-start: 12px; }
.pro-secure { text-align: center; }
.pro-status { display: flex; align-items: center; gap: 12px; }
.pro-status p { flex: 1; min-inline-size: 0; font-size: 15px; }
.pro-thanks { margin-block-end: 18px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/** "success" | "cancel" | null from #/pro?checkout=..., the query part of the hash (the router ignores it). */
function checkoutResult() {
  const q = location.hash.indexOf('?');
  if (q < 0) return null;
  const value = new URLSearchParams(location.hash.slice(q + 1)).get('checkout');
  return value === 'success' || value === 'cancel' ? value : null;
}

function benefit(name, title, hint) {
  return el('li', { class: 'pro-benefit' }, [
    el('span', { class: 'pro-icon', 'aria-hidden': 'true' }, [icon(name)]),
    el('div', {}, [el('b', { text: title }), el('p', { text: hint })])
  ]);
}

register('pro', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: '#/me', title: t('pro.title') });
  const plans = state.config.plans || {};
  const n = plans.proChecksPerDay || 0;
  const result = checkoutResult();
  // The query has done its job; a reload or a Back later lands on the plain screen instead of thanking twice.
  if (result) history.replaceState(history.state, '', location.pathname + location.search + '#/pro');

  const thanks = el('p', { class: 'alert pro-thanks', role: 'status', id: 'pro-thanks', text: t('pro.thanks'), hidden: result !== 'success' });
  root.appendChild(thanks);

  const mark = logoMark(68);
  root.appendChild(el('section', { class: 'pro-hero' }, [
    mark ? el('span', { class: 'pro-mark', 'aria-hidden': 'true' }, [mark]) : null,
    el('h1', { text: t('pro.headline') }),
    el('p', { class: 'lede', text: t('pro.lede') })
  ]));

  root.appendChild(el('ul', { class: 'pro-benefits' }, [
    benefit('ring', t('pro.benefit_checks', { n }), t('pro.benefit_checks_hint', { free: plans.freeChecksPerDay || 0 })),
    benefit('flip', t('pro.benefit_compare'), t('pro.benefit_compare_hint')),
    benefit('sparkle', t('pro.benefit_insights'), t('pro.benefit_insights_hint'))
  ]));

  if (plans.proPriceText) {
    root.appendChild(el('p', { class: 'pro-price', id: 'pro-price' }, [
      el('span', { class: 'pro-amount', text: plans.proPriceText }),
      plans.billing ? el('span', { class: 'hint', text: t('pro.price_note') }) : null
    ]));
  }

  const foot = el('div', { class: 'pro-foot', id: 'pro-foot' });
  root.appendChild(foot);

  const paint = () => {
    foot.replaceChildren();
    const me = state.me;
    if (!me) { foot.appendChild(signInPrompt()); return; }
    if (me.plan === 'pro') {
      foot.appendChild(el('div', { class: 'notice pro-status', id: 'pro-current' }, [
        proBadge(me),
        el('p', { text: me.proUntil ? t('pro.current', { date: fmtDate(me.proUntil) }) : t('pro.current_open') })
      ]));
      return;
    }
    if (!plans.billing) { foot.appendChild(el('p', { class: 'notice', id: 'pro-manual', text: t('pro.manual') })); return; }
    const go = el('button', { type: 'button', class: 'btn', id: 'pro-go', text: t('pro.go') });
    go.addEventListener('click', async () => {
      if (go.disabled) return;
      go.disabled = true;
      go.textContent = t('common.loading');
      try {
        const { url } = await api('POST', '/api/billing/checkout');
        location.href = url;   // Stripe's hosted page; it comes back to #/pro?checkout=...
      } catch (e) {
        if (ctx.stale()) return;
        toast(e.message || t('error.generic'));
        go.disabled = false;
        go.textContent = t('pro.go');
      }
    });
    foot.appendChild(go);
    foot.appendChild(el('p', { class: 'hint pro-secure', text: t('pro.secure') }));
  };
  paint();

  // Back from a paid Checkout: the webhook that flips the plan can land a moment after the person does, so "me" is
  // read again a few times until it says Pro (or the screen is left). Not awaited: the screen is up already.
  if (result === 'success' && state.me) {
    let stopped = false;
    onLeave(() => { stopped = true; });
    const poll = async () => {
      for (let attempt = 0; attempt < 6 && !stopped; attempt++) {
        await loadMe();
        if (ctx.stale() || stopped) return;
        paint();
        if (!state.me || state.me.plan === 'pro') return;
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    };
    poll();
  }
});
