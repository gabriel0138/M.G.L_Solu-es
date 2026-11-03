class MglFleetMap {
    constructor(options) {
        this.mapElement = options.mapElement;
        this.listElement = options.listElement;
        this.statusElement = options.statusElement;
        this.statusFilter = options.statusFilter;
        this.searchInput = options.searchInput;
        this.detailElement = options.detailElement;
        this.summaryElement = options.summaryElement;
        this.followInput = options.followInput;
        this.fetch = options.fetch || globalThis.fetch.bind(globalThis);
        this.leaflet = options.leaflet || globalThis.L;
        this.now = options.now || (() => Date.now());
        this.items = [];
        this.rows = new Map();
        this.selectedId = null;
        this.filterStatus = 'all';
        this.search = '';
        this.running = false;
        this.stopped = false;
        this.boundsInitialized = false;
        this.map = null;
        this.markerLayer = null;
        this.statusFilter?.addEventListener('change', () => this.setFilter(this.statusFilter.value, this.searchInput?.value || ''));
        this.searchInput?.addEventListener('input', () => this.setFilter(this.statusFilter?.value || 'all', this.searchInput.value));
        this.followInput?.addEventListener('change', () => this.renderMap(this.filtered()));
    }
    node(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }
    position(item) {
        const { latitude, longitude, precisaoMetros } = item;
        const received = Date.parse(item.capturadoEmUtc ?? item.recebidoEmUtc);
        if (!Number.isFinite(latitude) || latitude < -90 || latitude > 90
            || !Number.isFinite(longitude) || longitude < -180 || longitude > 180
            || (precisaoMetros != null && (!Number.isFinite(precisaoMetros) || precisaoMetros < 0 || precisaoMetros > 50))
            || !Number.isFinite(received) || received > this.now() + 60000) return null;
        return { lat: latitude, lng: longitude, received, captureKnown: item.capturadoEmUtc != null, stale: this.now() - received > 120000 };
    }
    text(value, fallback = '') { return String(value ?? fallback); }
    normalize(value) { return this.text(value).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase(); }
    filtered() {
        const query = this.normalize(this.search).trim();
        return this.items.filter(item => (this.filterStatus === 'all' || item.statusEntrega === this.filterStatus)
            && (!query || this.normalize([item.codigoRastreio, item.motorista, item.placa, item.veiculo, item.cidadeDestino].join(' ')).includes(query)));
    }
    setFilter(status, search) {
        this.filterStatus = ['Pendente', 'Em transporte'].includes(status) ? status : 'all';
        this.search = this.text(search);
        this.boundsInitialized = false;
        this.render();
    }
    async refresh() {
        if (this.running || this.stopped) return;
        this.running = true;
        const abort = new AbortController();
        this.abort = abort;
        const timeout = setTimeout(() => abort.abort(), 10000);
        try {
            const response = await this.fetch(this.mapElement.dataset.endpoint, { credentials: 'same-origin', cache: 'no-store', signal: abort.signal });
            if (response.redirected || response.status === 401 || response.status === 403) {
                this.stopped = true;
                this.items = [];
                this.rows.clear();
                this.map?.remove();
                this.map = null;
                this.markerLayer = null;
                this.mapElement.replaceChildren();
                this.listElement.replaceChildren();
                this.detailElement?.replaceChildren();
                if (this.summaryElement) this.summaryElement.textContent = '';
                this.statusElement.textContent = 'Seu acesso mudou. Entre novamente para consultar as posições.';
                return;
            }
            if (!response.ok) throw new Error('Position request failed');
            const payload = await response.json();
            if (!Array.isArray(payload.items)) throw new Error('Invalid position response');
            this.items = payload.items.filter(item => item && Number.isSafeInteger(item.id) && item.id > 0);
            this.render();
            const count = this.filtered().length;
            const positioned = this.filtered().filter(item => this.position(item)).length;
            this.statusElement.textContent = `${count} viagem(ns) · ${positioned} com GPS informado. Consulta automática a cada 10 segundos.`;
        } catch {
            if (!this.stopped) {
                this.render();
                this.statusElement.textContent = 'Não foi possível atualizar. As posições exibidas são da última consulta; confira seus horários.';
            }
        } finally {
            clearTimeout(timeout);
            this.running = false;
        }
    }
    render() {
        const items = this.filtered();
        // Keep a focused row stable while authorization continues to be checked.
        if (!this.listElement.contains?.(document.activeElement)) {
            this.listElement.replaceChildren();
            this.rows.clear();
            if (!items.length) this.listElement.append(this.node('div', 'cc-empty-state', 'Nenhuma viagem ativa encontrada para os filtros selecionados.'));
            for (const item of items) {
                const pos = this.position(item);
                const row = this.node('button', 'cc-fleet-trip');
                row.type = 'button';
                row.dataset.state = pos ? pos.stale ? 'stale' : pos.captureKnown ? 'fresh' : 'unknown' : 'waiting';
                row.setAttribute('aria-pressed', String(item.id === this.selectedId));
                const heading = this.node('span', 'cc-fleet-trip-header');
                heading.append(this.node('strong', '', this.text(item.codigoRastreio)), this.node('span', 'cc-status', this.text(item.statusEntrega)));
                row.append(heading,
                    this.node('span', '', [item.veiculo || 'Veículo não informado', item.placa].filter(Boolean).join(' · ')),
                    this.node('span', 'cc-fleet-location-meta', [item.cidadeDestino, item.estadoDestino].filter(Boolean).join(' / ')),
                    this.node('span', 'cc-fleet-location-meta', item.motorista || 'Sem motorista informado'),
                    this.node('span', 'cc-fleet-location-meta', pos ? `${pos.stale ? 'Sinal desatualizado' : pos.captureKnown ? 'GPS recebido' : 'Captura não informada; recebimento'} · ${new Date(pos.received).toLocaleString('pt-BR')}` : 'Aguardando GPS'));
                if (pos) row.append(this.node('small', 'cc-fleet-location-meta', Number.isFinite(item.precisaoMetros) ? `Precisão informada: ${Math.round(item.precisaoMetros)} m` : 'Precisão não informada'));
                row.addEventListener('click', () => this.select(item.id));
                this.rows.set(item.id, row);
                this.listElement.append(row);
            }
        }
        if (this.summaryElement) this.summaryElement.textContent = String(items.length);
        this.renderMap(items);
        if (this.selectedId && !items.some(item => item.id === this.selectedId)) {
            this.selectedId = null;
            if (this.detailElement) this.detailElement.textContent = 'Selecione uma viagem para consultar seu último sinal.';
        }
    }
    color(token) {
        return typeof getComputedStyle === 'function'
            ? getComputedStyle(document.documentElement).getPropertyValue(token).trim() : 'currentColor';
    }
    renderMap(items) {
        const located = items.map(item => ({ item, pos: this.position(item) })).filter(entry => entry.pos);
        this.markerLayer?.clearLayers();
        if (!located.length || !this.leaflet) {
            if (!this.emptyOverlay || !this.map) {
                const empty = this.node('div', 'cc-fleet-empty');
                empty.append(this.node('strong', '', !this.leaflet ? 'Mapa indisponível neste navegador' : 'Aguardando GPS'),
                    this.node('p', '', 'Nenhuma posição é estimada a partir da cidade ou do trajeto. A lista de viagens continua disponível.'));
                if (this.map) this.mapElement.append(empty); else this.mapElement.replaceChildren(empty);
                this.emptyOverlay = empty;
            }
            this.emptyOverlay.hidden = false;
            return;
        }
        if (this.emptyOverlay) this.emptyOverlay.hidden = true;
        if (!this.map) {
            this.mapElement.replaceChildren();
            this.emptyOverlay = null;
            this.map = this.leaflet.map(this.mapElement, { scrollWheelZoom: false });
            this.leaflet.tileLayer(this.mapElement.dataset.tileUrl || 'https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                attribution: this.mapElement.dataset.attribution || '© OpenStreetMap contributors', maxZoom: 19
            }).addTo(this.map);
            this.markerLayer = this.leaflet.layerGroup().addTo(this.map);
        }
        for (const { item, pos } of located) {
            const popup = this.node('div', 'cc-fleet-popup');
            popup.append(this.node('strong', '', this.text(item.codigoRastreio)), this.node('p', '', [item.veiculo, item.placa].filter(Boolean).join(' · ')),
                this.node('p', '', `${pos.stale ? 'Sinal desatualizado' : pos.captureKnown ? 'GPS recebido' : 'Captura não informada; recebimento'} · ${new Date(pos.received).toLocaleString('pt-BR')}`));
            this.leaflet.circleMarker([pos.lat, pos.lng], { radius: item.id === this.selectedId ? 10 : 7,
                color: this.color(pos.stale || !pos.captureKnown ? '--mgl-text-tertiary' : '--mgl-success'),
                fillColor: this.color(pos.stale || !pos.captureKnown ? '--mgl-text-tertiary' : '--mgl-success'), weight: 2, fillOpacity: .8
            }).addTo(this.markerLayer).bindPopup(popup);
        }
        const selected = located.find(entry => entry.item.id === this.selectedId);
        if (selected && this.followInput?.checked) this.map.panTo([selected.pos.lat, selected.pos.lng], { animate: false });
        else if (!this.boundsInitialized) {
            const coordinates = located.map(entry => [entry.pos.lat, entry.pos.lng]);
            if (coordinates.length === 1) this.map.setView(coordinates[0], 13);
            else this.map.fitBounds(this.leaflet.latLngBounds(coordinates), { padding: [24, 24] });
            this.boundsInitialized = true;
        }
    }
    select(id) {
        const item = this.filtered().find(entry => entry.id === id);
        if (!item) return;
        this.selectedId = id;
        for (const [key, row] of this.rows) row.setAttribute('aria-pressed', String(key === id));
        const pos = this.position(item);
        if (pos && this.map) this.map.setView([pos.lat, pos.lng], 15);
        if (this.detailElement) {
            const link = this.node('a', 'btn btn-sm btn-outline-secondary', 'Abrir cockpit da viagem');
            link.href = '/Entregas/Navegar/' + id;
            this.detailElement.replaceChildren(this.node('strong', '', this.text(item.codigoRastreio)),
                this.node('span', '', pos ? pos.stale ? 'Última posição desatualizada.' : pos.captureKnown ? 'Último GPS recebido no servidor.' : 'Horário da captura desconhecido; consulte o recebimento.' : 'Aguardando GPS: sem marcador no mapa.'), link);
            const historyLink = this.node('a', 'btn btn-outline-secondary', 'Histórico GPS');
            historyLink.href = '/GpsHistorico?entregaId=' + item.id;
            this.detailElement.append(historyLink);
        }
        this.renderMap(this.filtered());
    }
    destroy() {
        this.stopped = true;
        this.abort?.abort();
        this.map?.remove();
        this.map = null;
    }
}

if (typeof document !== 'undefined') {
    const boot = () => {
        const mapElement = document.getElementById('fleetMap');
        if (!mapElement) return;
        const fleet = new MglFleetMap({ mapElement, listElement: document.getElementById('fleetTripList'),
            statusElement: document.getElementById('fleetMapStatus'), statusFilter: document.getElementById('fleetStatusFilter'),
            searchInput: document.getElementById('fleetSearch'), detailElement: document.getElementById('fleetSelectionDetails'),
            summaryElement: document.getElementById('fleetSummary'), followInput: document.getElementById('fleetFollow') });
        fleet.refresh();
        const timer = setInterval(() => { if (!document.hidden && navigator.onLine) fleet.refresh(); }, 10000);
        window.addEventListener('online', () => fleet.refresh());
        window.addEventListener('pagehide', () => { clearInterval(timer); fleet.destroy(); });
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
}
