/* LmimaShop V2 mobile — service worker.
 * Bump CACHE_VERSION with every release. API traffic is always network-only.
 */

const CACHE_VERSION = '2.0.1';
const CACHE_NAME = 'lmima-v2-shell-' + CACHE_VERSION;

const PRECACHE = [
  './',
  './index.html',
  './manifest.json',
  './css/styles.css',
  './js/main.js',
  './js/config.js',
  './js/api.js',
  './js/store.js',
  './js/db.js',
  './js/sync.js',
  './js/pos.js',
  './js/products.js',
  './js/customers.js',
  './js/reports.js',
  './js/ui.js',
  './js/scanner.js',
  './js/payment.js',
  './js/qr-pair.js',
  './vendor/html5-qrcode.min.js',
  './icon-192.png',
  './icon-512.png',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(PRECACHE))
      .then(() => self.skipWaiting())
      .catch((err) => {
        console.error('Service worker precache failed:', err);
        throw err;
      })
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.map((key) => {
        if (key.startsWith('lmima-') && key !== CACHE_NAME) {
          return caches.delete(key);
        }
        return Promise.resolve(false);
      })))
      .then(() => self.clients.claim())
      .catch((err) => console.error('Service worker activation failed:', err))
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;
  let url;
  try {
    url = new URL(request.url);
  } catch (err) {
    console.error('Service worker received invalid URL:', err);
    return;
  }
  if (url.origin !== self.location.origin) return;
  // Never cache API responses: the app handles offline state and its own queue.
  if (url.pathname.includes('/api/')) return;
  event.respondWith(handleAssetRequest(event));
});

async function handleAssetRequest(event) {
  const request = event.request;
  const cache = await caches.open(CACHE_NAME);
  const cached = await cache.match(request);
  const networkPromise = fetch(request)
    .then((response) => {
      if (response && response.ok) cache.put(request, response.clone()).catch((err) => {
        console.warn('Service worker cache put failed:', err);
      });
      return response;
    })
    .catch((err) => {
      console.warn('Service worker network request failed:', request.url, err);
      return null;
    });

  if (cached) {
    event.waitUntil(networkPromise.then(() => undefined).catch(() => undefined));
    return cached;
  }

  const fresh = await networkPromise;
  if (fresh) return fresh;

  if (request.mode === 'navigate') {
    const shell = await cache.match('./index.html') || await cache.match('./');
    if (shell) return shell;
  }

  return new Response('أنت غير متصل وهذه الصفحة غير مخزنة مؤقتاً.', {
    status: 503,
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
  });
}
