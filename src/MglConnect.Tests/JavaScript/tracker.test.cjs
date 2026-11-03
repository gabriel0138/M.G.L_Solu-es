const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function trackerType() {
    const context = { console, Date, Number, AbortController, setTimeout, clearTimeout, setInterval, clearInterval, navigator: { onLine: true } };
    const source = fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/tracker.js'), 'utf8');
    vm.runInNewContext(source + '\nglobalThis.Tracker = MglCockpitTracker;', context);
    return context.Tracker;
}

function telemetryEnvironment({ live = false, joinFails = false, trackerOptions = {} } = {}) {
    let now = Date.parse('2026-10-29T12:00:00.000Z');
    class ClockDate extends Date {
        constructor(...args) { super(...(args.length ? args : [now])); }
        static now() { return now; }
    }
    const elements = new Map();
    for (const id of ['positionSourceBadge', 'telemetryConnectionState', 'speedValue', 'etaValue', 'etaMinutes', 'tripProgressBar']) {
        const attributes = {};
        elements.set(id, { textContent: '', dataset: {}, style: {}, attributes,
            setAttribute(name, value) { attributes[name] = value; }, classList: { toggle() {} } });
    }
    const events = new Map();
    const intervals = new Map();
    const handlers = {};
    const invocations = [];
    const fetchCalls = [];
    const mapMoves = [];
    const removedMapListeners = [];
    const toasts = [];
    let nextTimer = 1;
    let stopped = 0;
    const hub = {
        state: 'Connected',
        on(name, callback) { handlers[name] = callback; },
        onreconnecting(callback) { handlers.reconnecting = callback; },
        onreconnected(callback) { handlers.reconnected = callback; },
        onclose(callback) { handlers.closed = callback; },
        start: () => Promise.resolve(),
        invoke(...args) {
            invocations.push(args);
            return joinFails && args[0] === 'JoinLiveTracking' ? Promise.reject(new Error('Access denied')) : Promise.resolve();
        },
        stop() { stopped++; return Promise.resolve(); }
    };
    const latLng = (latitude, longitude) => {
        const lat = Array.isArray(latitude) ? latitude[0] : latitude;
        const lng = Array.isArray(latitude) ? latitude[1] : longitude;
        return { lat, lng, distanceTo: () => 0 };
    };
    const mapContainer = { dataset: { mapStatus: 'awaiting' } };
    const map = {
        setView(point, zoom) { mapMoves.push({ point, zoom }); },
        panTo(point) { mapMoves.push({ point }); },
        getContainer: () => mapContainer,
        on() {},
        off(...args) { removedMapListeners.push(args); }
    };
    const context = {
        console: { log() {}, warn() {}, error() {} },
        Date: ClockDate, Number, AbortController,
        document: {
            getElementById: id => elements.get(id) || null,
            createElement(tag) { return { tag, style: {}, children: [], attributes: {},
                setAttribute(name,value) { this.attributes[name] = value; },
                append(...children) { this.children.push(...children); }, remove() {} }; },
            body: { appendChild(node) { toasts.push(node); } }
        },
        navigator: { onLine: true },
        window: {
            addEventListener(name, handler) { events.set(name, handler); },
            removeEventListener(name, handler) { if (events.get(name) === handler) events.delete(name); }
        },
        L: {
            latLng,
            divIcon: value => value,
            marker: () => ({ addTo() { return this; }, setLatLng() {}, setZIndexOffset() {} })
        },
        setInterval(callback, duration) { const id = nextTimer++; intervals.set(id, { callback, duration }); return id; },
        clearInterval(id) { intervals.delete(id); },
        setTimeout() { return nextTimer++; }, clearTimeout() {},
        requestAnimationFrame() { return nextTimer++; }, cancelAnimationFrame() {},
        performance: { now: () => now }
    };
    context.fetch = async url => { fetchCalls.push(url); return { ok: false }; };
    if (live) {
        context.signalR = {
            HubConnectionState: { Connected: 'Connected' },
            HubConnectionBuilder: class {
                withUrl() { return this; }
                withAutomaticReconnect() { return this; }
                build() { return hub; }
            }
        };
    }
    const source = fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/tracker.js'), 'utf8');
    vm.runInNewContext(source + '\nglobalThis.Tracker = MglCockpitTracker;', context);
    const tracker = new context.Tracker({ map, entregaId: 1, codigoRastreio: 'MGL-TEST', coordinates: [],
        tempoBaseMinutos: 30, ...trackerOptions });
    return {
        tracker, elements, events, intervals, handlers, invocations, fetchCalls, mapMoves, mapContainer, removedMapListeners, toasts,
        stopped: () => stopped,
        now: () => now,
        advance(milliseconds) { now += milliseconds; },
        payload(overrides = {}) {
            return { latitude: -23.5, longitude: -46.6, accuracyMeters: 10, speedKmH: 30,
                heading: 90, isSimulated: false, timestamp: new Date(now).toISOString(), ...overrides };
        }
    };
}

