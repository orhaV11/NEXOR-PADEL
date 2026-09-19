// "Which one?": two photos of two outfits for the same intent, one stylist call, one winner with the reason and a tip.
// The form is #/compare (intent chips, the occasion, two slots side by side, the button); the verdict lives at
// #/compare/<id> so Back returns to the form and a comparison can be reopened from a history. The ids (#cmp-slot-a,
// #cmp-slot-b, #cmp-occasion, #cmp-submit, #cmp-error, #cmp-result, #cmp-again) and the .cmp-side[data-side=a|b] /
// .cmp-winner / .cmp-headline / .cmp-reason / .tip structure are part of the browser test contract; keep them when
// changing the layout. The photos are chosen from the library (pickFile on the shared #file input) and downscaled with
// prepareImage; there is no camera here, two outfits are rarely on the same person at the same time.
import {
  register, state, t, api, el, icon, setTopBar, navigate, signInPrompt, announce, focusHeading, pickFile, prepareImage,
  fmtNumber, intentLabel, INTENTS, MAX_EDGE, getLocale, loadMe, view, $, logoMark, scoreBadge
} from '../core.js';

const SIDES = ['a', 'b'];

// Module state, like the check's: it survives a language switch and a trip to the sign-in screen.
const cmp = {
  intent: null, occasion: '',
  photos: { a: null, b: null }, urls: { a: null, b: null }, busy: { a: false, b: false }, token: { a: 0, b: 0 },
  submitting: false, error: null,
  result: null, announced: false
};

const isPro = () => !!state.me && state.me.plan === 'pro' && (!state.me.proUntil || new Date(state.me.proUntil) > new Date());
const needsPro = () => !!(state.config.plans && state.config.plans.compareNeedsPro) && !isPro();
const outfitName = (side) => t('compare.outfit_' + side);

