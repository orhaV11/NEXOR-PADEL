// Web Push on the phone: can this browser do it, is it already on, turning it on and off, and the sign-out hook.
// The server only knows endpoints; the browser decides its own state from the PushManager. Every call here is best
// effort and never blocks a screen: a push service that is down turns into a toast, not a broken settings page.
import { api, state, t, isIos, isStandalone } from './core.js';

/** The VAPID public key as pushManager.subscribe wants it: base64url text into raw bytes. */
export function urlBase64ToUint8Array(base64) {
  const padded = base64 + '='.repeat((4 - base64.length % 4) % 4);
  const raw = atob(padded.replace(/-/g, '+').replace(/_/g, '/'));
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && typeof Notification !== 'undefined';

/**
 * What the settings switch can offer: 'ios_install' (Safari on iPhone only pushes from the installed app),
 * 'unsupported', 'not_configured' (no VAPID key on this server), 'denied' (blocked in the browser), or 'ready'.
 */
export function pushSupport() {
  if (isIos() && !isStandalone()) return 'ios_install';
  if (!supported()) return 'unsupported';
  if (!state.config.pushPublicKey) return 'not_configured';
  if (Notification.permission === 'denied') return 'denied';
  return 'ready';
}

/** The app's service worker registration, or null. Waits a little for a first install, never forever. */
async function registration() {
  if (!supported()) return null;
  const existing = await navigator.serviceWorker.getRegistration('/');
  if (existing && existing.active) return existing;
  return Promise.race([navigator.serviceWorker.ready, new Promise((resolve) => setTimeout(() => resolve(null), 4000))]);
}

/** This browser's current subscription, or null. */
export async function getPushSubscription() {
  try {
    const reg = await registration();
    return reg ? await reg.pushManager.getSubscription() : null;
  } catch (e) {
    return null;
  }
}

/** True when the subscription was made with the key this server uses now (rotated keys need a fresh subscription). */
function madeWithKey(sub, key) {
  const current = sub.options && sub.options.applicationServerKey;
  if (!current) return true;
  const a = new Uint8Array(current);
  const b = urlBase64ToUint8Array(key);
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

function subscriptionBody(sub) {
  const json = sub.toJSON();
  return { endpoint: json.endpoint, p256dh: json.keys && json.keys.p256dh, auth: json.keys && json.keys.auth };
}

/**
 * Asks for permission, subscribes this browser and registers it with the server. Resolves with the subscription,
 * or null when the person dismissed the permission prompt. Throws with a message the settings screen can show.
 */
export async function enablePush() {
  const key = state.config.pushPublicKey;
  if (!key) throw new Error(t('push.not_configured'));
  const reg = await registration();
  if (!reg) throw new Error(t('push.unsupported'));
  const permission = await Notification.requestPermission();
  if (permission === 'denied') throw new Error(t('push.denied'));
  if (permission !== 'granted') return null;
  let sub = await reg.pushManager.getSubscription();
  if (sub && !madeWithKey(sub, key)) {
    try { await sub.unsubscribe(); } catch (e) { /* stale anyway */ }
    sub = null;
  }
  if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(key) });
  try {
    await api('POST', '/api/push/subscriptions', subscriptionBody(sub));
  } catch (e) {
    // The server did not take it: leave nothing behind in the browser either, so the switch tells the truth.
    try { await sub.unsubscribe(); } catch (e2) { /* nothing to undo */ }
    throw e;
  }
  return sub;
}

/** Re-registers an existing browser subscription with the signed-in account. Idempotent; survives a server reset or a new sign-in on the same phone. */
export async function syncPush(sub) {
  if (!sub || !state.me || !state.config.pushPublicKey) return;
  try { await api('POST', '/api/push/subscriptions', subscriptionBody(sub)); } catch (e) { /* next visit */ }
}

/** Drops this browser's subscription on both sides. The server first, while the session cookie is still there. */
export async function disablePush() {
  const sub = await getPushSubscription();
  if (!sub) return false;
  try { await api('DELETE', '/api/push/subscriptions', { endpoint: sub.endpoint }); } catch (e) { /* a 410 will clean it up later */ }
  try { await sub.unsubscribe(); } catch (e) { /* already gone */ }
  return true;
}

/** For sign-out: call before the logout request, so the next person on this phone is not pinged about the last one. Never throws. */
export async function unsubscribePush() {
  try { await disablePush(); } catch (e) { /* best effort */ }
}

/** A "your look caught fire" ping to your own browsers. */
export async function sendTestPush() {
  await api('POST', '/api/push/test');
}
