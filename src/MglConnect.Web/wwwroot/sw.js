// Only public, application-owned static resources may be cached.
const CACHE_NAME = 'mgl-static-v4';
const STATIC_ASSETS = ['/css/site.css', '/js/site.js', '/manifest.json', '/offline.html', '/js/driver-gps-queue.js', '/js/gps-offline-recovery.js'];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS)));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(Promise.all([
        // The old root-page cache could contain authenticated HTML. Never read it.
        caches.delete('mgl-connect-v1'),
        caches.delete('mgl-static-v2'),
        caches.delete('mgl-static-v3'),
        self.clients.claim()
    ]));
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    if (url.origin !== self.location.origin) return;
    event.respondWith(handleRequest(event.request, url));
});

async function handleRequest(request, url) {
    const isStatic = request.method === 'GET' && STATIC_ASSETS.includes(url.pathname);
    if (isStatic) {
        const cache = await caches.open(CACHE_NAME);
        const cached = await cache.match(url.pathname);
        if (cached) return cached;
        try {
            const response = await fetch(request);
            if (response.ok && !response.redirected) await cache.put(url.pathname, response.clone());
            return response;
        } catch { return unavailable(); }
    }
    // Mutations, APIs and authenticated HTML always require a network response.
    try { return await fetch(request); }
    catch {
        if (request.mode === 'navigate') {
            const cache = await caches.open(CACHE_NAME);
            const page = await cache.match('/offline.html');
            if (page) return new Response(await page.text(), {
                status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }
            });
        }
        return unavailable();
    }
}

function unavailable() {
    return new Response(JSON.stringify({ sucesso: false, erro: 'Sem conexão. A operação não foi confirmada pelo servidor.' }), {
        status: 503, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }
    });
}

self.addEventListener('sync', event => {
    if (event.tag === 'sync-entregas' || event.tag === 'sync-gps') {
        // Legacy queues have no reliable actor/organization scope. Preserve them;
        // an authenticated page must validate ownership before any future replay.
        event.waitUntil(self.clients.matchAll({ type: 'window' }).then(clients => {
            for (const client of clients) client.postMessage({ type: 'MGL_SYNC_REQUIRED' });
        }));
    }
});
