// OREVOSH — the growth loop, client side (Round 13). Three small things, all of them links:
//
//   1. ?via — an invite link is /?via=<handle> (and /look/<id>?via=<handle> on a share). This module reads it off the
//      address as soon as the app loads, keeps it in localStorage (try/catch: private mode simply has no memory), and
//      hands it back once, at signup, as invitedBy. The server validates the handle and gives both accounts one more
//      check for the day; an unknown or stale handle is quietly not an invite. ?via=share is the share loop's own
//      marker, never a person, so it is never kept.
//   2. The person's own invite link, with a copy button and the phone's share sheet.
//   3. A look's public address (/look/<id>), for "Copy link" and the share sheet on a posted look.
//
// Loaded at boot because views/post.js imports it, which main.js imports; capture() runs on import, before any view.
import { state, t, el, sheet, toast, copyText } from './core.js';

const KEY = 'orevosh.invite';
/** The shape a handle has (AuthEndpoints' own regex): anything else in ?via is somebody's noise. */
const HANDLE = /^[\p{L}\p{N}_.]{2,40}$/u;
/** The reserved value that marks an arrival from a share card or a share video rather than from a person. */
export const VIA_SHARE = 'share';

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

/** The origin the server publishes for itself (/api/config publicOrigin), or '' when it publishes none. */
export function configuredOrigin() {
  const configured = state.config && state.config.publicOrigin;
  const origin = typeof configured === 'string' ? configured.trim().replace(/\/+$/, '') : '';
  if (!origin) return '';
  try { return new URL(origin).origin; } catch (e) { return ''; }
}

/**
 * The origin a link made inside the app carries: the configured one, else this browser's own. A page the person is
 * looking at is an honest address to copy; only the share card and the share video refuse to guess (they are files that
 * travel, and a localhost line on a story would be a lie).
 */
export const linkOrigin = () => configuredOrigin() || location.origin;

/** A look's public address: the page a share lands on, readable with no app and no account. */
export const publicLookUrl = (postId) => linkOrigin() + '/look/' + postId;

/** A person's invite link, and the same link with a look on it when they are sharing one. */
export const inviteUrl = (handle) => linkOrigin() + '/?via=' + encodeURIComponent(handle);
export const lookInviteUrl = (postId, handle) => publicLookUrl(postId) + '?via=' + encodeURIComponent(handle);

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
 * sharer's own ?via when they are signed in, so a look that travels is also an invite.
 */
export function openLookLinkSheet(post) {
  ensureStyle();
  const url = state.me ? lookInviteUrl(post.id, state.me.handle) : publicLookUrl(post.id);
  const copy = el('button', { type: 'button', class: 'btn btn-secondary', id: 'link-copy', text: t('link.copy') });
  const share = el('button', { type: 'button', class: 'btn', id: 'link-share', text: t('link.share') });
  copy.addEventListener('click', () => copyText(url, t('link.copied')));
  share.addEventListener('click', () => shareUrl(url, t('link.share_text', { name: post.user.name }), t('link.copied')));
  sheet({
    title: t('link.title'),
    content: el('div', { class: 'stack' }, [
      el('p', { class: 'hint', text: t('link.hint') }),
      el('p', { class: 'inv-link', id: 'link-url', dir: 'ltr', text: pretty(url) }),
      el('div', { class: 'inv-actions' }, [share, copy])
    ])
  });
}

capture();
