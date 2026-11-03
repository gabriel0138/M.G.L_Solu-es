const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function setup(fetch, fragment = undefined) {
    const listeners = {};
    const panel = { dataset: { refreshUrl: '/Cliente/Painel', fragmentId: fragment === undefined ? undefined : 'adminDeliveryDetails' }, children: ['old'],
        addEventListener(name, handler) { listeners[name] = handler; }, replaceChildren(...items) { this.children = items; } };
    const status = { textContent: '' };
    const document = { hidden: false, getElementById: id => id === 'operationalPanel' ? panel : status, addEventListener() {} };
    let tick;
    const context = { document, navigator: { onLine: true }, window: { addEventListener() {} },
        location: { origin: 'https://mgl.test', search: '?de=2026-10-01&ate=2026-10-28&ignored=private' },
        URL, URLSearchParams, AbortController, fetch,
        DOMParser: class { parseFromString(html) { return { body: { childNodes: [html] }, getElementById: () => fragment }; } },
        setTimeout: () => 1, clearTimeout() {}, setInterval: callback => { tick = callback; } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/operational-dashboard.js'), 'utf8'), context);
    return { panel, status, document, tick: () => tick(), listeners };
}

test('visible dashboard refreshes the authorized fragment with only its period parameters', async () => {
    let request;
    const page = setup(async (url, options) => { request = { url, options }; return new Response('<div>authorized updates</div>'); });
    await page.tick();
    assert.equal(request.url.origin, 'https://mgl.test');
    assert.equal(request.url.searchParams.get('de'), '2026-10-01');
    assert.equal(request.url.searchParams.has('ignored'), false);
    assert.equal(request.options.credentials, 'same-origin');
    assert.equal(request.options.cache, 'no-store');
    assert.equal(page.panel.children[0], '<div>authorized updates</div>');
});

test('a real operation notification refreshes only the authenticated detail fragment', async () => {
    const fragment = { id: 'adminDeliveryDetails', status: 'Entregue' };
    const page = setup(async () => new Response('<html>layout and private operation</html>'), fragment);
    await page.listeners['mgl:operation-changed']();
    assert.equal(page.panel.children[0], fragment);
    assert.equal(page.panel.children.length, 1);
});

test('a missing authenticated detail fragment clears data instead of inserting a login or error document', async () => {
    let calls = 0;
    const page = setup(async () => { calls++; return new Response('<html>Login</html>'); }, null);
    await page.tick();
    await page.tick();
    assert.equal(page.panel.children.length, 0);
    assert.equal(calls, 1);
    assert.match(page.status.textContent, /acesso.*não pôde ser confirmado/);
});

test('hidden pages do not poll and slow responses do not overlap', async () => {
    let calls = 0;
    let release;
    const page = setup(() => { calls++; return new Promise(resolve => { release = resolve; }); });
    page.document.hidden = true;
    await page.tick();
    assert.equal(calls, 0);
    page.document.hidden = false;
    const first = page.tick();
    await page.tick();
    assert.equal(calls, 1);
    release(new Response('updated'));
    await first;
});

test('loss of access clears private content and stops further polling', async () => {
    let calls = 0;
    const page = setup(async () => { calls++; return new Response('', { status: 403 }); });
    await page.tick();
    await page.tick();
    assert.equal(page.panel.children.length, 0);
    assert.equal(calls, 1);
    assert.match(page.status.textContent, /sessão perdeu acesso/);
});

test('network failure preserves the last content and labels it as stale', async () => {
    const page = setup(async () => { throw new TypeError('offline'); });
    await page.tick();
    assert.equal(page.panel.children[0], 'old');
    assert.match(page.status.textContent, /última consulta/);
});

test('refresh does not remove an action that currently has keyboard focus', async () => {
    const page = setup(async () => new Response('new content'));
    page.panel.contains = () => true;
    await page.tick();
    assert.equal(page.panel.children[0], 'old');
    assert.match(page.status.textContent, /seleção atual/);
});