test('drawing a planned route does not place a vehicle at its origin', () => {
    const tracker = Object.create(trackerType().prototype);
    let markers = 0;
    for (const name of ['setupRouteLayers', 'setupSignalR', 'setupTrafficLayerMatch', 'setupLayerListeners']) tracker[name] = () => {};
    tracker.setupVehicleMarker = () => markers++;
    tracker.initialCoordinates = [[-23, -46], [-24, -47]];
    tracker.init();
    assert.equal(markers, 0);
});

test('an old offline position is not replayed as current GPS or silently discarded', async () => {
    const tracker = Object.create(trackerType().prototype);
    tracker.gpsOfflineQueue = [{ lat: -23, lng: -46, timestamp: Date.now() - 600000 }];
    tracker.isOnline = true;
    let emitted = 0;
    tracker.emitTelemetry = () => { emitted++; };
    tracker.showToastAlert = () => {};
    await tracker.syncOfflineGPS();
    assert.equal(emitted, 0);
    assert.equal(tracker.gpsOfflineQueue.length, 1);
});

test('invalid or missing coordinates never move the map', () => {
    const tracker = Object.create(trackerType().prototype);
    assert.equal(tracker.updateVehiclePosition(NaN, -46, 10, 0), false);
    assert.equal(tracker.updateVehiclePosition(-23, 181, 10, 0), false);
});

test('a persisted snapshot retains its capture age after reload and centers only on its coordinates', () => {
    const first = telemetryEnvironment();
    const captured = first.payload({ timestamp: new Date(first.now() - 180000).toISOString() });
    assert.equal(first.tracker.applyTelemetryPosition(captured, { snap: true }), true);
    assert.equal(first.elements.get('positionSourceBadge').dataset.state, 'stale');
    assert.match(first.elements.get('positionSourceBadge').textContent, /^GPS desatualizado · \d{2}:\d{2}:\d{2}$/);
    assert.equal(first.mapMoves.length, 1);
    assert.equal(first.mapMoves[0].point.lat, captured.latitude);
    assert.equal(first.mapMoves[0].point.lng, captured.longitude);
    first.tracker.destroy();

    const reloaded = telemetryEnvironment();
    assert.equal(reloaded.tracker.applyTelemetryPosition(captured, { snap: true }), true);
    assert.equal(reloaded.elements.get('positionSourceBadge').dataset.state, 'stale');
    assert.equal(reloaded.tracker.lastPositionTimestamp, Date.parse(captured.timestamp));
    reloaded.tracker.destroy();
});

test('invalid coordinates, accuracy, capture dates and simulation flags never move the map', () => {
    const env = telemetryEnvironment();
    const invalid = [
        null, {},
        env.payload({ latitude: NaN }), env.payload({ latitude: 91 }), env.payload({ latitude: '-23.5' }),
        env.payload({ longitude: Infinity }), env.payload({ longitude: -181 }), env.payload({ longitude: undefined }),
        env.payload({ accuracyMeters: 51 }), env.payload({ accuracyMeters: -1 }),
        env.payload({ accuracyMeters: NaN }), env.payload({ accuracyMeters: '10' }),
        env.payload({ timestamp: null }), env.payload({ timestamp: env.now() }),
        env.payload({ timestamp: 'not-a-date' }), env.payload({ timestamp: '2026-02-30T12:00:00Z' }),
        env.payload({ timestamp: '2026-10-29T12:00:00' }), env.payload({ timestamp: '2026-10-29T24:00:00Z' }),
        env.payload({ timestamp: new Date(env.now() + 60001).toISOString() }),
        env.payload({ isSimulated: 'false' }), env.payload({ isSimulated: 'true' }),
        env.payload({ isSimulated: undefined })
    ];
    for (const payload of invalid) assert.equal(env.tracker.applyTelemetryPosition(payload), false);
    assert.equal(env.mapMoves.length, 0);
    assert.equal(env.tracker.currentLatLng, null);
    assert.equal(env.tracker.lastPositionTimestamp, null);
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'awaiting');
    env.tracker.destroy();
});

