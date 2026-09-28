// The Pro screen: the pitch (the mark, a headline, the benefits), the price, and the way in. With Stripe live the
// button opens Checkout; while Pro is switched on by hand there is a calm note instead; someone who already has Pro
// sees the end date and, with Stripe live, "Manage subscription" (Stripe's Billing Portal, Round 11: the card, the
// invoices, cancelling; it returns to #/settings and the webhook changes the plan here), or with Pro switched on by
// hand, a line saying to write to us. Checkout returns to #/pro?checkout=success (thanks, and "me" is reloaded until the webhook has
// flipped the plan; no button meanwhile, a second tap would open a second subscription) or #/pro?checkout=cancel
// (just the screen again).
// Round 14 — the page is about what the person GETS, not how high a number goes. Every benefit below is gated on a flag
// from /api/config, and a benefit whose flag is false is not drawn at all: NOTHING on this page may promise a thing this
// server cannot do. PlansTests reads this file's benefit lines and fails the build over an invented promise, so a new
// benefit needs both a flag here and a row in that table. The cap is not a benefit; it is one fair-use line, once, last.
// Round 20 — billing: a yearly price beside the monthly one (a two-button toggle, drawn only where the server has BOTH a
// yearly price id and a yearly amount in the reader's currency, so nothing is shown that Checkout cannot sell; the
// saving is computed here from the two numbers and never typed, and since the review a year that saves nothing against
// twelve months is not offered at all), and a no-card trial (Plans:ProTrialDays), offered as one guarded benefit line
// only to an account /api/billing/state says is eligible - a signed-in person who already went through Checkout once
// sees the plain price. The trial line names what the trial turns into on the interval the toggle stands on.
// Round 20 — the wedge: #/pro?from=compare|wardrobe says which surface sent the person here, and the page tells the
// server once (POST /api/funnel/pro-opened, a funnel tally, never awaited); from=compare also asks Checkout to come back
// as #/pro?checkout=success&return=compare, and once the poll sees the plan flip the page redirects to
// #/compare?ready=1, where the camera is ready. Every query value is read before the query is stripped. A cancelled
// Checkout comes back as #/pro?checkout=cancel&return=compare, and a second try from there asks for the same return: the
// intent lasts until Checkout succeeds or the person leaves this page.
import { register, state, t, el, icon, api, setTopBar, signInPrompt, toast, loadMe, fmtDate, logoMark, proBadge, onLeave, intlLocale, getLocale, hashQuery, redirect } from '../core.js';

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
.pro-interval { display: grid; grid-template-columns: 1fr 1fr; gap: 4px; padding: 4px; border-radius: 999px; background: var(--bg-2, var(--accent-tint)); margin-block-end: 14px; }
.pro-interval button { border: 0; border-radius: 999px; padding: 10px 12px; font: inherit; font-weight: 700; color: var(--ink-2); background: transparent; cursor: pointer; }
.pro-interval button[aria-pressed="true"] { background: var(--bg); color: var(--ink); box-shadow: 0 1px 4px rgba(0, 0, 0, 0.12); }
.pro-price .pro-saving { display: block; margin-block-start: 6px; font-weight: 700; color: var(--accent); }
.pro-price .pro-amount { display: block; font-family: var(--font-display); font-weight: 800; font-size: 32px; line-height: 1; color: var(--ink); direction: ltr; unicode-bidi: isolate; }
.pro-price .hint { display: block; margin-block-start: 8px; }
.pro-foot > * + * { margin-block-start: 12px; }
.pro-secure { text-align: center; }
.pro-status { display: flex; align-items: center; gap: 12px; }
.pro-status p { flex: 1; min-inline-size: 0; font-size: 15px; }
.pro-thanks { margin-block-end: 18px; }
.pro-manage-hint { text-align: center; }
.pro-fair { text-align: center; margin-block-start: 16px; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/** "success" | "cancel" | null from #/pro?checkout=..., the query part of the hash (the router ignores it). */
function checkoutResult() {
  const value = hashQuery('checkout');
  return value === 'success' || value === 'cancel' ? value : null;
}

/** The surfaces that send people here and are counted: the compare screen's refusal and the wardrobe's Pro line. */
const FROM_SURFACES = ['compare', 'wardrobe'];

/** "Manage subscription": the Billing Portal in this tab; 404 (a Pro without a customer behind it) and the rest are toasted. */
function manageButton(ctx) {
  const button = el('button', { type: 'button', class: 'btn btn-secondary', id: 'billing-manage', text: t('billing.manage') });
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

/**
 * Which currency to charge this reader in. The REGION, not the language: someone reading in Hebrew from Berlin holds
 * euros, and someone reading in English from Tel Aviv holds shekels. navigator.languages carries both halves, so the
 * first entry that names a region this server prices in wins; failing that, the server's own default currency.
 *
 * A region we have no PRICE for falls back too. Showing a currency with no price behind it would mean inventing one
 * from an exchange rate, and a rate that moved overnight must never move what somebody is charged.
 */
function currencyForReader(plans) {
  const prices = plans.proPrices || {};
  const byRegion = plans.currencyByRegion || {};
  const tags = [].concat(navigator.languages || [], navigator.language || [], getLocale());
  for (const tag of tags) {
    let region = '';
    try {
      region = (new Intl.Locale(tag).region || '').toUpperCase();
    } catch (e) {
      // A malformed tag from an old browser: read the region off the end of the string instead.
      const parts = String(tag).split('-');
      region = parts.length > 1 ? parts[parts.length - 1].toUpperCase() : '';
    }
    const currency = region && byRegion[region];
    if (currency && prices[currency] > 0) return currency;
  }
  // A country this server does not price in: the world currency when one is set (usually USD), else the home one.
  return plans.fallbackCurrency || plans.proPriceCurrency || '';
}

/**
 * The price in the reader's own language: Intl puts the symbol where that language puts it and uses its own digits.
 * Returns '' when this server publishes no amount, which is how the price stays off the page entirely. A currency
 * code the browser does not know throws rather than guessing, and an unpriced page is better than a wrong price.
 */
function money(amount, currency) {
  const value = Number(amount);
  if (!Number.isFinite(value) || value <= 0 || !currency) return '';
  try {
    return new Intl.NumberFormat(intlLocale(), { style: 'currency', currency: currency }).format(value);
  } catch (e) {
    return '';
  }
}

/** One benefit row; a hint that is '' draws no line under the title, and hintId names the one the toggle repaints. */
function benefit(name, title, hint, hintId) {
  return el('li', { class: 'pro-benefit' }, [
    el('span', { class: 'pro-icon', 'aria-hidden': 'true' }, [icon(name)]),
    el('div', {}, [el('b', { text: title }), hint ? el('p', { id: hintId, text: hint }) : null])
  ]);
}

register('pro', async (root, params, ctx) => {
  ensureStyle();
  setTopBar({ back: '#/me', title: t('pro.title') });
  const plans = state.config.plans || {};
  const n = plans.proChecksPerDay || 0;
  const result = checkoutResult();
  // Round 20: where the person came from, and where a paid Checkout should land them. Both read now, before the query goes.
  const from = FROM_SURFACES.includes(hashQuery('from')) ? hashQuery('from') : null;
  const returnTo = hashQuery('return') === 'compare' ? 'compare' : null;
  // Sent by a refused compare, or back from a Checkout that was (a cancel carries return=compare too): either way the
  // next Checkout from this page comes back to the compare screen.
  const backToCompare = from === 'compare' || returnTo === 'compare';
  // The open is counted once, on arrival, by a signed-in account; the answer is nobody's business here and never awaited.
  if (from && state.me) api('POST', '/api/funnel/pro-opened', { from }).catch(() => {});

  // Round 20: the trial is offered to a signed-out visitor (a new account is eligible) and to a signed-in one only when
  // the server says so - one read of the billing state, and a read that fails means no trial line rather than a promise.
  let billingState = null;
  if (state.me && plans.billing && plans.proTrialDays > 0) {
    try { billingState = await api('GET', '/api/billing/state'); } catch (e) { billingState = null; }
    if (ctx.stale()) return;
  }
  const trialOffered = !state.me || !!(billingState && billingState.trialDays > 0);
  const trialDays = plans.proTrialDays || 0;
  // The query has done its job; a reload or a Back later lands on the plain screen instead of thanking (or counting) twice.
  if (result || from || returnTo) history.replaceState(history.state, '', location.pathname + location.search + '#/pro');

  const thanks = el('p', { class: 'alert pro-thanks', role: 'status', id: 'pro-thanks', text: t('pro.thanks'), hidden: result !== 'success' });
  root.appendChild(thanks);

  const mark = logoMark(68);
  root.appendChild(el('section', { class: 'pro-hero' }, [
    mark ? el('span', { class: 'pro-mark', 'aria-hidden': 'true' }, [mark]) : null,
    el('h1', { text: t('pro.headline') }),
    el('p', { class: 'lede', text: t('pro.lede') })
  ]));

  // Round 16: money is written differently in every language this app speaks - where the symbol sits, which digits
  // are used, how the decimal is marked - and proPriceText is ONE string shown to all four. So the server sends the
  // amount and the currency, and the browser, which already knows all of that, writes it. proPriceText stays as the
  // override for a price no format covers ("first month free, then...").
  const currency = currencyForReader(plans);
  const amount = (plans.proPrices && plans.proPrices[currency]) || plans.proPriceAmount;
  const priceText = plans.proPriceText || money(amount, currency);
  // Round 20: the yearly amount in the SAME currency, or nothing. A year is only ever shown where the server can sell
  // one (plans.yearly: a yearly price id is set) in the currency the reader was quoted; no conversion, ever.
  const yearlyAmount = (plans.proYearlyPrices && plans.proYearlyPrices[currency]) || (currency === plans.proPriceCurrency ? plans.proYearlyPriceAmount : 0) || 0;
  const yearlyText = plans.yearly && yearlyAmount > 0 ? money(yearlyAmount, currency) : '';
  const monthlyAmount = Number(amount) > 0 ? Number(amount) : 0;
  // The saving is arithmetic over the two numbers the owner typed, never a third setting: 1 - year / twelve months.
  const savingPct = yearlyText && monthlyAmount > 0 ? Math.round((1 - yearlyAmount / (12 * monthlyAmount)) * 100) : 0;
  let interval = 'month';
  // Review of Round 20: what the trial turns into, on the interval the toggle stands on - after the trial Checkout sells
  // the year when Yearly is pressed - and nothing at all when no price is published, rather than "Then  a month".
  const trialThen = () => (interval === 'year' && yearlyText ? t('pro.per_year', { price: yearlyText }) : priceText ? t('pro.per_month', { price: priceText }) : '');

  // Each line: an optional guard from /api/config, then the benefit. One benefit per line, because PlansTests reads
  // them from this file and matches every key against the thing in the server that makes it true.
  root.appendChild(el('ul', { class: 'pro-benefits' }, [
    // Round 20 — the no-card trial, first because it is the line that changes what the button does: Checkout opens with
    // trial_period_days and no card asked for exactly where Stripe is live, the server has a trial and this account
    // never went through Checkout (GET /api/billing/state says trialDays > 0); the webhook grants the days, not a month.
    plans.billing && plans.proTrialDays > 0 && trialOffered ? benefit('timer', t('pro.benefit_trial', { days: trialDays }), trialThen() && t('pro.benefit_trial_hint', { price: trialThen() }), 'pro-trial-hint') : null,
    benefit('flip', t('pro.benefit_which'), t(plans.compareNeedsPro ? 'pro.benefit_which_hint_only' : 'pro.benefit_which_hint')),
    plans.tasteNeedsPro ? benefit('sparkle', t('pro.benefit_taste'), t('pro.benefit_taste_hint')) : null,
    plans.wardrobe ? benefit('bag', t('pro.benefit_wardrobe'), t('pro.benefit_wardrobe_hint', { free: plans.wardrobeNames })) : null,
    plans.compareNeedsPro ? benefit('ring', t('pro.benefit_insights'), t('pro.benefit_insights_hint')) : null,
    // Round 19 — Tomorrow: the whole feature where the server sells it to Pro alone (or gives free none of it), else Pro's
    // own day of it from the whole wardrobe. The numbers are the server's as published, never a stand-in: a cap of 0 is
    // a feature that is off, and the line goes rather than promise ten.
    plans.tomorrow && plans.proSuggestionsPerDay > 0 && (plans.tomorrowNeedsPro || plans.freeSuggestionsPerDay <= 0) ? benefit('calendar', t('pro.benefit_tomorrow'), t('pro.benefit_tomorrow_hint')) : null,
    plans.tomorrow && plans.proSuggestionsPerDay > 0 && !plans.tomorrowNeedsPro && plans.freeSuggestionsPerDay > 0 ? benefit('calendar', t('pro.benefit_tomorrow_daily'), t('pro.benefit_tomorrow_daily_hint', { n: plans.proSuggestionsPerDay, free: plans.freeSuggestionsPerDay, pieces: plans.wardrobeNamesPro, seen: plans.wardrobeNames })) : null
  ]));
  // The cap, once and last, as what it is: a fair-use brake, not the product. Both numbers as the server really
  // enforces them (clamped to Limits:ChecksPerDay before they leave /api/config).
  // Round 16: the month is the allowance that means something. A daily number reads as a boast nobody tests - "30 a
  // day" when a person does one or two - while the month is the number that is actually there to be used, and the one
  // the price is built on. The day stays as the burst limit it is, and is not advertised.
  const month = plans.proCallsPerMonth || 0;
  root.appendChild(el('p', { class: 'hint pro-fair', id: 'pro-fair', text: month > 0
    ? t('pro.allowance_month', { n: month })
    : t('pro.fair_use', { checks: n, compares: plans.proComparesPerDay || 0 }) }));

  // Round 20: the interval toggle, only when the year can really be sold in this currency, then the price block, which
  // re-renders with the toggle: a month reads "a month" and "billed monthly", a year reads "a year", "billed once a
  // year" and the saving. Review of Round 20: a yearly amount at or above twelve months saves nothing, and a year that
  // costs as much as the months it replaces is not offered at all (the doctor warns "no saving"), so no toggle.
  const price = el('p', { class: 'pro-price', id: 'pro-price' });
  const paintPrice = () => {
    price.replaceChildren();
    if (interval === 'year' && yearlyText) {
      price.appendChild(el('span', { class: 'pro-amount', text: t('pro.per_year', { price: yearlyText }) }));
      price.appendChild(el('span', { class: 'hint', text: t('pro.price_note_yearly') }));
      if (savingPct > 0) price.appendChild(el('span', { class: 'pro-saving', id: 'pro-saving', text: t('pro.yearly_saving', { pct: savingPct }) }));
      return;
    }
    price.appendChild(el('span', { class: 'pro-amount', text: t('pro.per_month', { price: priceText }) }));
    if (plans.billing) price.appendChild(el('span', { class: 'hint', text: t('pro.price_note') }));
  };
  if (priceText && yearlyText && savingPct > 0 && plans.billing) {
    const pick = (next) => {
      interval = next;
      monthButton.setAttribute('aria-pressed', String(next === 'month'));
      yearButton.setAttribute('aria-pressed', String(next === 'year'));
      paintPrice();
      // The trial line above promises the price after the trial, so it follows the toggle too.
      const trialHint = root.querySelector('#pro-trial-hint');
      if (trialHint) trialHint.textContent = t('pro.benefit_trial_hint', { price: trialThen() });
    };
    const monthButton = el('button', { type: 'button', id: 'pro-interval-month', 'aria-pressed': 'true', text: t('pro.interval_month'), onclick: () => pick('month') });
    const yearButton = el('button', { type: 'button', id: 'pro-interval-year', 'aria-pressed': 'false', text: t('pro.interval_year'), onclick: () => pick('year') });
    root.appendChild(el('div', { class: 'pro-interval', id: 'pro-interval', role: 'group', 'aria-label': t('pro.interval_month') + ' / ' + t('pro.interval_year') }, [monthButton, yearButton]));
  }
  if (priceText) {
    paintPrice();
    root.appendChild(price);
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
      if (plans.billing) {
        foot.appendChild(manageButton(ctx));
        foot.appendChild(el('p', { class: 'hint pro-manage-hint', text: t('billing.manage_hint') }));
      } else {
        foot.appendChild(el('p', { class: 'hint pro-manage-hint', id: 'billing-manual', text: t('billing.manual_hint') }));
      }
      return;
    }
    if (!plans.billing) { foot.appendChild(el('p', { class: 'notice', id: 'pro-manual', text: t('pro.manual') })); return; }
    // Just paid and the webhook has not landed yet: the thanks note above says so, and there is no button to tap again.
    if (result === 'success') return;
    // Round 20: the button says what the trial line promised, when it is drawn; a second Checkout on the same account
    // never trials, and the server decides that whatever the button read.
    const goText = () => (trialOffered && plans.proTrialDays > 0 ? t('pro.go_trial', { days: trialDays }) : t('pro.go'));
    const go = el('button', { type: 'button', class: 'btn', id: 'pro-go', text: goText() });
    go.addEventListener('click', async () => {
      if (go.disabled) return;
      go.disabled = true;
      go.textContent = t('common.loading');
      try {
        // Round 17: the currency this page QUOTED, so Stripe charges the number the person just read rather than
        // whatever the price is denominated in. The server drops anything it has no price for, so this can only ever
        // pick from what the server itself offers. Round 20: and the interval the toggle stands on; a year the server
        // cannot sell in this currency is refused (400), never quietly sold as a month.
        // Round 20: a person sent here by a refused compare is landed back on that screen once the plan has flipped.
        const back = backToCompare ? '&return=compare' : '';
        const { url } = await api('POST', '/api/billing/checkout?currency=' + encodeURIComponent(currency) + '&interval=' + encodeURIComponent(interval) + back);
        location.href = url;   // Stripe's hosted page; it comes back to #/pro?checkout=...
      } catch (e) {
        if (ctx.stale()) return;
        toast(e.message || t('error.generic'));
        // 409: this account is Pro already and this tab did not know (loaded before the webhook flipped the plan).
        if (e.status === 409) { await loadMe(); if (!ctx.stale()) paint(); return; }
        go.disabled = false;
        go.textContent = goText();
      }
    });
    foot.appendChild(go);
    foot.appendChild(el('p', { class: 'hint pro-secure', text: t('pro.secure') }));
  };
  paint();

  // Back from a paid Checkout: the webhook that flips the plan can land a moment after the person does, so "me" is
  // read again a few times until it says Pro (or the screen is left). Not awaited: the screen is up already.
  // Round 20: when Checkout was asked to return to the compare screen, the flip is what sends the person there - a
  // compare before it would spend the free day. If the webhook has not landed within the six tries, the thanks stays
  // and the person is on #/pro as before: the honest fallback.
  if (result === 'success' && state.me) {
    let stopped = false;
    onLeave(() => { stopped = true; });
    const poll = async () => {
      for (let attempt = 0; attempt < 6 && !stopped; attempt++) {
        await loadMe();
        if (ctx.stale() || stopped) return;
        paint();
        if (!state.me) return;
        if (state.me.plan === 'pro') {
          if (returnTo === 'compare') redirect('#/compare?ready=1');
          return;
        }
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    };
    poll();
  }
});
