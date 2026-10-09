/*
 * Calling Bell web push service worker.
 *
 * The server sends Firebase Cloud Messaging *data* messages, so this worker decides how they appear: it shows a notification while the
 * site isn't in front (an open, focused tab already gets the same update live), opens the right page when it is clicked, and tells the
 * server the push arrived ("received") or was opened ("clicked") - FCM has no delivery receipts for the web.
 * No Firebase code is needed here; credentials never reach the browser.
 */
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

function acknowledge(data, kind) {
  if (!data || !data.deliveryId || !data.ack) return Promise.resolve();
  return fetch('/api/notifications/deliveries/' + encodeURIComponent(data.deliveryId) + '/ack', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ signature: data.ack, event: kind }),
    keepalive: true,
  }).catch(() => undefined);
}

self.addEventListener('push', (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch (e) {
    return;
  }
  // FCM puts a data message's fields under "data".
  const data = payload.data || payload;
  if (!data || !data.title) return;

  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const inFront = windows.find((w) => w.visibilityState === 'visible' && w.focused);
    if (inFront) {
      inFront.postMessage({ type: 'cb-push', data });
    } else {
      await self.registration.showNotification(data.title, {
        body: data.body || '',
        icon: data.icon || '/icons/notification-192.png',
        badge: data.badge || data.icon || '/icons/notification-192.png',
        tag: data.tag || data.deliveryId,
        renotify: true,
        data: { url: data.url || '/', deliveryId: data.deliveryId, ack: data.ack },
      });
    }
    await acknowledge(data, 'received');
  })());
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const data = event.notification.data || {};
  const url = new URL(data.url || '/', self.location.origin);

  event.waitUntil((async () => {
    await acknowledge(data, 'clicked');
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const open = windows.find((w) => new URL(w.url).origin === self.location.origin);
    if (open) {
      // This worker doesn't control the app's pages (its scope is separate), so the page navigates itself.
      open.postMessage({ type: 'cb-navigate', url: url.pathname + url.search + url.hash });
      await open.focus();
      return;
    }
    await self.clients.openWindow(url.href);
  })());
});
