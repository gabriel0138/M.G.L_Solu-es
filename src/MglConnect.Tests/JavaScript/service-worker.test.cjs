const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function worker(fetchImpl) {
    const handlers = {};
    const removed = [];
    const stored = new Map();
    const context = {
        URL, Request, Response, console,
        fetch: fetchImpl,
        self: {
            location: { origin: 'https://mgl.test' },
            addEventListener: (name, handler) => { handlers[name] = handler; },
            skipWaiting: async () => {},
            clients: { claim: async () => {}, matchAll: async () => [] }
        },
        caches: {
            keys: async () => ['another-app-cache', 'mgl-connect-v1'],
            delete: async key => { removed.push(key); return true; },
            match: async () => new Response('PRIVATE CACHED DASHBOARD'),
            open: async () => ({
                match: async () => new Response('Public offline page'),
                put: async (key, response) => stored.set(key, response),
                addAll: async () => {}
            })
        }
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/sw.js'), 'utf8'), context);
    async function request(url, method = 'GET', mode = 'cors') {
        let response;
        const work = [];
        handlers.fetch({ request: { url: `https://mgl.test${url}`, method, mode },
            respondWith: promise => { response = Promise.resolve(promise); },
            waitUntil: promise => work.push(promise) });
        const result = response ? await response : await fetchImpl({ url, method });
        await Promise.all(work);
        return result;
    }
    return { handlers, removed, stored, request };
}

test('offline mutations return a failure, never an HTTP 200 acknowledgement', async () => {
    const sw = worker(async () => { throw new TypeError('offline'); });
    const response = await sw.request('/api/entregas/1/pod', 'POST');
    assert.equal(response.status, 503);
    assert.equal(response.ok, false);
});

test('private navigation never falls back to cached authenticated HTML', async () => {
    const sw = worker(async () => { throw new TypeError('offline'); });
    const response = await sw.request('/Cliente/Pedido/1', 'GET', 'navigate');
    assert.equal(response.status, 503);
    assert.doesNotMatch(await response.text(), /PRIVATE/);
});

test('API authorization failures are preserved and never cached', async () => {
    const sw = worker(async () => new Response('unauthorized', { status: 401 }));
    const response = await sw.request('/api/entregas/1');
    assert.equal(response.status, 401);
    assert.equal(sw.stored.size, 0);
});

test('activation invalidates only this application cache', async () => {
    const sw = worker(async () => new Response('ok'));
    const work = [];
    sw.handlers.activate({ waitUntil: task => work.push(task) });
    await Promise.all(work);
    assert.ok(sw.removed.includes('mgl-connect-v1'));
    assert.ok(!sw.removed.includes('another-app-cache'));
});

test('legacy background sync never replays unscoped operations', async () => {
    let requests = 0;
    const sw = worker(async () => { requests++; return new Response('ok'); });
    const work = [];
    sw.handlers.sync({ tag: 'sync-entregas', waitUntil: task => work.push(task) });
    await Promise.all(work);
    assert.equal(requests, 0);
});