test('valid boundary coordinates, missing accuracy and the accepted future tolerance stay usable', () => {
    const env = telemetryEnvironment();
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: 0, longitude: 0, accuracyMeters: null }), { snap: true }), true);
    assert.equal(env.tracker.currentLatLng.lat, 0);
    assert.equal(env.tracker.currentLatLng.lng, 0);
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: -90, longitude: 180, accuracyMeters: 50,
        timestamp: new Date(env.now() + 60000).toISOString() }), { snap: true }), true);
    assert.equal(env.tracker.currentLatLng.lat, -90);
    assert.equal(env.tracker.currentLatLng.lng, 180);
    env.tracker.destroy();
});

test('an unrelated tracking code or older fix cannot overwrite the accepted position', () => {
    const env = telemetryEnvironment();
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ codigoRastreio: 'MGL-OTHER' })), false);
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ codigoRastreio: 123 })), false);
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ codigoRastreio: ' mgl-test ' })), true);
    const accepted = env.tracker.lastPositionTimestamp;
    const marker = env.tracker.currentLatLng;
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ timestamp: new Date(env.now() - 1).toISOString(), latitude: -10 })), false);
    assert.equal(env.tracker.lastPositionTimestamp, accepted);
    assert.equal(env.tracker.currentLatLng, marker);
    assert.equal(env.mapMoves.length, 1);
    env.tracker.destroy();
});

test('simulation is explicit and cannot be relabeled as confirmed or local GPS', () => {
    const env = telemetryEnvironment();
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ isSimulated: true }), { localOnly: true }), true);
    assert.equal(env.tracker.lastPositionSource, 'simulated');
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'simulated');
    assert.equal(env.elements.get('positionSourceBadge').textContent, 'SIMULAÇÃO — POSIÇÃO FICTÍCIA');
    env.advance(180000);
    env.tracker.updatePositionStatus();
    assert.equal(env.elements.get('positionSourceBadge').textContent, 'SIMULAÇÃO — POSIÇÃO FICTÍCIA');
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ isSimulated: 'false' })), false);
    assert.equal(env.tracker.lastPositionSource, 'simulated');
    env.tracker.destroy();
});

test('a device fix stays distinct from server confirmation and ages without claiming receipt', () => {
    const env = telemetryEnvironment();
    assert.equal(env.tracker.applyTelemetryPosition(env.payload(), { localOnly: true }), true);
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'local');
    assert.match(env.elements.get('positionSourceBadge').textContent, /^GPS do aparelho ·/);
    env.advance(120001);
    env.tracker.updatePositionStatus();
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'stale');
    assert.match(env.elements.get('positionSourceBadge').textContent, /^GPS do aparelho desatualizado ·/);
    assert.equal(env.tracker.applyTelemetryPosition(env.payload(), { snap: true }), true);
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'received');
    assert.match(env.elements.get('positionSourceBadge').textContent, /^GPS recebido ·/);
    env.tracker.destroy();
});

test('a first fix initializes a Leaflet map before creating its vehicle marker', () => {
    const env = telemetryEnvironment();
    const order = [];
    env.tracker.map = { setView(point) { order.push(['view', point.lat, point.lng]); } };
    env.tracker.setupVehicleMarker = point => { order.push(['marker', ...point]); };
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: -20, longitude: -40 })), true);
    assert.deepEqual(order, [['view', -20, -40], ['marker', -20, -40]]);
    env.tracker.destroy();
});

