// "Which one?": two photos of two outfits for the same occasion, one stylist call, one winner with the reason and a tip.
// The form is #/compare (the check's two chip rows, the note, two slots side by side, the button); the verdict lives at
// #/compare/<id> so Back returns to the form and a comparison can be reopened from a history. The ids (#cmp-slot-a,
// #cmp-slot-b, #cmp-occasion, #cmp-submit, #cmp-error, #cmp-pro-nudge, #cmp-go-pro, #cmp-result, #cmp-asked, #cmp-again)
// and the .cmp-side[data-side=a|b] / .cmp-winner / .cmp-headline / .cmp-verdict / .cmp-reason / .tip structure are part of
// the browser test contract; keep them when changing the layout.
// Round 20 — the wedge. The screen asks what the check asks (#occasions and #styles come from check.js, so the saved style
// preference is shared on purpose; the free line is the note) and sends occasion / style / note, never the one word.
// Each slot opens a sheet (#cmp-media-camera, #cmp-media-library): the library goes through prepareImage as before, the
// camera is the in-app one, borrowed through check.js's cameraReturn.handoff, which hands the still straight into the
// slot and brings the person back here. A free account refused at its day's allowance (429 with code plan_limit) sees the
// server's sentence AND the Pro nudge with the published number of comparisons a day and a Go Pro link that says where it
// came from (#/pro?from=compare); a paid Checkout that started there lands back on #/compare?ready=1, which opens slot A's
// sheet on the camera row - the camera is ready, but no permission prompt fires before a tap.
import {
  register, state, t, api, el, icon, setTopBar, navigate, signInPrompt, announce, focusHeading, pickFile, prepareImage,
  fmtNumber, MAX_EDGE, getLocale, loadMe, view, $, logoMark, scoreBadge, stagedWaitLine, sheet, toast, hashQuery
} from '../core.js';
import { occasionChips, styleChips, STYLES, SPLIT, preferredStyle, cameraReturn } from './check.js';

const SIDES = ['a', 'b'];

// What this comparison asks for, as on the check: the chips' object, handed to check.js's rows. The style is read once
// from the saved preference, so the two screens start from the same answer.
const pick = { occasion: null, style: null, loaded: false };

// Module state, like the check's: it survives a language switch and a trip to the sign-in screen (and to the camera).
const cmp = {
  note: '',
  photos: { a: null, b: null }, urls: { a: null, b: null }, busy: { a: false, b: false }, token: { a: 0, b: 0 },
  submitting: false, error: null, nudge: false, startedAt: 0,
  result: null, announced: false,
  pendingSide: null   // the slot a camera visit is filling, while the camera is up
};

const isPro = () => !!state.me && state.me.plan === 'pro' && (!state.me.proUntil || new Date(state.me.proUntil) > new Date());
const needsPro = () => !!(state.config.plans && state.config.plans.compareNeedsPro) && !isPro();
const outfitName = (side) => t('compare.outfit_' + side);
const occasionLabel = (occasion) => t('occasion.' + occasion);
const styleLabel = (style) => (style ? t('style.' + style) : t('style.none'));

