// M.G.L Connect — Service Worker PWA (Offline-First)
const CACHE_NAME = 'mgl-connect-v1';
const STATIC_ASSETS = [
    '/',
    '/css/site.css',
    '/js/site.js',
    '/manifest.json',
    'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css',
    'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css',
    'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS.filter(url => url.startsWith('/'))))
    );
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

self.addEventListener('fetch', (event) => {
    // Network-first para APIs, Cache-first para estáticos
    if (event.request.url.includes('/api/') || event.request.url.includes('/trackingHub') || event.request.url.includes('/telemetryHub')) {
        event.respondWith(
            fetch(event.request).catch(() =>
                new Response(JSON.stringify({ erro: 'Sem conexão. Dados serão sincronizados em breve.' }),
                    { headers: { 'Content-Type': 'application/json' } })
            )
        );
    } else {
        event.respondWith(
            caches.match(event.request).then(cached => cached || fetch(event.request))
        );
    }
});

// Background Sync — Sincroniza entregas e PODs offline
self.addEventListener('sync', (event) => {
    if (event.tag === 'sync-entregas') {
        event.waitUntil(syncEntregasOffline());
    }
});

async function syncEntregasOffline() {
    const db = await openIndexedDB();
    const tx = db.transaction('pendingSync', 'readwrite');
    const store = tx.objectStore('pendingSync');
    const items = await new Promise((resolve, reject) => {
        const req = store.getAll();
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });

    for (const item of items) {
        try {
            await fetch(item.url, {
                method: item.method,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(item.data)
            });
            store.delete(item.id);
        } catch (e) {
            console.warn('[SW] Sync falhou para item', item.id, e);
        }
    }
}

function openIndexedDB() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open('mgl-offline-db', 1);
        req.onupgradeneeded = () => {
            req.result.createObjectStore('pendingSync', { keyPath: 'id', autoIncrement: true });
            req.result.createObjectStore('gpsQueue', { keyPath: 'id', autoIncrement: true });
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}
