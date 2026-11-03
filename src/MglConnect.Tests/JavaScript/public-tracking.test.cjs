const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class Element {
    constructor(tagName = 'div') {
        this.tagName = tagName;
        this.children = [];
        this.dataset = {};
        this.attributes = {};
        this.className = '';
        this.listeners = {};
        this.hidden = false;
        this.classList = {
            add: value => { this.className = [...new Set([...this.className.split(' '), value])].join(' '); },
            remove: value => { this.className = this.className.split(' ').filter(item => item !== value).join(' '); }
        };
    }
    set innerHTML(_) { throw new Error('Public event content must not be inserted as HTML.'); }
    set textContent(value) { this.text = String(value); this.children = []; }
    get textContent() { return (this.text || '') + this.children.map(child => child.textContent).join(''); }
    setAttribute(name, value) { this.attributes[name] = value; }
    removeAttribute(name) { delete this.attributes[name]; }
    append(...children) { for (const child of children) { child.parent = this; this.children.push(child); } }
    insertBefore(child, before) { child.parent = this; this.children.splice(this.children.indexOf(before), 0, child); }
    remove() { if (this.parent) this.parent.children = this.parent.children.filter(child => child !== this); }
    addEventListener(name, callback) { this.listeners[name] = callback; }
    querySelectorAll(selector) {
        const matches = element => selector.startsWith('.') ? element.className.split(' ').includes(selector.slice(1))
            : selector === '[data-event-time]' ? Object.hasOwn(element.dataset, 'eventTime')
            : selector === '[data-stage]' ? Object.hasOwn(element.dataset, 'stage') : element.tagName === selector;
        const result = [];
        const visit = element => { for (const child of element.children) { if (matches(child)) result.push(child); visit(child); } };
        visit(this);
        return result;
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
}

function environment({ initialEvents = [], missingClient = false } = {}) {
    const root = new Element('main');
    const elements = {};
    for (const id of ['copyTrackingCode', 'trackingConnectionStatus', 'publicDeliveryStatus', 'publicTrackingHeadline',
        'publicTrackingDetail', 'publicTrackingStages', 'publicTrackingStagesNote', 'publicTimeline']) {
        const element = new Element();
        elements[id] = element;
        root.append(element);
    }
    elements.publicDeliveryStatus.textContent = 'Pendente';
    elements.publicTrackingHeadline.textContent = 'Preparando sua entrega';
    for (const index of [1, 2, 3]) {
        const item = new Element('li');
        item.dataset.stage = String(index);
        elements.publicTrackingStages.append(item);
    }
    for (const event of initialEvents) {
        const element = new Element('article');
        element.className = 'timeline-event cc-tracking-event latest';
        element.dataset.eventTime = event.dataHora;
        element.dataset.eventStatus = event.status;
        elements.publicTimeline.append(element);
    }
    if (!initialEvents.length) {
        elements.publicTimelineEmpty = new Element('p');
        elements.publicTimeline.append(elements.publicTimelineEmpty);
    }
    const attached = element => element === root || !!element.parent && attached(element.parent)
        && element.parent.children.includes(element);
    const handlers = {};
    const calls = [];
    const requests = [];
    let fetchHandler = async () => ({ ok: true, json: async () => ({ codigoRastreio: 'MGL-TEST', status: 'Pendente', eventos: [] }) });
    let joinFails = false;
    const connection = {
        on(name, callback) { handlers[name] = callback; },
        onreconnecting(callback) { handlers.reconnecting = callback; },
        onclose(callback) { handlers.closed = callback; },
        onreconnected(callback) { handlers.reconnected = callback; },
        start: () => Promise.resolve(),
        invoke(...args) { calls.push(args); return joinFails ? Promise.reject(new Error('Unavailable')) : Promise.resolve(); }
    };
    const context = {
        Date, console,
        document: {
            getElementById: id => elements[id] && attached(elements[id]) ? elements[id] : null,
            createElement: tag => new Element(tag)
        },
        copiarCodigoRastreio() {},
        fetch: async (...args) => { requests.push(args); return fetchHandler(...args); }
    };
    if (!missingClient) context.signalR = {
        HubConnectionBuilder: class {
            withUrl(url) { assert.equal(url, '/trackingHub'); return this; }
            withAutomaticReconnect() { return this; }
            build() { return connection; }
        }
    };
    const source = fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/Views/Rastreio/Index.cshtml'), 'utf8');
    const script = source.match(/<script>([\s\S]*?)<\/script>/)[1]
        .replace('@Json.Serialize(Model.Entrega.CodigoRastreio)', '"MGL-TEST"');
    vm.runInNewContext(script, context);
    return {
        elements, handlers, calls, requests,
        setFetch(handler) { fetchHandler = handler; },
        rejectJoin(value) { joinFails = value; },
        events: () => elements.publicTimeline.querySelectorAll('[data-event-time]'),
        payload(overrides = {}) { return { codigoRastreio: 'MGL-TEST', status: 'Em transporte',
            dataHora: '2026-10-29T12:00:00.123Z', titulo: 'Em transporte',
            descricao: 'A encomenda está em transporte.', localidade: 'Atualização operacional', icone: 'bi-truck', ...overrides }; }
    };
}

const flush = () => new Promise(resolve => setImmediate(resolve));

test('the production public hub DTO updates status, stages and the safe timeline', async () => {
    const env = environment();
    await flush();
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'connected');
    assert.equal(env.requests.length, 0, 'The initial HTML does not need a redundant snapshot request.');
    env.handlers.ReceiveCheckpointUpdate(env.payload());
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Em transporte');
    assert.equal(env.elements.publicTrackingHeadline.textContent, 'A caminho do destino');
    assert.equal(env.elements.publicTrackingStages.children[1].dataset.stageState, 'current');
    assert.equal(env.events().length, 1);
    assert.equal(env.events()[0].dataset.eventStatus, 'Em transporte');
    assert.equal(env.events()[0].querySelector('time').dateTime, '2026-10-29T12:00:00.123Z');
    assert.match(env.events()[0].querySelector('time').textContent, /UTC$/);
});

