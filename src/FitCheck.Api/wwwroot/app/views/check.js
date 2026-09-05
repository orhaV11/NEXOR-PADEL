// Check and result: pick a photo, say where the outfit is going, let the stylist look, read the verdict, post the look.
// Ported from the Phase 2 monolith onto the kit. The ids (#photo, #submit, #occasion, #check-error, #result, #post-open,
// #post-confirm, #post-link, #caption, #challenge-pick) and the .score/.result-headline/.items/.working/.tip/.bar structure
// are part of the browser test contract; keep them when changing the layout.
import {
  register, state, t, api, el, icon, setTopBar, navigate, requireSignIn, signInPrompt, sheet, toast, announce, focusHeading, onLeave, pickFile, prepareImage, fmtNumber, fmtPercent, intentLabel, INTENTS, MAX_EDGE, isBrand, isMe, loadMe, getLocale, reducedMotion, copyText, view, $, redirect, showAlert
} from '../core.js';

const SCORE_COUNT_MS = 900;

// ---------- check ----------

register('check', async (root) => {
  const ck = state.check;
  setTopBar({ title: t('check.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('check.title') }));
  if (!state.me) { root.appendChild(signInPrompt()); return; }
  if (ck.busy) { root.appendChild(loadingBlock()); return; }   // a check is in flight; the result view takes over when it lands

  const form = el('form', { class: 'stack', novalidate: true, onsubmit: (event) => { event.preventDefault(); submitCheck(); } });
  root.appendChild(form);

  if (ck.challenge) {
    form.appendChild(el('div', { class: 'alert between', id: 'challenge-banner' }, [
      el('div', {}, [
        el('b', { text: t('check.entering', { title: ck.challenge.title }) }),
        el('div', { class: 'muted', style: 'font-size: 13px; margin-block-start: 2px;', text: t('check.entering_hint', { tag: '#' + ck.challenge.tag }) })
      ]),
      el('button', { type: 'button', class: 'btn btn-secondary btn-sm', style: 'flex: none;', text: t('common.cancel'), onclick: () => { ck.challenge = null; navigate('#/check'); } })
    ]));
  }

  const chips = el('div', { class: 'chips', role: 'group', 'aria-label': t('a11y.intent_group') });
  for (const intent of INTENTS) {
    chips.appendChild(el('button', {
      type: 'button', class: 'chip', 'data-intent': intent, 'aria-pressed': String(ck.intent === intent), text: intentLabel(intent),
      onclick: () => {
        ck.intent = intent;
        for (const chip of chips.children) chip.setAttribute('aria-pressed', String(chip.dataset.intent === intent));
        updateSubmit();
      }
    }));
  }
  form.appendChild(el('div', {}, [el('h2', { text: t('check.intent_label') }), el('div', { style: 'margin-block-start: 10px;' }, [chips])]));

  const occasion = el('input', {
    type: 'text', id: 'occasion', maxlength: '120', autocomplete: 'off', enterkeyhint: 'done', placeholder: t('check.occasion_placeholder'),
    value: ck.occasion, oninput: (event) => { ck.occasion = event.target.value; }
  });
  form.appendChild(el('div', { class: 'field' }, [el('label', { for: 'occasion', text: t('check.occasion_label') }), occasion]));

  form.appendChild(el('button', { id: 'photo', class: 'photo', type: 'button', onclick: choosePhoto }));
  const error = el('p', { id: 'check-error', class: 'alert danger', role: 'alert', hidden: true });
  if (ck.error) { error.textContent = ck.error; error.hidden = false; ck.error = null; }
  form.appendChild(error);
  form.appendChild(el('button', { id: 'submit', class: 'btn', type: 'submit', text: t('check.submit') }));
  form.appendChild(el('p', { class: 'hint', text: t('check.private_note') }));
  renderPhoto();
  updateSubmit();
});

function loadingBlock() {
  return el('div', { class: 'loading', role: 'status' }, [
    el('div', {}, [el('div', { class: 'loading-mark', 'aria-hidden': 'true' }), el('p', { text: t('loading.line'), tabindex: '-1' })])
  ]);
}

/** Paints the photo button from state: empty prompt, "preparing", or the preview with a replace hint. */
function renderPhoto() {
  const photo = $('photo');
  if (!photo) return;
  const ck = state.check;
  const label = ck.photoBusy ? t('check.photo_processing') : t(ck.previewUrl ? 'check.photo_replace' : 'check.photo_add');
  photo.classList.toggle('has-image', !!ck.previewUrl);
  photo.setAttribute('aria-label', label);
  photo.setAttribute('aria-busy', String(ck.photoBusy));
  photo.innerHTML = '';
  if (ck.previewUrl) {
    photo.appendChild(el('img', { src: ck.previewUrl, alt: t('a11y.photo_preview') }));
    photo.appendChild(el('span', { class: 'photo-replace', text: label }));
  } else {
    photo.appendChild(el('span', { class: 'photo-empty' }, [
      ck.photoBusy ? el('span', { class: 'loading-mark', 'aria-hidden': 'true', style: 'margin-block-end: 0;' }) : icon('camera'),
      el('strong', { text: label }),
      ck.photoBusy ? null : el('span', { class: 'hint', text: t('check.photo_hint') })
    ]));
  }
}

function updateSubmit() {
  const submit = $('submit');
  const ck = state.check;
  if (submit) submit.disabled = !(ck.intent && ck.photo) || ck.busy || ck.photoBusy;
}

/**
 * Opens the picker, then downscales the chosen file before it is kept. photoToken guards the race: a second pick, a
 * sign-out or "check another" while the first decode is still running makes the first result land nowhere.
 */
async function choosePhoto() {
  const ck = state.check;
  if (ck.busy) return;
  const file = await pickFile('file');
  if (!file) return;
  const errorNode = $('check-error');
  if (errorNode) errorNode.hidden = true;
  const token = ++ck.photoToken;
  ck.photo = null; ck.photoBusy = true;
  renderPhoto(); updateSubmit();
  let blob = null;
  try { blob = await prepareImage(file, MAX_EDGE); } catch (e) { blob = null; }
  // prepareImage hands back the original when it cannot decode it; a file that is not an image is no use to anyone.
  if (blob === file && file.type && !file.type.startsWith('image/')) blob = null;
  if (token !== ck.photoToken) return;
  ck.photoBusy = false;
  if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
  if (blob) { ck.photo = blob; ck.previewUrl = URL.createObjectURL(blob); }
  else {
    ck.photo = null; ck.previewUrl = null;
    const node = $('check-error');   // looked up again: the view may have been re-rendered during the decode
    if (node) { node.textContent = t('error.image_read'); node.hidden = false; } else ck.error = t('error.image_read');
  }
  renderPhoto(); updateSubmit();
}

async function submitCheck() {
  const ck = state.check;
  if (!ck.intent || !ck.photo || ck.busy || ck.photoBusy) return;
  ck.busy = true;
  updateSubmit();
  const root = view();
  root.innerHTML = '';
  root.appendChild(loadingBlock());
  announce(t('loading.line'));
  focusHeading();
  try {
    const form = new FormData();
    form.append('intent', ck.intent);
    form.append('occasion', ck.occasion.trim());
    form.append('language', getLocale());
    form.append('image', ck.photo, 'outfit.jpg');
    state.result = await api('POST', '/api/checks', form);
    state.resultAnimated = false;
    state.resultPostId = null;
    ck.busy = false;
    loadMe();   // streak may have moved
    navigate('#/result');
  } catch (e) {
    ck.busy = false;
    ck.error = e && e.status === 401 ? null : (e && e.message ? e.message : t('error.generic'));   // a lost session already re-rendered
    navigate('#/check');
  }
}

/** Back to a fresh check. "Try another photo" keeps the challenge; "Check another" drops it. */
function checkAnother(keepChallenge) {
  const ck = state.check;
  state.result = null; state.resultPostId = null; state.resultAnimated = false;
  ck.photo = null; ck.photoBusy = false; ck.error = null; ck.photoToken += 1;
  if (!keepChallenge) ck.challenge = null;
  if (ck.previewUrl) URL.revokeObjectURL(ck.previewUrl);
  ck.previewUrl = null;
  navigate('#/check');
}

// ---------- result ----------

register('result', async (root) => {
  const result = state.result;
  if (!result) { redirect('#/check'); return; }
  setTopBar({ title: t('result.title') });
  const feedback = result.feedback || {};
  const status = feedback.status || result.status;
  const intent = intentLabel(result.intent);
  const container = el('div', { id: 'result', class: 'stack' });
  root.appendChild(container);

  if (status !== 'ok') {
    const rejected = status === 'rejected';
    if (!state.resultAnimated) announce(t(rejected ? 'result.rejected_title' : 'result.not_outfit_title'));
    state.resultAnimated = true;
    container.appendChild(el('div', { class: 'state' }, [
      el('h1', { text: t(rejected ? 'result.rejected_title' : 'result.not_outfit_title') }),
      el('p', { class: 'lede', style: 'margin-block-start: 12px;', text: (!rejected && feedback.message) || t(rejected ? 'result.rejected_body' : 'result.not_outfit_body') }),
      el('button', { type: 'button', class: 'btn', style: 'margin-block-start: 24px;', text: t('result.try_again'), onclick: () => checkAnother(true) })
    ]));
    return;
  }

  const animate = !state.resultAnimated && !reducedMotion();
  if (!state.resultAnimated) announce(t('a11y.score', { score: fmtNumber(feedback.score) }) + '. ' + feedback.headline);
  state.resultAnimated = true;
  const scoreNode = el('span', { class: 'score', text: animate ? fmtNumber(0) : fmtNumber(feedback.score) });
  const fill = el('div', { class: 'bar-fill', style: animate ? 'inline-size: 0%;' : 'transition: none; inline-size: ' + feedback.intentMatch + '%;' });

  container.appendChild(el('div', {}, [
    el('div', { class: 'hero', role: 'img', 'aria-label': t('a11y.score', { score: fmtNumber(feedback.score) }) }, [
      scoreNode, el('span', { class: 'score-out', 'aria-hidden': 'true', text: t('result.out_of') })
    ]),
    el('h1', { class: 'result-headline', text: feedback.headline, style: 'margin-block-start: 16px;' }),
    feedback.vibe ? el('p', { class: 'vibe', text: feedback.vibe }) : null
  ]));
  container.appendChild(el('div', {}, [
    el('div', { class: 'match-label' }, [el('span', { text: t('result.intent_match', { intent }) }), el('span', { text: fmtPercent(feedback.intentMatch / 100) })]),
    el('div', { class: 'bar', role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(feedback.intentMatch), 'aria-label': t('result.intent_match', { intent }) }, [fill])
  ]));
  if (feedback.items && feedback.items.length) {
    container.appendChild(el('div', {}, [
      el('h2', { text: t('result.items') }),
      el('ul', { class: 'items', style: 'margin-block-start: 4px;' }, feedback.items.map((item) => el('li', {}, [
        el('span', { class: 'dot ' + item.verdict, 'aria-hidden': 'true' }),
        el('div', {}, [
          el('div', { class: 'item-name' }, [item.name, el('span', { class: 'item-verdict', text: t('verdict.' + item.verdict) })]),
          item.note ? el('div', { class: 'item-note', text: item.note }) : null
        ])
      ])))
    ]));
  }
  if (feedback.working && feedback.working.length) {
    container.appendChild(el('div', {}, [
      el('h2', { text: t('result.working') }),
      el('ul', { class: 'working', style: 'margin-block-start: 6px;' }, feedback.working.map((line) => el('li', { text: line })))
    ]));
  }
  if (feedback.oneTip) {
    container.appendChild(el('div', {}, [el('h2', { text: t('result.tip') }), el('div', { class: 'tip', style: 'margin-block-start: 8px;' }, [el('p', { text: feedback.oneTip })])]));
  }

  const postArea = el('div');
  container.appendChild(postArea);
  renderPostArea(postArea, result);
  container.appendChild(el('div', { class: 'row' }, [
    el('button', { type: 'button', class: 'btn btn-secondary', onclick: () => shareResult(result) }, [icon('share'), t('result.share')]),
    el('button', { type: 'button', class: 'btn btn-ghost', text: t('result.again'), onclick: () => checkAnother(false) })
  ]));

  if (animate) {
    requestAnimationFrame(() => { fill.style.inlineSize = feedback.intentMatch + '%'; });
    animateScore(scoreNode, feedback.score);
  }
});

/** "Post it" until the check is public, then the link to the look. */
function renderPostArea(area, result) {
  area.innerHTML = '';
  const postId = state.resultPostId || result.postId;
  if (postId) {
    area.appendChild(el('a', { class: 'btn', id: 'post-link', href: '#/post/' + encodeURIComponent(postId) }, [icon('check'), t('result.posted') + ' · ' + t('result.view_post')]));
    return;
  }
  area.appendChild(el('button', { type: 'button', class: 'btn', id: 'post-open', text: t('result.post'), onclick: () => openPostSheet(area, result) }));
}

/**
 * The post sheet: caption, an open challenge of the same intent (the one the check was started from is preselected),
 * product links for brands. Posting makes the photo, intent, score and headline public; the tip and breakdown stay private.
 */
function openPostSheet(area, result) {
  if (!requireSignIn('#/result')) return;
  // Entering a challenge is just its hashtag in the caption; the server links the look while the challenge is open.
  const pending = state.check.challenge;
  const caption = el('textarea', { id: 'caption', maxlength: '140', rows: '3', autocomplete: 'off', placeholder: t('result.caption_placeholder') });
  if (pending && pending.tag) caption.value = '#' + pending.tag + ' ';

  const productRows = [];
  let productsField = null;
  if (isBrand()) {
    const grid = el('div', { class: 'products-grid' });
    for (let i = 0; i < 3; i++) {
      const row = {
        label: el('input', { type: 'text', maxlength: '60', autocomplete: 'off', placeholder: t('result.product_label'), 'aria-label': t('result.product_label') }),
        url: el('input', { type: 'url', maxlength: '500', inputmode: 'url', autocapitalize: 'off', autocomplete: 'off', placeholder: t('result.product_url'), 'aria-label': t('result.product_url') }),
        price: el('input', { type: 'text', maxlength: '20', autocomplete: 'off', placeholder: t('result.product_price'), 'aria-label': t('result.product_price') })
      };
      productRows.push(row);
      grid.appendChild(row.label); grid.appendChild(row.url); grid.appendChild(row.price);
    }
    productsField = el('div', { class: 'field' }, [el('span', { class: 'label', text: t('result.products') }), grid]);
  }

  const error = el('p', { class: 'alert danger', role: 'alert', hidden: true });
  const confirm = el('button', { type: 'button', class: 'btn', id: 'post-confirm', text: t('result.confirm_post') });
  const cancel = el('button', { type: 'button', class: 'btn btn-ghost', text: t('common.cancel'), onclick: () => s.close() });
  const content = el('div', { class: 'stack' }, [
    el('p', { class: 'muted', text: t('result.post_intro') }),
    el('div', { class: 'field' }, [el('label', { for: 'caption', text: t('result.caption') }), caption, el('span', { class: 'hint', text: t('result.caption_hint') })]),
    productsField,
    error,
    el('div', { class: 'row' }, [confirm, cancel])
  ]);
  const s = sheet({ title: t('result.post_title'), content });

  confirm.addEventListener('click', async () => {
    confirm.disabled = true; error.hidden = true;
    const products = productRows
      .filter((r) => r.label.value.trim() || r.url.value.trim())
      .map((r) => ({ label: r.label.value.trim(), url: r.url.value.trim(), price: r.price.value.trim() || null }));
    try {
      const post = await api('POST', '/api/posts', { checkId: result.id, caption: caption.value, products });
      state.resultPostId = post.id;
      result.postId = post.id;
      state.check.challenge = null;
      s.close();
      toast(t('result.posted'));
      renderPostArea(area, result);
      const link = $('post-link');
      if (link) link.focus({ preventScroll: true });
    } catch (e) {
      error.textContent = e && e.message ? e.message : t('error.generic');
      error.hidden = false;
      confirm.disabled = false;
    }
  });

  requestAnimationFrame(() => { if (document.contains(caption)) { caption.focus(); caption.setSelectionRange(caption.value.length, caption.value.length); } });
}

function animateScore(node, target) {
  const start = performance.now();
  let frame = 0;
  const tick = (now) => {
    const progress = Math.min(1, (now - start) / SCORE_COUNT_MS);
    const eased = 1 - Math.pow(1 - progress, 3);
    node.textContent = fmtNumber(Math.round(eased * target));
    if (progress < 1) frame = requestAnimationFrame(tick);
  };
  frame = requestAnimationFrame(tick);
  onLeave(() => { cancelAnimationFrame(frame); node.textContent = fmtNumber(target); });
}

async function shareResult(result) {
  if (state.sharing) return;
  state.sharing = true;
  try {
    const feedback = result.feedback || {};
    const text = t('result.share_text', { score: fmtNumber(feedback.score), intent: intentLabel(result.intent), tip: feedback.oneTip || '' });
    if (navigator.share) {
      try { await navigator.share({ text }); return; }
      catch (e) { if (e && (e.name === 'AbortError' || e.name === 'InvalidStateError')) return; }
    }
    await copyText(text, t('result.copied'));
  } finally { state.sharing = false; }
}
