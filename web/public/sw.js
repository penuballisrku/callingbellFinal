/*
 * CallInBell app service worker (makes the site installable: "Add to Home Screen").
 *
 * It deliberately caches nothing the visitor reads - no pages, no API data - so everything shown is always current. It only keeps a
 * small offline page, shown when a page is opened without a connection. Web push runs in its own worker
 * (firebase-messaging-sw.js, separate scope), so the two never interfere.
 */
const OFFLINE_CACHE = 'cb-offline-v2'; // v2: CallInBell rebrand (new offline page text and icon)
const OFFLINE_URL = '/offline.html';

self.addEventListener('install', (event) => {
  // The offline page is a nicety: if it can't be stored (storage full or blocked), the worker still installs.
  event.waitUntil(caches.open(OFFLINE_CACHE).then((cache) => cache.addAll([OFFLINE_URL, '/icons/app-192.png'])).catch(() => undefined)
    .then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.filter((k) => k.startsWith('cb-offline-') && k !== OFFLINE_CACHE).map((k) => caches.delete(k)));
    await self.clients.claim();
  })());
});

// Page loads go to the network as usual; only when that fails (offline) is the offline page shown. Everything else is untouched.
self.addEventListener('fetch', (event) => {
  if (event.request.mode !== 'navigate') return;
  event.respondWith(fetch(event.request).catch(async () => (await caches.match(OFFLINE_URL).catch(() => undefined)) || Response.error()));
});