test('the public handler rejects unrelated or missing tracking codes and invalid capture dates', () => {
    const env = environment();
    for (const overrides of [{ codigoRastreio: 'MGL-OTHER' }, { codigoRastreio: null }, { codigoRastreio: undefined },
        { dataHora: 'invalid-date' }, { dataHora: null }, { dataHora: 123 }]) {
        env.handlers.ReceiveCheckpointUpdate(env.payload(overrides));
    }
    env.handlers.ReceiveCheckpointUpdate(null);
    assert.equal(env.events().length, 0);
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Pendente');
    env.handlers.ReceiveCheckpointUpdate(env.payload({ codigoRastreio: ' mgl-test ' }));
    assert.equal(env.events().length, 1);
});

test('SQLite UTC without a suffix deduplicates the same hub event with a Z suffix', () => {
    const env = environment({ initialEvents: [{ dataHora: '2026-10-29T12:00:00.1230000', status: 'Em transporte' }] });
    env.handlers.ReceiveCheckpointUpdate(env.payload());
    assert.equal(env.events().length, 1);
    env.handlers.ReceiveCheckpointUpdate(env.payload({ dataHora: '2026-10-29T15:00:00.123+03:00' }));
    assert.equal(env.events().length, 1);
});

test('older or duplicate public events do not reverse the current headline or change ordering', () => {
    const env = environment();
    env.handlers.ReceiveCheckpointUpdate(env.payload({ status: 'Entregue', dataHora: '2026-10-29T13:00:00Z' }));
    env.handlers.ReceiveCheckpointUpdate(env.payload({ status: 'Pendente', dataHora: '2026-10-29T11:00:00Z' }));
    env.handlers.ReceiveCheckpointUpdate(env.payload({ status: 'Entregue', dataHora: '2026-10-29T13:00:00Z' }));
    assert.equal(env.events().length, 2);
    assert.equal(env.events()[0].dataset.eventStatus, 'Entregue');
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Entregue');
    assert.equal(env.elements.publicTrackingHeadline.textContent, 'Entrega concluída');
});

