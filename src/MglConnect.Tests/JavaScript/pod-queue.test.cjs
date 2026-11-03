const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function setup() {
    const context = { console, Date, AbortController, setTimeout, clearTimeout };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/driver-pod-queue.js'), 'utf8')
        + '\nglobalThis.Queue = MglDriverPodQueue;', context);
    const rows = new Map();
    const store = {
        list: async owner => [...rows.values()].filter(item => item.owner === owner).map(item => structuredClone(item)),
        put: async item => rows.set(item.id, structuredClone(item)),
        remove: async id => rows.delete(id)
    };
    let id = 0;
    const make = (fetch, userId = '1', onChange = () => {}) => new context.Queue({ userId, tenantId: 'TENANT-A' }, { store, fetch, uuid: () => 'item-' + ++id, onChange });
    return { rows, store, make };
}
const pod = { nomeRecebedor: 'Receiver', documentoRecebedor: 'SYNTHETIC', assinaturaBase64: 'data:image/png;base64,SYNTHETIC' };
const auth = (userId = '1') => Response.json({ userId, tenantId: 'tenant-a', role: 'Motorista', token: 'test-only-token' });

test('reload recovers the same persisted signature and duplicate submission does not overwrite it', async () => {
    const fixture = setup();
    const queue = fixture.make(async () => auth());
    const saved = await queue.enqueue(10, pod);
    const reloaded = fixture.make(async () => auth());
    assert.equal((await reloaded.pending())[0].body.assinaturaBase64, pod.assinaturaBase64);
    assert.equal((await reloaded.enqueue(10, pod)).id, saved.id);
    await assert.rejects(reloaded.enqueue(10, { ...pod, nomeRecebedor: 'Different receiver' }));
    assert.equal(fixture.rows.size, 1);
});

for (const failure of ['network', 'unauthorized', 'conflict', 'server', 'invalid-receipt']) {
    test(`pending POD survives ${failure} without a false acknowledgement`, async () => {
        const fixture = setup();
        const queue = fixture.make(async url => {
            if (url.includes('Contexto')) return auth();
            if (failure === 'network') throw new TypeError('lost connection');
            if (failure === 'invalid-receipt') return Response.json({ sucesso: true, entregaId: 999, status: 'Entregue' });
            return Response.json({}, { status: failure === 'unauthorized' ? 401 : failure === 'conflict' ? 409 : 500 });
        });
        await queue.enqueue(10, pod);
        await queue.sync();
        assert.equal(fixture.rows.size, 1);
    });
}

test('a different logged-in user never sends the previous user signature', async () => {
    const fixture = setup();
    let posts = 0;
    const queue = fixture.make(async url => { if (url.includes('Contexto')) return auth('2'); posts++; return Response.json({}); });
    await queue.enqueue(10, pod);
    await queue.sync();
    assert.equal(posts, 0);
    assert.equal(fixture.rows.size, 1);
    assert.equal((await fixture.make(async () => auth('2'), '2').pending()).length, 0);
});

test('lost response after commit is retried with identical content and only the matching receipt removes it', async () => {
    const fixture = setup();
    const sent = [];
    const queue = fixture.make(async (url, options) => {
        if (url.includes('Contexto')) return auth();
        sent.push(options.body);
        if (sent.length === 1) throw new TypeError('response lost after server commit');
        return Response.json({ sucesso: true, entregaId: 10, status: 'Entregue' });
    });
    await queue.enqueue(10, pod);
    await queue.sync();
    assert.equal(fixture.rows.size, 1);
    await queue.sync();
    assert.equal(sent.length, 2);
    assert.equal(sent[0], sent[1]);
    assert.equal(fixture.rows.size, 0);
});

test('storage failure never reports a locally saved POD', async () => {
    const fixture = setup();
    fixture.store.put = async () => { throw new Error('quota'); };
    let saved = false;
    const queue = fixture.make(async () => auth(), '1', () => { saved = true; });
    await assert.rejects(queue.enqueue(10, pod));
    assert.equal(saved, false);
    assert.equal(fixture.rows.size, 0);
});
