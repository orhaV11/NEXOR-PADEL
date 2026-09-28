// OREVOSH — the growth loop, client side (Round 13). Three small things, all of them links:
//
//   1. ?via — an invite link is /?via=<handle> (and /look/<id>?via=<handle> on a share). This module reads it off the
//      address as soon as the app loads, keeps it in localStorage (try/catch: private mode simply has no memory), and
//      hands it back once, at signup, as invitedBy. The server validates the handle and gives both accounts one more
//      check for the day; an unknown or stale handle is quietly not an invite. ?via=share is the share loop's own
//      marker, never a person, so it is never kept.
//   2. The person's own invite link, with a copy button and the phone's share sheet.
//   3. A look's public address (/look/<id>), for "Copy link" and the share sheet on a posted look.
//   4. Round 20 — ?src: an entry link (/go/<source>) redirects to /?src=<source>#/check. The source is kept exactly like
//      the invite (localStorage, try/catch), sent with every guest check while it is kept (a guest may check twice, then
//      sign up) and handed to the signup once, where it is forgotten, so one arrival attributes at most one account.
//      Sign-out does not clear it: it is the device's arrival, not the person's. Never a cookie. Review of Round 21: a
//      browser with somebody signed in forgets both words as soon as it knows (forgetArrival, called by core.js's
//      loadMe): an account has nothing left for them to attribute, and the privacy page says the browser keeps them
//      until a signup, not for as long as an existing account keeps using the phone.
//
// Loaded at boot because views/post.js imports it, which main.js imports; capture() runs on import, before any view.
// Once read, ?via and ?src leave the address (the rest of the query and the hash stay), so a reload or a restored tab
// does not keep them again after the signup spent them.
import { state, t, el, sheet, toast, copyText, VIA_SHARE, configuredOrigin, linkOrigin, publicLookUrl, inviteUrl, lookInviteUrl, shareLookUrl, lookShareText } from './core.js';

// The link builders moved to core.js so the share button can reach them without an await (see shareLookUrl there).
// They are re-exported from here because this is where they were, and every caller still says invite.js.
export { VIA_SHARE, configuredOrigin, linkOrigin, publicLookUrl, inviteUrl, lookInviteUrl, shareLookUrl };

const KEY = 'orevosh.invite';
/** The shape a handle has (AuthEndpoints' own regex): anything else in ?via is somebody's noise. */
const HANDLE = /^[\p{L}\p{N}_.]{2,40}$/u;
/** The reserved value that marks an arrival from a share card or a share video rather than from a person. */

function read() { try { return localStorage.getItem(KEY) || ''; } catch (e) { return ''; } }
function write(handle) { try { localStorage.setItem(KEY, handle); } catch (e) { /* private mode */ } }
function forget() { try { localStorage.removeItem(KEY); } catch (e) { /* private mode */ } }

/**
 * Reads ?via off an address and keeps it when it is a handle. Returns what is kept now (the fresh one, or the one from
 * an earlier visit). Called once on import; exported so the landing page's own tiny script and a test can call it too.
 */
export function capture(search) {
  let via = '';
  try {
    via = (new URLSearchParams(search === undefined ? location.search : search).get('via') || '').trim();
  } catch (e) {
    return read();
  }
  if (!via || via.toLowerCase() === VIA_SHARE || !HANDLE.test(via)) return read();
  write(via);
  return via;
}

/** The invite the signup form sends as invitedBy, if any; asked for once and then forgotten, so it counts for one account. */
export function takeInvite() {
  const handle = read();
  if (handle) forget();
  return handle || null;
}

/** The invite kept for this browser, without spending it (the welcome line on the signup screen could read it). */
export const pendingInvite = () => read() || null;

// ---------- Round 20: the entry link's source ----------

const SOURCE_KEY = 'orevosh.source';
/** The shape a source has (FunnelOptions on the server): a short lower-case word, never a secret. */
const SOURCE = /^[a-z0-9]{1,16}$/;

function readSource() { try { return localStorage.getItem(SOURCE_KEY) || ''; } catch (e) { return ''; } }
function writeSource(code) { try { localStorage.setItem(SOURCE_KEY, code); } catch (e) { /* private mode */ } }
function forgetSource() { try { localStorage.removeItem(SOURCE_KEY); } catch (e) { /* private mode */ } }

/**
 * Reads ?src off an address and keeps it when it has the shape of a source. Returns what is kept now. Called once on
 * import beside capture(); the server validates the word against its allowlist, so a lie here bends only its own row.
 */
export function captureSource(search) {
  let src = '';
  try {
    src = (new URLSearchParams(search === undefined ? location.search : search).get('src') || '').trim().toLowerCase();
  } catch (e) {
    return readSource();
  }
  if (!src || !SOURCE.test(src)) return readSource();
  writeSource(src);
  return src;
}

/** The source the guest check sends, without spending it: a guest may check twice before signing up. */
export const pendingSource = () => readSource() || null;