test('hub position and connection callbacks use the validated contract without inventing GPS', async () => {
    const env = telemetryEnvironment({ live: true });
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state, 'connecting');
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'awaiting');
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state, 'connected');
    assert.equal(env.mapMoves.length, 0);
    env.handlers.ReceiveVehiclePosition(env.payload({ accuracyMeters: 100 }));
    assert.equal(env.mapMoves.length, 0);
    env.handlers.ReceiveVehiclePosition(env.payload({ codigoRastreio: 'MGL-TEST' }));
    assert.equal(env.mapMoves.length, 1);
    env.handlers.reconnecting();
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state, 'reconnecting');
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'received');
    await env.handlers.reconnected();
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state, 'connected');
    assert.equal(env.invocations.filter(args => args[0] === 'JoinLiveTracking').length, 2);
    env.handlers.closed();
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state, 'closed');
    assert.equal(env.mapMoves.length, 1);
    env.tracker.destroy();
});

test('a failed authorized subscription is visibly disconnected and does not claim a position', async () => {
    const env = telemetryEnvironment({ live: true, joinFails: true });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state, 'closed');
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'awaiting');
    assert.equal(env.mapMoves.length, 0);
    env.tracker.destroy();
});

test('the freshness interval and registered listeners are released by destroy', async () => {
    const env = telemetryEnvironment({ live: true });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(env.tracker.applyTelemetryPosition(env.payload()), true);
    const freshness = env.intervals.get(env.tracker.positionFreshnessTimer);
    assert.equal(freshness.duration, 5000);
    env.advance(120001);
    freshness.callback();
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'stale');
    env.events.get('offline')();
    assert.equal(env.elements.get('telemetryConnectionState').textContent, 'Sem conexão de rede');
    env.tracker.destroy();
    env.tracker.destroy();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(env.intervals.size, 0);
    assert.equal(env.events.size, 0);
    assert.equal(env.removedMapListeners.length, 1);
    assert.equal(env.stopped(), 1);
    assert.equal(env.tracker.applyTelemetryPosition(env.payload()), false);
});

test('telemetry emission preserves the live seven-argument hub contract', () => {
    const env = telemetryEnvironment({ live: true });
    env.tracker.emitTelemetry(-23, -46, 40, 120, 8, false);
    const sent = env.invocations.find(args => args[0] === 'SendVehicleTelemetry');
    assert.deepEqual(sent, ['SendVehicleTelemetry', 'MGL-TEST', -23, -46, 40, 120, 8, false]);
    env.tracker.destroy();
});

test('traffic lookup does not substitute a planned route origin for absent GPS', async () => {
    const env = telemetryEnvironment();
    env.tracker.initialCoordinates = [[-23, -46], [-24, -47]];
    await env.tracker.fetchTrafficConditions();
    assert.equal(env.fetchCalls.length, 0);
    assert.equal(env.tracker.currentLatLng, null);
    assert.equal(env.mapMoves.length, 0);
    env.tracker.destroy();
});

test('traffic lookup and follow mode use the last accepted fix rather than route coordinates', async () => {
    const env = telemetryEnvironment();
    env.tracker.initialCoordinates = [[-23, -46], [-24, -47]];
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: -20, longitude: -40 })), true);
    await env.tracker.fetchTrafficConditions();
    assert.equal(env.fetchCalls.length, 1);
    assert.match(env.fetchCalls[0], /latAtual=-20&lngAtual=-40/);
    env.tracker.setFollowVehicle(true);
    assert.equal(env.mapMoves.length, 2);
    assert.equal(env.mapMoves[1].point.lat, -20);
    assert.equal(env.mapMoves[1].point.lng, -40);
    assert.equal(env.mapMoves[1].zoom, 15);
    env.tracker.destroy();
});

test('an absent or invalid planned duration stays unknown even after GPS and route progress', async () => {
    for (const tempoBaseMinutos of [undefined, null, 0, -10, NaN, '30']) {
        const env = telemetryEnvironment({ trackerOptions: { tempoBaseMinutos } });
        assert.equal(env.tracker.tempoBaseMinutos, 0);
        assert.equal(env.elements.get('etaMinutes').textContent, 'Sem estimativa');
        assert.equal(env.elements.get('etaValue').textContent, '--:--');
        env.tracker.routeCoordinates = env.tracker.initialCoordinates = [[-23, -46], [-24, -47]];
        env.tracker.routeCumulativeMeters = [0, 100];
        env.tracker.totalRouteMeters = 100;
        env.tracker.progressMeters = 60;
        // An on-route fix isolates duration handling from off-route quality gating.
        assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: -23, longitude: -46 })), true);
        assert.equal(env.elements.get('etaMinutes').textContent, 'Sem estimativa');
        assert.equal(env.elements.get('etaValue').textContent, '--:--');
        assert.equal(env.elements.get('tripProgressBar').style.width, '60%');
        assert.equal(env.elements.get('tripProgressBar').attributes['aria-valuenow'], '60');
        await env.tracker.fetchTrafficConditions();
        assert.equal(env.fetchCalls.length, 0);
        env.tracker.destroy();
    }
});

