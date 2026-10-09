import { api } from './api';

/**
 * Web push (Firebase Cloud Messaging) for signed-in users: asks the browser's permission (only after a click), gets the FCM token and
 * registers it with the API, which stores it against the user. The Firebase settings come from the API (public identifiers); the server
 * credentials never reach the browser. Firebase's code is only loaded when push is actually used.
 */
export interface WebPushConfig { apiKey: string; authDomain: string; projectId: string; messagingSenderId: string; appId: string; vapidKey: string }

/** unsupported: this browser can't; unconfigured: the server has no web push; default: not asked yet; denied: blocked; enabled: on. */
export type PushState = 'unsupported' | 'unconfigured' | 'default' | 'denied' | 'enabled';

const TOKEN_KEY = 'cb-push-token';
const SYNCED_KEY = 'cb-push-synced';
/** The token is re-sent at most once a day (it also refreshes FCM's side), or at once when it changes. */
const RESYNC_MS = 24 * 60 * 60_000;
const SW_URL = '/firebase-messaging-sw.js';
/** A scope of its own, so the worker never controls (or caches) the app's pages. */
const SW_SCOPE = '/firebase-cloud-messaging-push-scope';

let configPromise: Promise<WebPushConfig | null> | null = null;
export function getPushConfig(): Promise<WebPushConfig | null> {
  configPromise ??= api.get<WebPushConfig | null>('/api/notifications/web-push/config').catch(() => null);
  return configPromise;
}

const storage = {
  get: (k: string) => { try { return localStorage.getItem(k); } catch { return null; } },
  set: (k: string, v: string) => { try { localStorage.setItem(k, v); } catch { /* private mode */ } },
  remove: (k: string) => { try { localStorage.removeItem(k); } catch { /* private mode */ } },
};

export function pushSupported(): boolean {
  return typeof window !== 'undefined' && window.isSecureContext && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
}

export async function getPushState(): Promise<PushState> {
  if (!pushSupported()) return 'unsupported';
  if (!(await getPushConfig())) return 'unconfigured';
  if (Notification.permission === 'denied') return 'denied';
  if (Notification.permission === 'granted' && storage.get(TOKEN_KEY)) return 'enabled';
  return 'default';
}

async function firebaseMessaging(config: WebPushConfig) {
  const [{ initializeApp, getApps }, messaging] = await Promise.all([import('firebase/app'), import('firebase/messaging')]);
  if (!(await messaging.isSupported())) return null;
  const app = getApps()[0] ?? initializeApp({
    apiKey: config.apiKey, authDomain: config.authDomain, projectId: config.projectId, messagingSenderId: config.messagingSenderId, appId: config.appId,
  });
  return { messaging, instance: messaging.getMessaging(app) };
}

async function currentToken(config: WebPushConfig): Promise<string | null> {
  const fb = await firebaseMessaging(config);
  if (!fb) return null;
  const registration = await navigator.serviceWorker.register(SW_URL, { scope: SW_SCOPE });
  return fb.messaging.getToken(fb.instance, { vapidKey: config.vapidKey, serviceWorkerRegistration: registration });
}

function describeBrowser() {
  const ua = navigator.userAgent;
  const browser = /Edg\//.test(ua) ? 'Edge' : /OPR\//.test(ua) ? 'Opera' : /Firefox\//.test(ua) ? 'Firefox' : /Chrome\//.test(ua) ? 'Chrome' : /Safari\//.test(ua) ? 'Safari' : 'Other';
  const uaData = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData;
  const platform = uaData?.platform || (/Windows/.test(ua) ? 'Windows' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Mac OS/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : 'Other');
  return { browser, platform };
}

async function register(token: string) {
  await api.post('/api/notifications/devices', { token, deviceType: 'WEB', ...describeBrowser() });
  storage.set(TOKEN_KEY, token);
  storage.set(SYNCED_KEY, String(Date.now()));
}

/** Asks permission (call from a click) and registers this browser. Returns the resulting state. */
export async function enablePush(): Promise<PushState> {
  const config = await getPushConfig();
  if (!pushSupported()) return 'unsupported';
  if (!config) return 'unconfigured';
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') return permission === 'denied' ? 'denied' : 'default';
  const token = await currentToken(config);
  if (!token) return 'unsupported';
  await register(token);
  return 'enabled';
}

/**
 * On each visit while signed in: when permission was already given, re-registers the token if it changed (FCM rotates them) or once a
 * day. Never asks for permission.
 */
export async function syncPush(): Promise<void> {
  if (!pushSupported() || Notification.permission !== 'granted') return;
  const config = await getPushConfig();
  if (!config) return;
  const token = await currentToken(config);
  if (!token) return;
  const synced = Number(storage.get(SYNCED_KEY) ?? 0);
  if (token !== storage.get(TOKEN_KEY) || Date.now() - synced > RESYNC_MS) await register(token);
}

/** Stops push to this browser for the signed-in user (before signing out, or when turned off). */
export async function disablePush(): Promise<void> {
  const token = storage.get(TOKEN_KEY);
  storage.remove(TOKEN_KEY);
  storage.remove(SYNCED_KEY);
  if (!token) return;
  await api.post('/api/notifications/devices/unregister', { token }).catch(() => undefined);
  const config = await getPushConfig();
  if (!config) return;
  const fb = await firebaseMessaging(config).catch(() => null);
  if (fb) await fb.messaging.deleteToken(fb.instance).catch(() => undefined);
}