test('unknown states stay unknown and untrusted event text is never inserted as HTML', () => {
    const env = environment();
    const unsafe = '<img src=x onerror=alert(1)>';
    env.handlers.ReceiveCheckpointUpdate(env.payload({ status: unsafe, titulo: unsafe, descricao: unsafe, localidade: unsafe, icone: unsafe }));
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Em atualização');
    assert.equal(env.elements.publicTrackingStages.hidden, true);
    assert.equal(env.events()[0].querySelector('h3').textContent, unsafe);
    assert.equal(env.events()[0].querySelector('p').textContent, unsafe);
    assert.equal(env.events()[0].querySelector('i').className, 'bi bi-info-circle');
});

test('reconnection joins once then recovers missed events and authoritative status without polling', async () => {
    const env = environment();
    await flush();
    env.handlers.reconnecting();
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'reconnecting');
    env.setFetch(async () => ({ ok: true, json: async () => ({ codigoRastreio: 'MGL-TEST', status: 'Entregue', eventos: [
        env.payload({ status: 'Pendente', dataHora: '2026-10-29T11:00:00.0000000' }),
        env.payload({ status: 'Entregue', dataHora: '2026-10-29T13:00:00.0000000' })
    ] }) }));
    await env.handlers.reconnected();
    assert.equal(env.requests.length, 1);
    assert.equal(env.requests[0][0], '/api/rastreio/MGL-TEST');
    assert.equal(env.requests[0][1].cache, 'no-store');
    assert.equal(env.calls.filter(call => call[0] === 'JoinTrackingGroup').length, 2);
    assert.equal(env.events().length, 2);
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Entregue');
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'connected');
    env.handlers.ReceiveCheckpointUpdate(env.payload({ status: 'Entregue', dataHora: '2026-10-29T13:00:00Z' }));
    assert.equal(env.events().length, 2);
    env.handlers.closed();
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'closed');
    assert.equal(env.events().length, 2);
});

test('failed subscription, rate limiting or an unrelated recovery snapshot do not claim recovered history', async () => {
    const env = environment();
    await flush();
    env.rejectJoin(true);
    await env.handlers.reconnected();
    assert.equal(env.requests.length, 0);
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'unavailable');
    env.rejectJoin(false);
    env.setFetch(async () => ({ ok: false, status: 429 }));
    await env.handlers.reconnected();
    assert.equal(env.requests.length, 1);
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'unavailable');
    env.setFetch(async () => ({ ok: true, json: async () => ({ codigoRastreio: 'MGL-OTHER', status: 'Entregue', eventos: [env.payload()] }) }));
    await env.handlers.reconnected();
    assert.equal(env.events().length, 0);
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Pendente');
    assert.equal(env.requests.length, 2, 'No automatic retries consume the shared public query budget.');
});

test('a late recovery snapshot cannot reverse an update that arrived while its request was pending', async () => {
    const env = environment();
    await flush();
    let finish;
    env.setFetch(() => new Promise(resolve => { finish = resolve; }));
    const reconnecting = env.handlers.reconnected();
    await flush();
    env.handlers.ReceiveCheckpointUpdate(env.payload({ status: 'Entregue', dataHora: '2026-10-29T13:00:00Z' }));
    finish({ ok: true, json: async () => ({ codigoRastreio: 'MGL-TEST', status: 'Pendente', eventos: [] }) });
    await reconnecting;
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Entregue');
    assert.equal(env.events().length, 1);
});

test('missing SignalR is shown honestly without changing the server-rendered delivery state', () => {
    const env = environment({ missingClient: true });
    assert.equal(env.elements.trackingConnectionStatus.dataset.state, 'unavailable');
    assert.equal(env.elements.publicDeliveryStatus.textContent, 'Pendente');
    assert.equal(env.requests.length, 0);
});