register('compare', async (root, params, ctx) => {
  setTopBar({ back: params.id ? '#/compare' : '#/check', title: t('compare.title') });
  root.appendChild(el('h1', { class: 'sr-only', text: t('compare.title') }));
  // This screen is up, so no camera visit is in flight: a handoff left behind by an abandoned one must not outlive it.
  cameraReturn.handoff = null;
  cmp.pendingSide = null;
  // The shared sign-in notice is the default on ten screens and could only speak in generalities; here it was also
  // telling a guest that checks need an account, on a server whose first check is free. Say what this screen actually
  // gates, and point a guest at the thing they CAN do.
  if (!state.me) {
    const guests = !!(state.config.plans && state.config.plans.guestChecksPerDay > 0);
    root.appendChild(signInPrompt(null, t(guests ? 'compare.needs_account_free' : 'compare.needs_account')));
    return;
  }
  if (cmp.submitting) { showLoading(root); return; }   // in flight; the verdict takes over when it lands

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

  // Round 20: back from a paid Checkout that started here, with the plan flipped. The query has done its job.
  const ready = hashQuery('ready') === '1';
  if (ready) history.replaceState(history.state, '', location.pathname + location.search + '#/compare');

  const form = el('form', { class: 'stack', novalidate: true, onsubmit: (event) => { event.preventDefault(); submit(); } });
  root.appendChild(form);
  form.appendChild(el('p', { class: 'lede', text: t('compare.intro') }));

  if (!pick.loaded) { pick.style = preferredStyle(); pick.loaded = true; }
  form.appendChild(occasionChips(pick, updateSubmit));
  form.appendChild(styleChips(pick, updateSubmit));

  const note = el('input', {
    type: 'text', id: 'cmp-occasion', maxlength: '120', autocomplete: 'off', enterkeyhint: 'done', placeholder: t('occasion.note_placeholder'),
    value: cmp.note, oninput: (event) => { cmp.note = event.target.value; }
  });
  form.appendChild(el('div', { class: 'field' }, [el('label', { for: 'cmp-occasion', text: t('occasion.note_label') }), note]));

  form.appendChild(el('div', { class: 'cmp-slots' }, SIDES.map((side) => el('button', { id: 'cmp-slot-' + side, class: 'cmp-slot', type: 'button', 'data-side': side, onclick: () => chooseSlotMedia(side) }))));
  const error = el('p', { id: 'cmp-error', class: 'alert danger', role: 'alert', hidden: true });
  if (cmp.error) { error.textContent = cmp.error; error.hidden = false; cmp.error = null; }
  form.appendChild(error);
  // The Pro nudge: only after the free day's refusal (see submit), with the number the server publishes, never a literal.
  const plans = state.config.plans || {};
  form.appendChild(el('div', { class: 'notice', id: 'cmp-pro-nudge', hidden: !cmp.nudge }, [
    el('p', { text: t('compare.nudge', { n: fmtNumber(plans.proComparesPerDay || 0) }) }),
    el('a', { class: 'btn', id: 'cmp-go-pro', href: '#/pro?from=compare', style: 'margin-block-start: 12px;', text: t('pro.go') })
  ]));
  cmp.nudge = false;
  form.appendChild(el('button', { id: 'cmp-submit', class: 'btn', type: 'submit', text: t('compare.submit') }));
  form.appendChild(el('p', { class: 'hint', text: t('compare.private_note') }));
  for (const side of SIDES) renderSlot(side);
  updateSubmit();

  // "Camera ready" means the sheet is open on the camera row, not a getUserMedia call fired without a tap: a permission
  // prompt that arrives on a page the person did not tap is the thing Round 19 kept out, and iOS needs the gesture too.
  if (ready) {
    toast(t('compare.pro_ready'));
    requestAnimationFrame(() => { const slot = $('cmp-slot-a'); if (!slot) return; slot.focus({ preventScroll: true }); chooseSlotMedia('a'); });
  }
});

/** While the stylist looks at both: the mark at 96px with its flame breathing, as on a check. */
function loadingBlock() {
  const mark = logoMark(96);
  return el('div', { class: 'loading', role: 'status' }, [
    el('div', {}, [
      mark ? el('div', { class: 'mark breathing', 'aria-hidden': 'true' }, [mark]) : el('div', { class: 'loading-mark', 'aria-hidden': 'true' }),
      el('p', { id: 'loading-line', text: t('loading.line'), tabindex: '-1' })
    ])
  ]);
}

/**
 * Round 20: the block on the page with its line told in stages from cmp.startedAt (core.js stagedWaitLine), the same
 * shape as the check's wait with "looking at both looks" as its first line. A text swap is content, not motion, so it
 * runs under reduced motion too; the one-off announce() at submit is unchanged.
 */
function showLoading(root) {
  root.appendChild(loadingBlock());
  stagedWaitLine($('loading-line'), cmp.startedAt, 'loading.stage_both');
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
  if (submit) submit.disabled = !(pick.occasion && cmp.photos.a && cmp.photos.b) || cmp.submitting || cmp.busy.a || cmp.busy.b;
}
function showError(message) {
  const node = $('cmp-error');   // looked up fresh: the view may have been re-rendered during a decode
  if (node) { node.textContent = message; node.hidden = false; } else cmp.error = message;
}
function clearError() { const node = $('cmp-error'); if (node) node.hidden = true; }

/**
 * A slot was tapped: the camera or the library, in a sheet titled for that slot. No clip row, a slot is a still. Closing
 * first, then acting: the picker's input.click() must run inside the tap that chose the row.
 */
function chooseSlotMedia(side) {
  if (cmp.submitting) return;
  const list = el('div', { class: 'sheet-list' });
  const s = sheet({ title: t(cmp.urls[side] ? 'compare.replace_photo' : 'compare.add_photo', { outfit: outfitName(side) }), content: list });
  const row = (id, name, text, onclick) => el('button', { type: 'button', id, onclick: () => { s.close(); onclick(); } }, [icon(name), text]);
  list.appendChild(row('cmp-media-camera', 'camera', t('check.open_camera'), () => {
    // The in-app camera, borrowed: the still comes back into this slot, and Back returns here without a second entry.
    cmp.pendingSide = side;
    cameraReturn.fromCheck = true;
    cameraReturn.handoff = { to: '#/compare', photoOnly: true, photo: (blob) => acceptBlob(side, blob), file: (file) => acceptFile(side, file) };
    navigate('#/camera');
  }));
  list.appendChild(row('cmp-media-library', 'image', t('check.from_library'), async () => { const file = await pickFile('file'); if (file) acceptFile(side, file); }));
}

