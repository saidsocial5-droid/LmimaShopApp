const CACHE_NAME = 'lmima-mobile-v3.4.0';
const ASSETS_TO_CACHE = [
  './',
  './index.html',
  './styles.css',
  './fonts/Cairo.ttf',
  './db.js',
  './sync_v2.js',
  './signalr.min.js',
  './app.js',
  './html5-qrcode.min.js',
  './manifest.json',
  './icon-192.png',
  './icon-512.png',
  './logo.png',
  './lmima_mobile_sync.js',
  './cash/cash_200.jpg',
  './cash/cash_100.jpg',
  './cash/cash_50.jpg',
  './cash/cash_20.jpg',
  './cash/cash_10.jpg',
  './cash/cash_5.jpg',
  './cash/cash_2.jpg',
  './cash/cash_1.jpg',
  './cash/cash_05.jpg',
  './cash/cash_exact.jpg'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log('[ServiceWorker] Pre-caching offline assets');
      return cache.addAll(ASSETS_TO_CACHE).catch(err => {
        console.warn('[ServiceWorker] Some assets failed to pre-cache:', err);
      });
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keyList) => {
      return Promise.all(keyList.map((key) => {
        if (key !== CACHE_NAME) {
          console.log('[ServiceWorker] Removing old cache', key);
          return caches.delete(key);
        }
      }));
    })
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;

  event.respondWith(
    caches.match(event.request).then((cachedResponse) => {
      const fetchPromise = fetch(event.request).then((networkResponse) => {
        if (networkResponse && networkResponse.status === 200) {
          const responseClone = networkResponse.clone();
          caches.open(CACHE_NAME).then((cache) => {
            cache.put(event.request, responseClone);
          });
        }
        return networkResponse;
      }).catch(() => {
        return cachedResponse;
      });

      return cachedResponse || fetchPromise;
    })
  );
});
