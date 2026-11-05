const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const NOW = Date.parse('2026-10-28T15:00:00Z');
const source = () => fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/fleet-map.js'), 'utf8');

// Small DOM with no HTML parser: any use of innerHTML fails the regression test.
class Element {
    constructor(tagName = 'div') {
        this.tagName = tagName.toUpperCase();
        this.children = [];
        this.dataset = {};
        this.attributes = new Map();
        this.listeners = new Map();
        this.value = '';
        this.hidden = false;
        this._text = '';
        this._classes = new Set();
        this.classList = {
            add: (...values) => values.forEach(value => this._classes.add(value)),
            remove: (...values) => values.forEach(value => this._classes.delete(value)),
            contains: value => this._classes.has(value),
            toggle: (value, force) => {
                const enabled = force === undefined ? !this._classes.has(value) : force;
                if (enabled) this._classes.add(value); else this._classes.delete(value);
                return enabled;
            }
        };
    }
    set textContent(value) { this._text = String(value ?? ''); this.children = []; }
    get textContent() { return this._text + this.children.map(child => typeof child === 'string' ? child : child.textContent).join(''); }
    set innerHTML(_) { throw new Error('Fleet data must be rendered through textContent, never innerHTML.'); }
    set className(value) { this._classes = new Set(String(value).split(/\s+/).filter(Boolean)); }
    get className() { return [...this._classes].join(' '); }
    append(...children) { this.children.push(...children); }
    appendChild(child) { this.append(child); return child; }
    replaceChildren(...children) { this._text = ''; this.children = []; this.append(...children); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    addEventListener(name, callback) { this.listeners.set(name, callback); }
    removeEventListener(name) { this.listeners.delete(name); }
}

function leafletMock() {
    const markers = [];
    const maps = [];
    const collection = () => ({
        layers: new Set(),
        addLayer(layer) { this.layers.add(layer); layer.owner = this; return this; },
        removeLayer(layer) { this.layers.delete(layer); if (layer.owner === this) layer.owner = null; return this; },
        clearLayers() { for (const layer of [...this.layers]) this.removeLayer(layer); return this; },
        addTo(owner) { owner.addLayer(this); return this; },
        remove() { this.owner?.removeLayer(this); return this; }
    });
    const marker = (position, options) => {
        const layer = {
            position, options, owner: null,
            addTo(owner) { owner.addLayer(this); return this; },
            bindPopup(content) { this.popup = content; return this; },
            bindTooltip(content) { this.tooltip = content; return this; },
            setLatLng(next) { this.position = next; return this; },
            setStyle(next) { this.options = { ...this.options, ...next }; return this; },
            remove() { this.owner?.removeLayer(this); return this; }
        };
        markers.push(layer);
        return layer;
    };
    const leaflet = {
        map() {
            const map = Object.assign(collection(), {
                setView(center, zoom) { this.center = center; this.zoom = zoom; return this; },
                panTo(center) { this.center = center; return this; },
                fitBounds(bounds) { this.bounds = bounds; return this; },
                invalidateSize() { return this; },
                remove() { this.clearLayers(); this.destroyed = true; }
            });
            maps.push(map);
            return map;
        },
        tileLayer: () => ({ addTo(owner) { owner.addLayer(this); return this; } }),
        circleMarker: marker,
        polyline: marker,
        marker,
        layerGroup: collection,
        featureGroup: collection,
        latLngBounds: coordinates => ({ coordinates, isValid: () => coordinates.length > 0 })
    };
    const activeMarkers = () => markers.filter(item => {
        let current = item;
        while (current.owner) {
            if (!current.owner.layers.has(current)) return false;
            current = current.owner;
        }
        return maps.includes(current) && !current.destroyed;
    });
    return { leaflet, markers, activeMarkers };
}

function trip(overrides = {}) {
    return {
        id: 7, codigoRastreio: 'MGL-FLEET-7', statusEntrega: 'Em transporte',
        cidadeDestino: 'Campinas', estadoDestino: 'SP', motorista: 'Motorista autorizado',
        veiculo: 'Caminhão', placa: 'ABC1D23', latitude: -23.55, longitude: -46.63,
        precisaoMetros: 12, velocidadeKmH: 35, direcao: 80,
        recebidoEmUtc: new Date(NOW - 10000).toISOString(), ...overrides
    };
}

function response(items, overrides = {}) {
    return {
        ok: true, status: 200, redirected: false,
        headers: { get: name => name.toLowerCase() === 'content-type' ? 'application/json' : null },
        json: async () => ({ items, consultadoEmUtc: new Date(NOW).toISOString() }),
        ...overrides
    };
}

function setup(items = [], fetchOverride) {
    const dom = {
        mapElement: new Element(), listElement: new Element(), statusElement: new Element(),
        statusFilter: new Element('select'), searchInput: new Element('input')
    };
    dom.mapElement.dataset.endpoint = '/Operacoes/Posicoes';
    dom.statusFilter.value = 'all';
    const document = {
        hidden: false, readyState: 'loading',
        createElement: tag => new Element(tag),
        createTextNode: text => String(text),
        getElementById: () => null, querySelector: () => null,
        addEventListener() {}, removeEventListener() {}
    };
    for (const element of Object.values(dom)) element.ownerDocument = document;
    const requestLog = [];
    const fetch = async (...args) => {
        requestLog.push(args);
        return fetchOverride ? fetchOverride(...args) : response(items);
    };
    const leaflet = leafletMock();
    const context = {
        console, document, fetch, URL, AbortController, Date, Number,
        navigator: { onLine: true }, location: { origin: 'https://mgl.test' },
        window: { addEventListener() {}, removeEventListener() {} },
        setInterval: () => 1, clearInterval() {}, setTimeout: () => 1, clearTimeout() {}
    };
    vm.runInNewContext(source() + '\nglobalThis.FleetMap = MglFleetMap;', context);
    const clock = { now: NOW };
    const fleet = new context.FleetMap({ ...dom, fetch, leaflet: leaflet.leaflet, now: () => clock.now });
    return { fleet, ...dom, ...leaflet, requestLog, clock };
}

// Check visible data and markers rather than the implementation's internal state.
test('missing, malformed and inaccurate GPS never creates a vehicle at a city fallback', async () => {
    const invalid = [
        { latitude: null, longitude: null },
        { latitude: '', longitude: -46 },
        { latitude: '-23.55', longitude: '-46.63' },
        { latitude: NaN, longitude: -46 },
        { latitude: -91, longitude: -46 },
        { latitude: -23, longitude: 181 },
        { latitude: -23, longitude: Infinity },
        { precisaoMetros: 51 }
    ];
    const page = setup(invalid.map((coordinates, index) => trip({ id: index + 1, codigoRastreio: `MISSING-${index}`, ...coordinates })));
    try {
        await page.fleet.refresh();
        assert.equal(page.markers.length, 0);
        assert.match(page.listElement.textContent, /MISSING-0/);
        assert.match(page.listElement.textContent + page.statusElement.textContent, /aguardando GPS|sem GPS|sem posição|baixa precisão/i);
    } finally { page.fleet.destroy(); }
});

test('only observed valid GPS coordinates produce vehicle markers', async () => {
    const page = setup([
        trip(),
        trip({ id: 8, codigoRastreio: 'MGL-NO-GPS', latitude: null, longitude: null })
    ]);
    try {
        await page.fleet.refresh();
        const markers = page.activeMarkers();
        assert.equal(markers.length, 1);
        assert.equal(markers[0].position[0], -23.55);
        assert.equal(markers[0].position[1], -46.63);
        assert.match(page.listElement.textContent, /MGL-FLEET-7/);
        assert.match(page.listElement.textContent, /MGL-NO-GPS/);
    } finally { page.fleet.destroy(); }
});

test('old positions are explicitly stale instead of presented as live GPS', async () => {
    const page = setup([trip({ recebidoEmUtc: new Date(NOW - 30 * 60000).toISOString() })]);
    try {
        await page.fleet.refresh();
        assert.match(page.listElement.textContent + page.statusElement.textContent, /desatualizad|aguardando GPS/i);
        assert.doesNotMatch(page.listElement.textContent, /GPS ao vivo|GPS ativo/i);
    } finally { page.fleet.destroy(); }
});

test('status and text filters update the loaded fleet without extra requests', async () => {
    const page = setup([
        trip(),
        trip({ id: 8, codigoRastreio: 'MGL-PENDING-8', statusEntrega: 'Pendente', cidadeDestino: 'Santos', placa: 'XYZ9E87' })
    ]);
    try {
        await page.fleet.refresh();
        const requests = page.requestLog.length;
        page.fleet.setFilter('Pendente', 'Santos');
        assert.match(page.listElement.textContent, /MGL-PENDING-8/);
        assert.doesNotMatch(page.listElement.textContent, /MGL-FLEET-7/);
        page.fleet.setFilter('all', 'abc1d23');
        assert.match(page.listElement.textContent, /MGL-FLEET-7/);
        assert.doesNotMatch(page.listElement.textContent, /MGL-PENDING-8/);
        page.fleet.setFilter('all', '');
        assert.match(page.listElement.textContent, /MGL-FLEET-7/);
        assert.match(page.listElement.textContent, /MGL-PENDING-8/);
        assert.equal(page.requestLog.length, requests);
    } finally { page.fleet.destroy(); }
});

for (const rejected of [
    { label: 'HTTP 401', options: { ok: false, status: 401 } },
    { label: 'HTTP 403', options: { ok: false, status: 403 } },
    { label: 'login redirect', options: { redirected: true } }
]) {
    test(`${rejected.label} clears private fleet data and prevents subsequent polling`, async () => {
        let calls = 0;
        const page = setup([], async () => ++calls === 1 ? response([trip()]) : response([], rejected.options));
        try {
            await page.fleet.refresh();
            assert.equal(page.activeMarkers().length, 1);
            await page.fleet.refresh();
            assert.equal(page.activeMarkers().length, 0);
            assert.doesNotMatch(page.listElement.textContent, /MGL-FLEET-7|ABC1D23|Motorista autorizado/);
            await page.fleet.refresh();
            assert.equal(calls, 2);
        } finally { page.fleet.destroy(); }
    });
}

test('untrusted operational text remains literal text rather than executable markup', async () => {
    const injected = '<img src=x onerror=alert(1)>';
    const page = setup([trip({ codigoRastreio: injected, motorista: '<script>alert(2)</script>', cidadeDestino: injected })]);
    try {
        await page.fleet.refresh();
        assert.ok(page.listElement.textContent.includes(injected));
        const popup = page.activeMarkers()[0]?.popup;
        if (popup) {
            assert.equal(typeof popup, 'object', 'Popup data must use a DOM node, not an HTML string.');
            assert.ok(popup.textContent.includes(injected));
        }
    } finally { page.fleet.destroy(); }
});

test('losing all GPS positions restores the empty map state instead of leaving an unexplained map', async () => {
    let calls = 0;
    const page = setup([], async () => response(++calls === 1 ? [trip()] : []));
    try {
        await page.fleet.refresh();
        assert.equal(page.activeMarkers().length, 1);
        await page.fleet.refresh();
        assert.equal(page.activeMarkers().length, 0);
        assert.match(page.mapElement.textContent, /aguardando GPS|sem posição/i);
    } finally { page.fleet.destroy(); }
});

test('an outage ages the last received fix instead of keeping an old green live state', async () => {
    let calls = 0;
    const page = setup([], async () => {
        if (++calls === 1) return response([trip()]);
        throw new TypeError('offline');
    });
    try {
        await page.fleet.refresh();
        page.clock.now += 180000;
        await page.fleet.refresh();
        assert.match(page.statusElement.textContent, /última consulta/i);
        assert.match(page.listElement.textContent, /desatualizado/i);
        assert.equal(page.activeMarkers().length, 1);
    } finally { page.fleet.destroy(); }
});
test('a newly received historical fix is stale according to capture time', async()=>{
    const page=setup([trip({capturadoEmUtc:new Date(NOW-600000).toISOString(),recebidoEmUtc:new Date(NOW).toISOString()})]);
    try {await page.fleet.refresh();assert.match(page.listElement.textContent,/desatualizado/);assert.doesNotMatch(page.listElement.textContent,/GPS recebido/);}
    finally{page.fleet.destroy();}
});

test('a planned road is independent of GPS and disappears after a failed planning attempt', async () => {
    let available=true;
    const plan={planningId:'plan-1',rotaDisponivel:true,trajetoCoordenadas:[{latitude:-23.55,longitude:-46.63},{latitude:-23.551,longitude:-46.632}]};
    const page=setup([trip({latitude:null,longitude:null})],async url=>url.includes('/Planejamento')
        ?{ok:true,json:async()=>({plan:available?plan:{planningId:'failed',rotaDisponivel:false}})}
        :response([trip({latitude:null,longitude:null})]));
    await page.fleet.refresh();page.fleet.selectedId=7;await page.fleet.loadRoadPlan(7);
    assert.equal(page.mapElement.dataset.planId,'plan-1');assert.equal(page.fleet.routeLayer.layers.size,4);
    page.fleet.renderMap(page.fleet.items);
    assert.ok(!page.fleet.emptyOverlay||page.fleet.emptyOverlay.hidden,'Waiting for GPS cannot mask the road plan');
    available=false;await page.fleet.loadRoadPlan(7);
    assert.equal(page.mapElement.dataset.planId,'');assert.equal(page.fleet.routeLayer.layers.size,0);
    page.fleet.destroy();
});