register('compare', async (root, params, ctx) => {
  setTopBar({ back: params.id ? '#/compare' : '#/check', title: t('compare.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('compare.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (cmp.submitting) { root.appendChild(loadingBlock()); return; }   // in flight; the verdict takes over when it lands

  if (params.id) {
    let result = cmp.result && cmp.result.id === params.id ? cmp.result : null;
    if (!result) {
      result = await api('GET', '/api/compare/' + encodeURIComponent(params.id));
      if (ctx.stale()) return;
      cmp.result = result;
      cmp.announced = false;
    }
    renderResult(root, result);
    return;
  }

  if (needsPro()) {
    root.appendChild(el('div', { class: 'notice', id: 'cmp-pro' }, [
      el('h3', { text: t('compare.pro_title') }),
      el('p', { class: 'muted', text: t('compare.pro_body') }),
      el('a', { class: 'btn', href: '#/pro', style: 'margin-block-start: 14px;', text: t('compare.go_pro') })
    ]));
    return;
  }

  const form = el('form', { class: 'stack', novalidate: true, onsubmit: (event) => { event.preventDefault(); submit(); } });
  root.appendChild(form);
  form.appendChild(el('p', { class: 'lede', text: t('compare.intro') }));

  const chips = el('div', { class: 'chips', role: 'group', 'aria-label': t('a11y.intent_group') });
  for (const intent of INTENTS) {
    chips.appendChild(el('button', {
      type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(cmp.intent === intent), text: intentLabel(intent),
      onclick: () => {
        cmp.intent = intent;
        for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === intent));
        updateSubmit();
      }
    }));
  }
  form.appendChild(el('div', {}, [el('h2', { text: t('check.intent_label') }), el('div', { style: 'margin-block-start: 10px;' }, [chips])]));

  const occasion = el('input', {
    type: 'text', id: 'cmp-occasion', maxlength: '120', autocomplete: 'off', enterkeyhint: 'done', placeholder: t('check.occasion_placeholder'),
    value: cmp.occasion, oninput: (event) => { cmp.occasion = event.target.value; }
  });
  form.appendChild(el('div', { class: 'field' }, [el('label', { for: 'cmp-occasion', text: t('check.occasion_label') }), occasion]));

  form.appendChild(el('div', { class: 'cmp-slots' }, SIDES.map((side) => el('button', { id: 'cmp-slot-' + side, class: 'cmp-slot', type: 'button', 'data-side': side, onclick: () => pick(side) }))));
  const error = el('p', { id: 'cmp-error', class: 'alert danger', role: 'alert', hidden: true });
  if (cmp.error) { error.textContent = cmp.error; error.hidden = false; cmp.error = null; }
  form.appendChild(error);
  form.appendChild(el('button', { id: 'cmp-submit', class: 'btn', type: 'submit', text: t('compare.submit') }));
  form.appendChild(el('p', { class: 'hint', text: t('compare.private_note') }));
  for (const side of SIDES) renderSlot(side);
  updateSubmit();
});

/** While the stylist looks at both: the mark at 96px with its flame breathing, as on a check. */
function loadingBlock() {
  const mark = logoMark(96);
  return el('div', { class: 'loading', role: 'status' }, [
    el('div', {}, [
      mark ? el('div', { class: 'mark breathing', 'aria-hidden': 'true' }, [mark]) : el('div', { class: 'loading-mark', 'aria-hidden': 'true' }),
      el('p', { text: t('loading.line'), tabindex: '-1' })
    ])
  ]);
}

/** Paints one slot from state: the empty prompt, "preparing", or the photo with a replace pill. The letter stays on top. */
function renderSlot(side) {
  const slot = $('cmp-slot-' + side);
  if (!slot) return;
  const url = cmp.urls[side];
  const busy = cmp.busy[side];
  const label = busy ? t('compare.processing') : t(url ? 'compare.replace_photo' : 'compare.add_photo', { outfit: outfitName(side) });
  slot.classList.toggle('has-image', !!url);
  slot.setAttribute('aria-label', label);
  slot.setAttribute('aria-busy', String(busy));
  slot.innerHTML = '';
  slot.appendChild(el('span', { class: 'cmp-letter', 'aria-hidden': 'true', text: side.toUpperCase() }));
  if (url) {
    slot.appendChild(el('img', { src: url, alt: '' }));
    slot.appendChild(el('span', { class: 'cmp-replace', text: label }));
  } else {
    slot.appendChild(el('span', { class: 'cmp-empty' }, [
      busy ? el('span', { class: 'loading-mark', 'aria-hidden': 'true', style: 'margin-block-end: 0;' }) : icon('image'),
      el('strong', { text: outfitName(side) }),
      busy ? el('span', { class: 'hint', text: t('compare.processing') }) : el('span', { class: 'hint', text: t('compare.add_hint') })
    ]));
  }
}

function updateSubmit() {
  const submit = $('cmp-submit');
  if (submit) submit.disabled = !(cmp.intent && cmp.photos.a && cmp.photos.b) || cmp.submitting || cmp.busy.a || cmp.busy.b;
}
function showError(message) {
  const node = $('cmp-error');   // looked up fresh: the view may have been re-rendered during a decode
  if (node) { node.textContent = message; node.hidden = false; } else cmp.error = message;
}
function clearError() { const node = $('cmp-error'); if (node) node.hidden = true; }

/**
 * A photo for one side from the library, downscaled before it is kept. The token guards the race: a second pick for the
 * same side, or "compare two more" while the first decode is still running, makes the first result land nowhere.
 */
async function pick(side) {
  if (cmp.submitting) return;
  const file = await pickFile('file');
  if (!file) return;
  clearError();
  const token = ++cmp.token[side];
  cmp.busy[side] = true;
  renderSlot(side); updateSubmit();
  let blob = null;
  try { blob = await prepareImage(file, MAX_EDGE); } catch (e) { blob = null; }
  // prepareImage hands back the original when it cannot decode it; a file that is not an image is no use to anyone.
  if (blob === file && file.type && !file.type.startsWith('image/')) blob = null;
  if (token !== cmp.token[side]) return;
  cmp.busy[side] = false;
  if (blob) {
    if (cmp.urls[side]) URL.revokeObjectURL(cmp.urls[side]);
    cmp.photos[side] = blob;
    cmp.urls[side] = URL.createObjectURL(blob);
  } else showError(t('error.image_read'));
  renderSlot(side); updateSubmit();
}

async function submit() {
  if (!cmp.intent || !cmp.photos.a || !cmp.photos.b || cmp.submitting || cmp.busy.a || cmp.busy.b) return;
  cmp.submitting = true;
  updateSubmit();
  const root = view();
  root.innerHTML = '';
  root.appendChild(loadingBlock());
  announce(t('loading.line'));
  focusHeading();
  try {
    const form = new FormData();
    form.append('intent', cmp.intent);
    form.append('occasion', cmp.occasion.trim());
    form.append('language', getLocale());
    form.append('imageA', cmp.photos.a, 'outfit-a.jpg');
    form.append('imageB', cmp.photos.b, 'outfit-b.jpg');
    const result = await api('POST', '/api/compare', form);
    cmp.result = result;
    cmp.announced = false;
    cmp.submitting = false;
    loadMe();   // the day's count may have moved
    navigate('#/compare/' + encodeURIComponent(result.id));
  } catch (e) {
    cmp.submitting = false;
    cmp.error = e && e.status === 401 ? null : (e && e.message ? e.message : t('error.generic'));   // a lost session already re-rendered
    navigate('#/compare');
  }
}

/** Back to two empty slots. The intent and the occasion stay: the next pair is usually for the same evening. */
function reset() {
  for (const side of SIDES) {
    cmp.token[side] += 1;
    if (cmp.urls[side]) URL.revokeObjectURL(cmp.urls[side]);
    cmp.photos[side] = null; cmp.urls[side] = null; cmp.busy[side] = false;
  }
  cmp.result = null; cmp.error = null; cmp.announced = false;
  navigate('#/compare');
}

/**
 * The verdict: the two photos with their score rings, the winner in a gradient frame with its pill, the headlines, why,
 * and the one tip. A comparison the stylist could not make (not two outfits, or refused) shows the reason and a way back.
 */
function renderResult(root, result) {
  const feedback = result.feedback || {};
  const status = feedback.status || result.status;
  const container = el('div', { id: 'cmp-result', class: 'stack' });
  root.appendChild(container);

  if (status !== 'ok') {
    const rejected = status === 'rejected';
    if (!cmp.announced) announce(t(rejected ? 'compare.rejected_title' : 'compare.not_outfit_title'));
    cmp.announced = true;
    container.appendChild(el('div', { class: 'state' }, [
      el('h2', { class: 'cmp-state-title', text: t(rejected ? 'compare.rejected_title' : 'compare.not_outfit_title') }),
      el('p', { class: 'lede', style: 'margin-block-start: 12px;', text: (!rejected && feedback.message) || t(rejected ? 'compare.rejected_body' : 'compare.not_outfit_body') }),
      el('button', { type: 'button', class: 'btn', id: 'cmp-again', style: 'margin-block-start: 24px;', text: t('compare.try_again'), onclick: reset })
    ]));
    return;
  }

  const winner = feedback.winner === 'b' ? 'b' : 'a';
  const scores = { a: feedback.scoreA, b: feedback.scoreB };
  const headlines = { a: feedback.headlineA, b: feedback.headlineB };
  if (!cmp.announced) announce(t('compare.a11y_result', { outfit: outfitName(winner), a: fmtNumber(scores.a), b: fmtNumber(scores.b) }));
  cmp.announced = true;

  container.appendChild(el('div', { class: 'cmp-sides' }, SIDES.map((side) => {
    const isWinner = side === winner;
    const url = result['imageUrl' + side.toUpperCase()] || cmp.urls[side];
    return el('figure', { class: 'cmp-side' + (isWinner ? ' cmp-winner' : ''), 'data-side': side, 'aria-label': t('compare.a11y_score', { outfit: outfitName(side), score: fmtNumber(scores[side]) }) }, [
      el('div', { class: 'cmp-frame' }, [
        el('div', { class: 'cmp-photo' }, [
          url ? el('img', { src: url, alt: '' }) : null,
          el('span', { class: 'cmp-letter', 'aria-hidden': 'true', text: side.toUpperCase() }),
          isWinner ? el('span', { class: 'tag accent cmp-winner-pill', text: t('compare.winner') }) : null,
          scoreBadge(scores[side])
        ])
      ]),
      el('figcaption', {}, [
        el('span', { class: 'cmp-side-name', text: outfitName(side) }),
        headlines[side] ? el('b', { class: 'cmp-headline', text: headlines[side] }) : null
      ])
    ]);
  })));

  container.appendChild(el('p', { class: 'cmp-verdict', text: t('compare.verdict', { outfit: outfitName(winner) }) }));
  if (feedback.reason) {
    container.appendChild(el('div', {}, [el('h2', { text: t('compare.reason') }), el('p', { class: 'cmp-reason', style: 'margin-block-start: 8px;', text: feedback.reason })]));
  }
  if (feedback.oneTip) {
    container.appendChild(el('div', {}, [el('h2', { text: t('compare.tip') }), el('div', { class: 'tip', style: 'margin-block-start: 8px;' }, [el('p', { text: feedback.oneTip })])]));
  }
  container.appendChild(el('button', { type: 'button', class: 'btn btn-ghost', id: 'cmp-again', text: t('compare.again'), onclick: reset }));
}