/** Both words, gone: the invite and the source. For a browser somebody is signed in on (review of Round 21). */
export function forgetArrival() { forget(); forgetSource(); }

/** The source the signup sends; asked for once and then forgotten, so one arrival is at most one account. */
export function takeSource() {
  const code = readSource();
  if (code) forgetSource();
  return code || null;
}

/** The address as a person reads it on a button: no scheme, no trailing slash. */
export const pretty = (url) => url.replace(/^https?:\/\//, '').replace(/\/$/, '');

/**
 * The system share sheet with a URL, falling back to the clipboard where there is none (a desktop browser, an in-app
 * webview). Never rejects; a dismissed sheet is not an error.
 */
export async function shareUrl(url, text, doneMessage) {
  if (state.sharing) return;
  state.sharing = true;
  try {
    if (navigator.share) {
      try { await navigator.share({ title: t('app.name'), text, url }); return; }
      catch (e) { if (e && (e.name === 'AbortError' || e.name === 'InvalidStateError')) return; }
    }
    await copyText(url, doneMessage);
  } finally { state.sharing = false; }
}

const CSS = `
.inv-link { display: flex; align-items: center; gap: 8px; min-block-size: 44px; padding: 10px 14px; background: var(--surface-2);
  border-radius: var(--radius-sm); color: var(--ink-2); font-size: 14px; overflow-wrap: anywhere; direction: ltr; text-align: start; }
.inv-actions { display: flex; flex-wrap: wrap; gap: 10px; }
.inv-actions .btn { min-block-size: 44px; flex: 1 1 auto; }
`;
let styled = false;
function ensureStyle() {
  if (styled) return;
  styled = true;
  document.head.appendChild(el('style', { text: CSS }));
}

/**
 * "Invite friends": the person's link, a copy button and the share sheet, in a bottom sheet. Signed in only — the link
 * is their handle. Both sides get one more check the day an invite is accepted, and the sheet says so plainly.
 */
export function openInviteSheet() {
  if (!state.me) return;
  ensureStyle();
  const url = inviteUrl(state.me.handle);
  const copy = el('button', { type: 'button', class: 'btn btn-secondary', id: 'invite-copy', text: t('invite.copy') });
  const share = el('button', { type: 'button', class: 'btn', id: 'invite-share', text: t('invite.share') });
  copy.addEventListener('click', () => copyText(url, t('invite.copied')));
  share.addEventListener('click', () => shareUrl(url, t('invite.share_text'), t('invite.copied')));
  sheet({
    title: t('invite.title'),
    content: el('div', { class: 'stack' }, [
      el('p', { class: 'hint', text: t('invite.hint') }),
      el('p', { class: 'label', text: t('invite.link_label') }),
      el('p', { class: 'inv-link', id: 'invite-url', dir: 'ltr', text: pretty(url) }),
      el('div', { class: 'inv-actions' }, [share, copy])
    ])
  });
}

/** The row that opens it, for Settings and for the person's own profile. */
export function inviteButton(id) {
  return el('button', { type: 'button', class: 'btn btn-secondary', id: id || 'invite-friends', text: t('invite.title'), onclick: openInviteSheet });
}

/**
 * "Share this look": the public address of a posted look, the share sheet and a copy button. The link carries the
 * sharer's own ?via when they are signed in, so a look that travels is also an invite. Round 20: both buttons carry the
 * same sentence the share sheet sends (lookShareText, the Hebrew a challenge to the group) — the copy puts it on the
 * line above the address, so a paste into a chat reads as a message and not a bare link.
 */
export function openLookLinkSheet(post) {
  ensureStyle();
  const url = shareLookUrl(post.id);
  const sentence = lookShareText(post);
  const copy = el('button', { type: 'button', class: 'btn btn-secondary', id: 'link-copy', text: t('link.copy') });
  const share = el('button', { type: 'button', class: 'btn', id: 'link-share', text: t('link.share') });
  copy.addEventListener('click', () => copyText(sentence + '\n' + url, t('link.copied')));
  share.addEventListener('click', () => shareUrl(url, sentence, t('link.copied')));
  sheet({
    title: t('link.title'),
    content: el('div', { class: 'stack' }, [
      el('p', { class: 'hint', text: t('link.hint') }),
      el('p', { class: 'inv-link', id: 'link-url', dir: 'ltr', text: pretty(url) }),
      el('div', { class: 'inv-actions' }, [share, copy])
    ])
  });
}

/** Takes the words just read off the address out of it, with history.replaceState: no navigation, no new entry. */
function dropFromAddress(names) {
  try {
    const query = new URLSearchParams(location.search);
    if (!names.some((name) => query.has(name))) return;
    for (const name of names) query.delete(name);
    const rest = query.toString();
    history.replaceState(history.state, '', location.pathname + (rest ? '?' + rest : '') + location.hash);
  } catch (e) { /* the address keeps them; the signup still spends what is kept */ }
}

capture();
captureSource();
dropFromAddress(['via', 'src']);