/**
 * A photo for one side from the library, downscaled before it is kept. The token guards the race: a second pick for the
 * same side, or "compare two more" while the first decode is still running, makes the first result land nowhere.
 */
async function acceptFile(side, file) {
  if (cmp.submitting) return;
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
  if (blob) keep(side, blob); else showError(t('error.image_read'));
  renderSlot(side); updateSubmit();
}

/** A still from the in-app camera (already a JPEG the size the camera frames, as the check keeps it): straight into the slot. */
function acceptBlob(side, blob) {
  if (cmp.submitting || !blob) return;
  cmp.token[side] += 1;
  cmp.busy[side] = false;
  keep(side, blob);
  cmp.pendingSide = null;
  renderSlot(side); updateSubmit();   // the nodes are usually not on screen yet (the camera is); the return render paints them
}

function keep(side, blob) {
  if (cmp.urls[side]) URL.revokeObjectURL(cmp.urls[side]);
  cmp.photos[side] = blob;
  cmp.urls[side] = URL.createObjectURL(blob);
}

async function submit() {
  if (!pick.occasion || !cmp.photos.a || !cmp.photos.b || cmp.submitting || cmp.busy.a || cmp.busy.b) return;
  cmp.submitting = true;
  cmp.startedAt = Date.now();
  updateSubmit();
  const root = view();
  root.innerHTML = '';
  showLoading(root);
  announce(t('loading.line'));
  focusHeading();
  try {
    const form = new FormData();
    form.append('occasion', pick.occasion);
    form.append('style', pick.style || '');
    form.append('note', cmp.note.trim());
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
    // The Pro nudge: the free day's refusal and nothing else - not the month's, not the global one, not a Pro account
    // at its own ceiling - and only where this server gives Pro a number of comparisons a day worth naming.
    const plans = state.config.plans || {};
    cmp.nudge = !!(e && e.status === 429 && e.code === 'plan_limit' && !isPro() && plans.proComparesPerDay > 0);
    navigate('#/compare');
  }
}

/** Back to two empty slots. The chips and the note stay: the next pair is usually for the same evening. */
function reset() {
  for (const side of SIDES) {
    cmp.token[side] += 1;
    if (cmp.urls[side]) URL.revokeObjectURL(cmp.urls[side]);
    cmp.photos[side] = null; cmp.urls[side] = null; cmp.busy[side] = false;
  }
  cmp.result = null; cmp.error = null; cmp.nudge = false; cmp.announced = false;
  navigate('#/compare');
}

/**
 * What this comparison asked for. The server sends the pair on every comparison since Round 20; a row from before, or a
 * result held over from an older client, carries only the one word, and that word says which pair it stood for.
 */
function askedPair(result) {
  if (result.occasionKind) return { occasion: result.occasionKind, style: STYLES.includes(result.style) ? result.style : null };
  const [occasion, style] = SPLIT[result.intent] || ['Everyday', null];
  return { occasion, style };
}

/**
 * The verdict: the two photos with their score rings, the winner in a gradient frame with its pill, the headlines, what
 * was asked, why, and the one tip. A close call (feedback.close, the server's word for scores within a point that
 * both work for the occasion) reads "Both work" over the winner. A comparison the stylist could not make (not two outfits, or refused) shows the reason
 * and a way back.
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

  container.appendChild(el('p', { class: 'cmp-verdict', text: t(feedback.close ? 'compare.verdict_close' : 'compare.verdict', { outfit: outfitName(winner) }) }));
  const asked = askedPair(result);
  container.appendChild(el('p', { class: 'hint', id: 'cmp-asked', text: occasionLabel(asked.occasion) + ' · ' + styleLabel(asked.style) }));
  if (feedback.reason) {
    container.appendChild(el('div', {}, [el('h2', { text: t('compare.reason') }), el('p', { class: 'cmp-reason', style: 'margin-block-start: 8px;', text: feedback.reason })]));
  }
  if (feedback.oneTip) {
    container.appendChild(el('div', {}, [el('h2', { text: t('compare.tip') }), el('div', { class: 'tip', style: 'margin-block-start: 8px;' }, [el('p', { text: feedback.oneTip })])]));
  }
  container.appendChild(el('button', { type: 'button', class: 'btn btn-ghost', id: 'cmp-again', text: t('compare.again'), onclick: reset }));
}