test('a known plan computes an explicitly estimated arrival and synchronizes accessible progress', () => {
    const env = telemetryEnvironment();
    env.tracker.routeCoordinates = [[-23, -46], [-24, -47]];
    env.tracker.routeCumulativeMeters = [0, 100];
    env.tracker.totalRouteMeters = 100;
    env.tracker.progressMeters = 60;
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: -23, longitude: -46 })), true);
    assert.equal(env.elements.get('etaMinutes').textContent, '12 min estimados');
    assert.match(env.elements.get('etaValue').textContent, /^\d{2}:\d{2}$/);
    assert.equal(env.elements.get('tripProgressBar').style.width, '60%');
    assert.equal(env.elements.get('tripProgressBar').attributes['aria-valuenow'], '60');
    env.tracker.destroy();
});

test('accepted GPS preserves missing-route and map-tile warnings while clearing only a GPS wait', () => {
    const env = telemetryEnvironment();
    for (const warning of ['Rota indisponível. Ative o GPS para visualizar sua posição.', 'Sem tiles de mapa. Verifique a conexão.']) {
        env.mapContainer.dataset.mapStatus = warning;
        assert.equal(env.tracker.applyTelemetryPosition(env.payload(), { snap: true }), true);
        assert.equal(env.mapContainer.dataset.mapStatus, warning);
        assert.equal(env.tracker.currentLatLng.lat, -23.5);
    }
    env.mapContainer.dataset.mapStatus = 'Aguardando uma posição GPS válida; a posição simulada não será usada como GPS real.';
    assert.equal(env.tracker.applyTelemetryPosition(env.payload(), { snap: true }), true);
    assert.equal(env.mapContainer.dataset.mapStatus, undefined);
    env.tracker.destroy();
});

test('route progress projects the accepted fix between sparse vertices rather than jumping to a vertex', () => {
    const env = telemetryEnvironment();
    env.tracker.routeCoordinates = [[0, 0], [0, .01]];
    env.tracker.routeCumulativeMeters = [0, 1111.95];
    env.tracker.totalRouteMeters = 1111.95;
    env.tracker.totalDistKm = 1.11195;
    env.tracker.progressMeters = 0;
    env.tracker.updateRouteProgress(0, .004);
    assert.equal(env.elements.get('tripProgressBar').attributes['aria-valuenow'], '40');
    assert.equal(env.elements.get('etaMinutes').textContent, '18 min estimados');
});

test('a fix far from the route keeps its real position without advancing progress or claiming an ETA', async () => {
    const env = telemetryEnvironment();
    env.tracker.routeCoordinates = env.tracker.initialCoordinates = [[0, 0], [0, .01]];
    env.tracker.routeCumulativeMeters = [0, 1111.95];
    env.tracker.totalRouteMeters = 1111.95;
    env.tracker.totalDistKm = 1.11195;
    env.tracker.progressMeters = 200;
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({ latitude: .02, longitude: .009 })), true);
    assert.equal(env.tracker.currentLatLng.lat, .02);
    assert.equal(env.tracker.currentLatLng.lng, .009);
    assert.equal(env.tracker.progressMeters, 200);
    assert.equal(env.elements.get('etaMinutes').textContent, 'Sem estimativa');
    assert.equal(env.elements.get('etaValue').textContent, '--:--');
    assert.equal(env.mapContainer.dataset.routeState, 'off-route');
    await env.tracker.fetchTrafficConditions();
    assert.equal(env.fetchCalls.length, 0);
    env.tracker.updateRouteProgress(0, .005);
    assert.equal(env.elements.get('tripProgressBar').attributes['aria-valuenow'], '50');
    assert.equal(env.mapContainer.dataset.routeState, 'on-route');
});


test('telemetry messages are rendered as literal text with an accessible status, never HTML', () => {
    const env = telemetryEnvironment();
    const message = '<img src=x onerror=alert(1)> GPS';
    env.tracker.showToastAlert(message);
    const toast = env.toasts[0];
    assert.equal(toast.innerHTML, undefined);
    assert.equal(toast.attributes.role, 'status');
    assert.equal(toast.children[1].textContent, message);
});

test('confirmed completion stops live updates and prevents any later GPS or hub callback from moving the vehicle', async () => {
    const env = telemetryEnvironment({live:true});
    await Promise.resolve(); await Promise.resolve();
    assert.equal(env.tracker.applyTelemetryPosition(env.payload()),true);
    env.tracker.completeDelivery();
    const before = env.mapMoves.length;
    assert.equal(env.tracker.destroyed,true);
    assert.equal(env.intervals.size,0);
    assert.equal(env.elements.get('telemetryConnectionState').dataset.state,'completed');
    assert.match(env.elements.get('positionSourceBadge').textContent,/GPS encerrado/);
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({latitude:-20})),false);
    env.handlers.ReceiveVehiclePosition(env.payload({latitude:-20}));
    assert.equal(env.mapMoves.length,before);
    env.tracker.completeDelivery();
    await Promise.resolve(); await Promise.resolve();
    assert.equal(env.stopped(),1);
});

test('a completed trip without any accepted GPS never claims that a last position exists', () => {
    const env=telemetryEnvironment();
    env.tracker.completeDelivery();
    assert.equal(env.elements.get('positionSourceBadge').textContent,'GPS encerrado · nenhuma posição recebida');
    assert.equal(env.mapMoves.length,0);
});

test('a recently received historical GPS capture never produces a fresh ETA', () => {
    const env = telemetryEnvironment();
    env.tracker.routeCoordinates = [[-23.5,-46.6],[-23.6,-46.7]];
    env.tracker.routeCumulativeMeters = [0, 100];
    env.tracker.totalRouteMeters = 100;
    env.tracker.applyTelemetryPosition(env.payload({ timestamp: new Date(env.now()-600000).toISOString() }), {snap:true});
    assert.equal(env.elements.get('positionSourceBadge').dataset.state, 'stale');
    assert.equal(env.elements.get('etaValue').textContent, '--:--');
    assert.equal(env.elements.get('etaMinutes').textContent, 'Sem estimativa');
});
test('legacy receive time is labeled as unknown capture age and never supplies a fresh ETA', () => {
    const env=telemetryEnvironment();
    assert.equal(env.tracker.applyTelemetryPosition(env.payload({captureTimeKnown:false}),{snap:true}),true);
    assert.equal(env.elements.get('positionSourceBadge').dataset.state,'unknown-age');
    assert.match(env.elements.get('positionSourceBadge').textContent,/captura não informada/);
    assert.equal(env.elements.get('etaValue').textContent,'--:--');
    env.tracker.destroy();
});

test('unverified route keeps the observed GPS marker but suppresses operational ETA and guidance', () => {
    const env=telemetryEnvironment({live:true,trackerOptions:{navigationAllowed:false}});
    env.tracker.applyTelemetryPosition(env.payload());
    assert.equal(env.tracker.currentLatLng.lat,-23.5);
    assert.equal(env.elements.get('etaValue').textContent,'--:--');
    env.handlers.ReceiveRouteRecalculated({codigoRastreio:'MGL-TEST',navigationAllowed:false});
    assert.equal(env.tracker.navigationAllowed,false);
});
test('revoked or expired route confidence suspends navigation without shifting the observed coordinate', () => {
    const env=telemetryEnvironment({live:true,trackerOptions:{navigationValidUntil:'2026-10-29T12:00:01Z'}});
    env.tracker.applyTelemetryPosition(env.payload());const lat=env.tracker.currentLatLng.lat;
    env.advance(2000);env.tracker.updateRouteProgress(-23.5,-46.6);
    assert.equal(env.tracker.navigationAllowed,false);assert.equal(env.elements.get('etaValue').textContent,'--:--');assert.equal(env.tracker.currentLatLng.lat,lat);
    env.handlers.ReceiveRouteTrustInvalidated({codigoRastreio:'MGL-OTHER'});assert.equal(env.tracker.currentLatLng.lat,lat);
});
