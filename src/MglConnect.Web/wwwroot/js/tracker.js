/**
 * M.G.L Connect - Rastreamento em tempo real de posições observadas
 * Arquitetura: OpenStreetMap / Esri + rotas OSRM e telemetria SignalR
 * Posições ao vivo exigem telemetria; um trajeto planejado não é uma posição GPS.
 */
class MglCockpitTracker {
    /**
     * Instancia as camadas do ecossistema híbrido GIS:
     * 1. OpenStreetMap (nomes de ruas)
     * 2. Esri Dark Gray
     * 3. Esri World Imagery (satélite)
     */
    static createBaseLayers() {
        const esriDark = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}', {
            maxZoom: 19,
            attribution: 'Tiles &copy; Esri &mdash; Esri, DeLorme, NAVTEQ'
        });

        const esriImagery = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
            maxZoom: 19,
            attribution: 'Tiles &copy; Esri'
        });

        const osmStandard = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap contributors</a>'
        });

        const baseMaps = {
            "OpenStreetMap (ruas)": osmStandard,
            "Mapa escuro (Esri)": esriDark,
            "Satélite (Esri)": esriImagery
        };

        return {
            esriDark,
            esriImagery,
            osmStandard,
            baseMaps
        };
    }

    constructor(options) {
        this.demoMode = options.demoMode === true;
        this.demoRouteAvailable = this.demoMode && options.coordinates?.length > 1;
        this.navigationAllowed = options.navigationAllowed !== false;
        this.navigationValidUntil = Date.parse(options.navigationValidUntil);
        this.map = options.map;
        this.entregaId = options.entregaId;
        this.codigoRastreio = (options.codigoRastreio || '').trim().toUpperCase();
        this.initialCoordinates = options.coordinates || [];
        this.routeCoordinates = this.initialCoordinates;
        this.steps = options.steps || [];
        this.totalDistKm = options.totalDistKm || 0;
        this.totalFuelLiters = options.totalFuelLiters || 0;
        this.tempoBaseMinutos = Number.isFinite(options.tempoBaseMinutos) && options.tempoBaseMinutos > 0
            ? options.tempoBaseMinutos : 0;
        this.baseMaps = options.baseMaps || null;
        this.activeLayerName = options.activeLayerName || "OpenStreetMap (ruas)";

        // Elementos DOM HUD
        this.speedElem = document.getElementById('speedValue');
        this.etaElem = document.getElementById('etaValue');
        this.etaMinutesElem = document.getElementById('etaMinutes');
        this.distElem = document.getElementById('distanceRemaining');
        this.fuelElem = document.getElementById('fuelRemaining');
        this.progressBar = document.getElementById('tripProgressBar');
        this.maneuverRoad = document.getElementById('maneuverRoad');
        this.maneuverInstruction = document.getElementById('maneuverInstruction');
        this.maneuverIcon = document.getElementById('maneuverIcon');
        this.maneuverDist = document.getElementById('maneuverDist');
        this.trafficBadge = document.getElementById('trafficStatusBadge');
        this.positionSourceBadge = document.getElementById('positionSourceBadge');
        this.telemetryConnectionState = document.getElementById('telemetryConnectionState');
        this.lastPositionTimestamp = null;
        this.lastPositionSource = null;
        this.destroyed = false;
        this.followVehicle = false;
        if (this.tempoBaseMinutos === 0) this.updateArrivalEstimate(null);

        // Camadas Leaflet
        this.routePolylineOutline = null;
        this.routePolyline = null;
        this.oldRoutePolyline = null;
        this.vehicleMarker = null;
        this.destinationMarker = null;

        // The marker represents an observed fix, never an interpolated road position.
        this.currentLatLng = null;
        this.currentHeading = 0;

        // Conexão SignalR TelemetryHub
        this.hubConnection = null;
        this.lastTelemetryEmitTime = 0;

        this.currentStepIndex = 0;
        this.routeCumulativeMeters = [];
        this.totalRouteMeters = 0;
        this.progressMeters = 0;

        // Fila local de GPS para quando o sinal for perdido (4G na rodovia)
        // Será sincronizada automaticamente ao retomar conexão online
        this.gpsOfflineQueue = [];

        // Flag de conexão online/offline
        this.isOnline = navigator.onLine;

        // Event listener para mudança de status de conectividade
        this.onlineHandler = () => {
            this.isOnline = true;
            this.syncOfflineGPS();
        };
        this.offlineHandler = () => {
            this.isOnline = false;
            this.updateConnectionStatus('closed');
        };
        window.addEventListener('online', this.onlineHandler);
        window.addEventListener('offline', this.offlineHandler);

        this.gpsReceiptHandler = event => {
            const { receipt, metrics } = event.detail || {};
            if (receipt?.posicao) this.applyTelemetryPosition(receipt.posicao);
            if (!this.demoMode && metrics?.navigationAllowed===false)this.suspendNavigation();
            if (metrics && !metrics.gpsDataAccepted) this.showToastAlert(metrics.motivo || 'GPS ignorado.', 'bi-geo-alt', 'var(--mgl-warning)');
        };
        window.addEventListener('mgl:gps-confirmed', this.gpsReceiptHandler);
        this.updatePositionStatus();
        this.positionFreshnessTimer = setInterval(() => {this.updatePositionStatus();if(Number.isFinite(this.navigationValidUntil)&&Date.now()>this.navigationValidUntil)this.suspendNavigation('EXPIRADA');}, 5000);
        this.init();

        // Sincronizar dados offline ao retomar conexão (Service Worker already listens for 'sync' event,
        // mas também podemos fazer polling imediato quando o navegador volta online)
        if (this.isOnline) {
            this.syncOfflineGPS();
        }
    }

    init() {
        this.mapDragHandler=()=>this.setFollowVehicle(false);
        this.mapDragRegistered=typeof this.map?.on==='function';
        if(this.mapDragRegistered)this.map.on('dragstart',this.mapDragHandler);
        this.setupRouteLayers(this.initialCoordinates);
        this.setupSignalR();
        this.setupTrafficLayerMatch();
        this.setupLayerListeners();
        if (typeof ResizeObserver === 'function' && this.map?.getContainer && this.map?.invalidateSize) {
            this.mapResizeObserver = new ResizeObserver(() => {
                if (this.mapResizeFrame) cancelAnimationFrame(this.mapResizeFrame);
                this.mapResizeFrame = requestAnimationFrame(() => {
                    this.mapResizeFrame = null;
                    if (!this.destroyed) {
                        this.map.invalidateSize({ pan: false });
                        if(this.followVehicle && this.currentLatLng)this.map.panTo?.(this.currentLatLng,{animate:false});
                        else if(this.followVehicle)this.fitPlannedRoute();
                    }
                });
            });
            this.mapResizeObserver.observe(this.map.getContainer());
        }
    }

    fitPlannedRoute() {
        if(!this.routePolyline)return;
        const container=this.map.getContainer?.();
        const controls=typeof document!=='undefined'?document.querySelector?.('.cc-cockpit-controls'):null;
        const rect=container?.getBoundingClientRect?.(),panel=controls?.getBoundingClientRect?.();
        let bottom=24,top=24;
        if(rect&&panel&&panel.bottom>rect.top&&panel.top<rect.bottom)
            bottom=Math.min(Math.max(24,rect.bottom-panel.top+12),Math.max(24,rect.height*.45));
        this.map.fitBounds(this.routePolyline.getBounds(),{paddingTopLeft:[24,top],paddingBottomRight:[24,bottom],animate:false});
    }

    setupRouteLayers(coordinates) {
        if (!coordinates || coordinates.length === 0) return;
        this.routeCoordinates = coordinates;
        this.routeCumulativeMeters = [0];
        this.totalRouteMeters = 0;
        this.progressMeters = 0;
        for (let i = 1; i < coordinates.length; i++) {
            const segmentMeters = L.latLng(coordinates[i - 1]).distanceTo(L.latLng(coordinates[i]));
            this.totalRouteMeters += segmentMeters;
            this.routeCumulativeMeters.push(this.totalRouteMeters);
        }

        // Remove rota antiga se existir
        if (this.routePolylineOutline) this.map.removeLayer(this.routePolylineOutline);
        if (this.routePolyline) this.map.removeLayer(this.routePolyline);

        // 1. Casing escuro inferior para contraste (essencial sobre satélite e mapas claros)
        this.routePolylineOutline = L.polyline(coordinates, {
            color: '#0A1B2B',
            weight: 10,
            opacity: 0.90,
            lineCap: 'round',
            lineJoin: 'round'
        }).addTo(this.map);

        // 2. Traçado vibrante MGL Teal
        this.routePolyline = L.polyline(coordinates, {
            color: '#00C9B7',
            weight: 6,
            opacity: 1.0,
            lineCap: 'round',
            lineJoin: 'round'
        }).addTo(this.map);

        this.fitPlannedRoute();

        // Marcador de Destino
        const destCoord = coordinates[coordinates.length - 1];
        if (!this.destinationMarker) {
            this.destinationMarker = L.circleMarker(destCoord, {
                radius: 12,
                fillColor: '#FFB020',
                color: '#FFFFFF',
                weight: 3,
                fillOpacity: 1
            }).addTo(this.map).bindPopup('<b>Destino Final</b><br>Polo Logístico de Entrega');
        } else {
            this.destinationMarker.setLatLng(destCoord);
        }

        this.bringOverlaysToFront();
    }

    setupVehicleMarker(startPoint) {
        if (!startPoint) return;
        this.currentLatLng = L.latLng(startPoint[0], startPoint[1]);

        const vehicleIcon = L.divIcon({
            html: `
                <div id="cockpitVehicleMarker" class="vehicle-pulsing-marker" style="
                    width: 44px; height: 44px; 
                    background: #00C9B7; 
                    border: 3px solid #FFFFFF; 
                    border-radius: 50%; 
                    display: flex; align-items: center; justify-content: center; 
                    box-shadow: 0 0 18px rgba(0, 201, 183, 0.95);
                    transform: rotate(0deg);
                    transform-origin: center center;
                    will-change: transform;">
                    <i class="bi bi-truck-front-fill" style="color: #0A1B2B; font-size: 1.35rem;"></i>
                </div>
            `,
            className: 'vehicle-marker-wrapper',
            iconSize: [44, 44],
            iconAnchor: [22, 22]
        });

        this.vehicleMarker = L.marker(this.currentLatLng, { icon: vehicleIcon, zIndexOffset: 1000 }).addTo(this.map);
    }

    // Garante que todas as camadas de sobreposição fiquem sempre no topo após troca de base tile
    bringOverlaysToFront() {
        if (this.oldRoutePolyline) this.oldRoutePolyline.bringToFront();
        if (this.routePolylineOutline) this.routePolylineOutline.bringToFront();
        if (this.routePolyline) this.routePolyline.bringToFront();
        if (this.destinationMarker) this.destinationMarker.bringToFront();
        if (this.vehicleMarker) this.vehicleMarker.setZIndexOffset(1000);
    }

    setupLayerListeners() {
        if (!this.map || typeof this.map.on !== 'function') return;
        this.baseLayerChangeHandler = (e) => {
            this.activeLayerName = e.name;
            this.bringOverlaysToFront();
        };
        this.map.on('baselayerchange', this.baseLayerChangeHandler);
    }

    // Alterna sequencialmente as camadas de mapa disponíveis no baseMaps
    cycleBaseLayer() {
        if (!this.baseMaps || !this.map) return null;

        const keys = Object.keys(this.baseMaps);
        if (keys.length === 0) return null;

        let currentIdx = keys.indexOf(this.activeLayerName);
        if (currentIdx === -1) currentIdx = 0;

        const currentKey = keys[currentIdx];
        const nextIdx = (currentIdx + 1) % keys.length;
        const nextKey = keys[nextIdx];

        const currentLayer = this.baseMaps[currentKey];
        const nextLayer = this.baseMaps[nextKey];

        if (this.map.hasLayer(currentLayer)) {
            this.map.removeLayer(currentLayer);
        }
        this.map.addLayer(nextLayer);
        this.activeLayerName = nextKey;
        this.bringOverlaysToFront();

        this.showToastAlert(`Camada ativa: ${nextKey}`, 'bi-layers-fill', '#00C9B7');
        return nextKey;
    }

    setBaseLayer(name) {
        if (!this.baseMaps || !this.map || !this.baseMaps[name]) return;

        Object.keys(this.baseMaps).forEach(k => {
            if (this.map.hasLayer(this.baseMaps[k])) {
                this.map.removeLayer(this.baseMaps[k]);
            }
        });

        this.map.addLayer(this.baseMaps[name]);
        this.activeLayerName = name;
        this.bringOverlaysToFront();
    }

    setupSignalR() {
        if (typeof signalR === 'undefined') {
            console.warn('SignalR client não carregado.');
            this.updateConnectionStatus('closed');
            return;
        }

        this.updateConnectionStatus('connecting');
        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl('/telemetryHub')
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .build();

        // 1. Recebimento de Coordenadas em Tempo Real (outros dispositivos / central)
        this.hubConnection.on('ReceiveDemoState', payload => this.applyControlledDemoState(payload));
        this.hubConnection.on('ReceiveVehiclePosition', (payload) => {
            this.applyTelemetryPosition(payload);
        });

        // 2. Recebimento de Recálculo Dinâmico de Rota (Off-Route acionado no C#)
        this.hubConnection.on('ReceiveRouteTrustInvalidated', (payload) => {
            if(payload.codigoRastreio===this.codigoRastreio)this.suspendNavigation('OBSOLETA');
        });
        this.hubConnection.on('ReceiveRouteRecalculated', (payload) => {
            if(payload.codigoRastreio!==this.codigoRastreio)return;
            if(payload.navigationAllowed===false){this.suspendNavigation();return;}
            if (payload.codigoRastreio === this.codigoRastreio) {
                this.applyRouteReroute(payload);
            }
        });

        // 3. Telemetria Diagnóstica (XTE / ATD)
        this.hubConnection.on('ReceiveTelemetryMetrics', (metrics) => {
            if(metrics.codigoRastreio&&metrics.codigoRastreio!==this.codigoRastreio)return;
            if(!this.demoMode && metrics.navigationAllowed===false){this.suspendNavigation();return;}
            if (!metrics.gpsDataAccepted) {
                this.showToastAlert(metrics.motivo || 'A posição GPS foi ignorada por baixa qualidade.', 'bi-geo-alt-fill', '#FFB020');
            } else if (metrics.isOffRoute) {
                this.showToastAlert(`Desvio detectado: ${metrics.crossTrackDistanceMeters}m fora da via.`, 'bi-exclamation-triangle-fill', '#FFB020');
            }
        });

        this.hubConnection.on('ReceiveRouteRecalculationFailed', (payload) => {
            if(payload.codigoRastreio===this.codigoRastreio)this.suspendNavigation('NÃO VERIFICADA');
            if (payload.codigoRastreio === this.codigoRastreio) {
                this.showToastAlert(payload.mensagem, 'bi-exclamation-triangle-fill', '#EF4444');
            }
        });

        const joinLiveTracking = () => this.hubConnection.invoke('JoinLiveTracking', this.codigoRastreio);
        const connected = () => {
            if (this.destroyed) return Promise.resolve();
            return joinLiveTracking().then(() => {
                if (!this.destroyed) {
                    this.updateConnectionStatus('connected');
                    this.refreshConfirmedPosition();
                }
            });
        };
        this.hubConnection.onreconnecting(() => {
            if (!this.destroyed) this.updateConnectionStatus('reconnecting');
        });
        this.hubConnection.onreconnected(() => connected().catch(() => this.updateConnectionStatus('closed')));
        this.hubConnection.onclose(() => {
            if (!this.destroyed) this.updateConnectionStatus('closed');
        });

        this.hubConnection.start()
            .then(connected)
            .then(() => {
                if (!this.destroyed) console.debug('Conexão de telemetria autorizada.');
            })
            .catch(err => {
                if (this.destroyed) return;
                this.updateConnectionStatus('closed');
                console.error('Erro ao conectar ao TelemetryHub:', err);
            });
    }

    applyControlledDemoState(data) {
        if (!this.demoMode || this.destroyed || (data?.plan?.demonstracaoControlada !== true && data?.plan?.simulationEnabled !== true)) return false;
        const plan=data.plan;
        if(!plan.rotaDisponivel){
            this.suspendNavigation();
            for(const key of ['routePolyline','routePolylineOutline','destinationMarker'])if(this[key]){this.map.removeLayer(this[key]);this[key]=null;}
            this.routeCoordinates=[];this.steps=[];return false;
        }
        const coords=(plan.trajetoCoordenadas||[]).map(c=>[c.latitude,c.longitude]);
        if(coords.length<2||coords.some(c=>!c.every(Number.isFinite)||Math.abs(c[0])>90||Math.abs(c[1])>180))return false;
        this.demoRouteAvailable=true;this.totalDistKm=plan.distanciaKm;this.tempoBaseMinutos=plan.tempoEstimadoMinutos;this.steps=plan.passosNavegacao||[];
        if(JSON.stringify(coords)!==JSON.stringify(this.routeCoordinates)){
            this.setupRouteLayers(coords);this.currentStepIndex=0;
        }
        if(data.latest)this.applyTelemetryPosition(data.latest);
        return true;
    }

    async refreshConfirmedPosition() {
        if (this.destroyed || !this.entregaId) return;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch(this.demoMode ? `/Entregas/${this.entregaId}/Demonstracao` : `/api/gps/historico/${this.entregaId}`, { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
            if (!response.ok || response.redirected || this.destroyed) return;
            const history = await response.json();
            if(this.demoMode){this.applyControlledDemoState(history);return;}
            if (history.ultimaPosicao && !this.destroyed) this.applyTelemetryPosition(history.ultimaPosicao);
        } catch { /* Keep the last observed position and its original age. */ }
        finally { clearTimeout(timeout); }
    }

    updateConnectionStatus(state) {
        if (!this.telemetryConnectionState) return;
        const labels = {
            connecting: 'Conectando às atualizações',
            connected: 'Atualizações ao vivo conectadas',
            reconnecting: 'Reconectando às atualizações',
            closed: this.isOnline === false ? 'Sem conexão de rede' : 'Atualizações ao vivo desconectadas',
            completed: 'Viagem concluída · atualizações encerradas',
            cancelled: 'Viagem cancelada · atualizações encerradas'
        };
        this.telemetryConnectionState.textContent = labels[state] || labels.closed;
        this.telemetryConnectionState.dataset.state = Object.hasOwn(labels, state) ? state : 'closed';
    }

    // Shared entry point for persisted snapshots, hub updates and local device fixes.
    // A planned route never supplies a fallback position or a capture timestamp.
    applyTelemetryPosition(payload, options = {}) {
        if (this.destroyed || !payload || typeof payload !== 'object') return false;
        const { latitude, longitude, accuracyMeters, timestamp, isSimulated } = payload;
        if (!Number.isFinite(latitude) || latitude < -90 || latitude > 90
            || !Number.isFinite(longitude) || longitude < -180 || longitude > 180
            || (accuracyMeters != null && (!Number.isFinite(accuracyMeters) || accuracyMeters < 0 || accuracyMeters > 50))
            || typeof isSimulated !== 'boolean') return false;

        if (payload.codigoRastreio != null
            && (typeof payload.codigoRastreio !== 'string'
                || payload.codigoRastreio.trim().toUpperCase() !== this.codigoRastreio)) return false;

        const iso = typeof timestamp === 'string'
            ? /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$/i.exec(timestamp)
            : null;
        if (!iso) return false;
        const capturedAt = Date.parse(timestamp);
        const [, year, month, day, hour, minute, second, zone] = iso;
        const daysInMonth = new Date(Date.UTC(Number(year), Number(month), 0)).getUTCDate();
        if (!Number.isFinite(capturedAt) || Number(month) < 1 || Number(month) > 12
            || Number(day) < 1 || Number(day) > daysInMonth || Number(hour) > 23
            || Number(minute) > 59 || Number(second) > 59
            || (zone !== 'Z' && zone !== 'z' && (Number(zone.slice(1, 3)) > 23 || Number(zone.slice(4)) > 59))
            || capturedAt > Date.now() + 60000
            || (Number.isFinite(this.lastPositionTimestamp) && capturedAt < this.lastPositionTimestamp)) return false;

        const speed = Number.isFinite(payload.speedKmH) && payload.speedKmH >= 0 ? payload.speedKmH : undefined;
        const heading = Number.isFinite(payload.heading) ? ((payload.heading % 360) + 360) % 360 : undefined;
        this.positionIsHistorical = (this.demoMode && !isSimulated) || Date.now() - capturedAt > 120000 || (!isSimulated && (payload.captureTimeKnown === false || accuracyMeters == null || accuracyMeters > 50));
        if (this.updateVehiclePosition(latitude, longitude, speed, heading) === false) return false;
        if (speed === undefined && this.speedElem) this.speedElem.textContent = '—';

        this.lastPositionTimestamp = capturedAt;
        // Simulation always stays visibly simulated, even if a caller passes localOnly.
        this.lastPositionSource = isSimulated ? 'simulated' : payload.captureTimeKnown === false ? 'legacy' : options.localOnly === true ? 'local' : 'real';
        if (!this.demoMode && this.routeCoordinates.length < 2 && this.maneuverInstruction) {
            this.maneuverInstruction.textContent = 'Sem instruções de navegação. Confirme o trajeto com a operação.';
        }
        this.updatePositionStatus();
        return true;
    }

    updatePositionStatus(now = Date.now()) {
        if (!this.positionSourceBadge) return;
        let state = 'awaiting';
        let label = 'Aguardando posição GPS';
        let stale=false;
        const ageElement=document.getElementById('positionAge');
        if (Number.isFinite(this.lastPositionTimestamp) && this.lastPositionSource) {
            const time = new Date(this.lastPositionTimestamp).toLocaleTimeString('pt-BR', {
                hour: '2-digit', minute: '2-digit', second: '2-digit'
            });
            stale = now - this.lastPositionTimestamp > 120000;
            if(ageElement){
                const age=Math.max(0,Math.floor((now-this.lastPositionTimestamp)/1000));
                ageElement.textContent=this.lastPositionSource==='legacy'?'Captura desconhecida':`${stale?'Sinal desatualizado · ':''}há ${age<60?age+' s':Math.floor(age/60)+' min'}`;
                if(this.lastPositionSource!=='legacy')ageElement.setAttribute('datetime',new Date(this.lastPositionTimestamp).toISOString());
            }
            if (this.lastPositionSource === 'simulated') {
                state = 'simulated';
                label = 'SIMULAÇÃO — POSIÇÃO FICTÍCIA';
            } else if (this.lastPositionSource === 'local') {
                state = stale ? 'stale' : 'local';
                label = `${stale ? 'GPS do aparelho desatualizado' : 'GPS do aparelho'} · ${time}`;
            } else {
                state = stale ? 'stale' : 'received';
                label = `${stale ? 'GPS desatualizado' : 'GPS recebido'} · ${time}`;
            }
        }
        if (this.lastPositionSource === 'legacy' && state !== 'awaiting') {
            state = 'unknown-age'; label = 'Telemetria recebida · captura não informada';
        }
        this.positionSourceBadge.textContent = label;
        this.positionSourceBadge.dataset.state = state;
        this.positionSourceBadge.classList.toggle('text-warning', state === 'simulated' || state === 'stale' || state === 'unknown-age');
        this.positionSourceBadge.classList.toggle('text-success', state === 'received');
        if (stale || state === 'stale' || state === 'unknown-age') {
            this.positionIsHistorical = true;
            this.updateArrivalEstimate(null);
            if (this.speedElem) this.speedElem.textContent = '—';
        }
    }

    // Display only the received coordinate. A straight animation between sparse
    // fixes cuts across bends and invents positions unsupported by GPS evidence.
    updateVehiclePosition(lat, lng, speedKmH, heading) {
        if (!Number.isFinite(lat) || lat < -90 || lat > 90
            || !Number.isFinite(lng) || lng < -180 || lng > 180) return false;
        const firstPosition = !this.currentLatLng;
        this.currentLatLng = L.latLng(lat, lng);
        if (Number.isFinite(heading)) this.currentHeading = heading;
        if (firstPosition && typeof this.map?.setView === 'function') {
            this.map.setView(this.currentLatLng, 15, { animate: false });
        } else if (this.followVehicle && typeof this.map?.panTo === 'function') {
            this.map.panTo(this.currentLatLng, { animate: true, duration: 0.4 });
        }
        if (this.speedElem && speedKmH !== undefined) this.speedElem.textContent = Math.round(speedKmH);
        if (!this.vehicleMarker) this.setupVehicleMarker([lat, lng]);
        else this.vehicleMarker.setLatLng(this.currentLatLng);
        const icon = document.getElementById('cockpitVehicleMarker');
        if (icon) icon.style.transform = `rotate(${this.currentHeading.toFixed(1)}deg)`;
        const container = this.map?.getContainer?.();
        if (container?.dataset && (container.dataset.mapStatus === 'awaiting'
            || /^Aguardando uma posição GPS/i.test(container.dataset.mapStatus || ''))) {
            delete container.dataset.mapStatus;
        }
        this.updateRouteProgress(lat, lng);
        this.updateNavigationGuidance(lat, lng);
        return true;
    }

    suspendNavigation(state='SUSPENSA') {
        this.navigationAllowed=false;this.demoRouteAvailable=false;this.updateArrivalEstimate(null);
        if(this.fuelElem)this.fuelElem.textContent='—';
        if(this.distElem)this.distElem.textContent='—';
        if(this.maneuverInstruction)this.maneuverInstruction.textContent='Confirme o trajeto com a operação.';
        const badge=document.getElementById('routeTrustBadge');if(badge){badge.textContent=(state==='SUSPENSA'?(badge.dataset.confidence||'NÃO VERIFICADA'):state)+' · orientação suspensa';badge.dataset.navigationAllowed='false';}
    }
    updateRouteProgress(lat, lng) {
        if(!(this.navigationAllowed || this.demoMode && this.demoRouteAvailable)||Number.isFinite(this.navigationValidUntil)&&Date.now()>this.navigationValidUntil){this.suspendNavigation();this.updateArrivalEstimate(null);if(this.distElem)this.distElem.textContent='—';return;}
        if (this.positionIsHistorical) {
            this.updateArrivalEstimate(null);
            if (this.distElem) this.distElem.textContent = '—';
            return;
        }
        if (!this.routeCoordinates || this.routeCoordinates.length < 2 || this.totalRouteMeters <= 0) return;

        // Display-only projection over each segment, not GPS map matching.
        // Keep the vehicle at its observed coordinate, including off-route fixes.
        const radians = Math.PI / 180;
        const metersPerDegree = 6371000 * radians;
        const longitudeScale = Math.cos(lat * radians) * metersPerDegree;
        const wrapLongitude = value => ((value + 540) % 360) - 180;
        let nearestDistance = Number.POSITIVE_INFINITY;
        let alongRoute = this.progressMeters;
        for (let i = 1; i < this.routeCoordinates.length; i++) {
            const a = this.routeCoordinates[i - 1];
            const b = this.routeCoordinates[i];
            if (!a.every(Number.isFinite) || !b.every(Number.isFinite)
                || !Number.isFinite(this.routeCumulativeMeters[i])) continue;
            const ax = wrapLongitude(a[1] - lng) * longitudeScale;
            const ay = (a[0] - lat) * metersPerDegree;
            const dx = wrapLongitude(b[1] - a[1]) * longitudeScale;
            const dy = (b[0] - a[0]) * metersPerDegree;
            const squaredLength = dx * dx + dy * dy;
            const fraction = squaredLength > 0
                ? Math.max(0, Math.min(1, -(ax * dx + ay * dy) / squaredLength)) : 0;
            const distance = Math.hypot(ax + fraction * dx, ay + fraction * dy);
            if (distance < nearestDistance) {
                nearestDistance = distance;
                alongRoute = this.routeCumulativeMeters[i - 1]
                    + fraction * (this.routeCumulativeMeters[i] - this.routeCumulativeMeters[i - 1]);
            }
        }

        // Outside this display tolerance, progress and estimates must wait for a
        // valid route again. Authoritative deviation/recalculation stays on the server.
        this.routeProjectionOffRoute = nearestDistance > 100;
        const mapContainer = this.map?.getContainer?.();
        if (mapContainer?.dataset) mapContainer.dataset.routeState = this.routeProjectionOffRoute ? 'off-route' : 'on-route';
        const routeStatus = document.getElementById('routePositionStatus');
        if (routeStatus) {
            routeStatus.hidden = !this.routeProjectionOffRoute;
            routeStatus.textContent = this.routeProjectionOffRoute
                ? 'GPS fora do traçado planejado. Distância e chegada aguardam uma rota válida.' : '';
        }
        if (this.routeProjectionOffRoute) {
            this.updateArrivalEstimate(null);
            if (this.distElem) this.distElem.textContent = '—';
            if (this.fuelElem) this.fuelElem.textContent = '—';
            return;
        }
        this.progressMeters = Math.min(this.totalRouteMeters, Math.max(this.progressMeters, alongRoute));
        const progress = this.progressMeters / this.totalRouteMeters;
        const remainingKm = (this.totalDistKm * (1 - progress));
        if (this.progressBar) {
            const percent = Math.max(0, Math.min(100, Math.round(progress * 100)));
            this.progressBar.style.width = `${percent}%`;
            this.progressBar.setAttribute('aria-valuenow', String(percent));
        }
        if (this.distElem) this.distElem.textContent = remainingKm.toFixed(1);
        if (this.fuelElem) this.fuelElem.textContent = (this.totalFuelLiters * (1 - progress)).toFixed(1);

        this.updateArrivalEstimate(this.tempoBaseMinutos > 0
            ? Math.max(0, this.tempoBaseMinutos * (1 - progress)) : null);
    }

    updateArrivalEstimate(remainingMinutes) {
        if (!Number.isFinite(remainingMinutes) || remainingMinutes < 0) {
            if (this.etaMinutesElem) this.etaMinutesElem.textContent = 'Sem estimativa';
            if (this.etaElem) this.etaElem.textContent = '--:--';
            return;
        }
        if (this.etaMinutesElem) this.etaMinutesElem.textContent = `${remainingMinutes > 0 && remainingMinutes < 1 ? '<1' : Math.ceil(remainingMinutes - 1e-9)} min estimados`;
        if (this.etaElem) {
            const eta = new Date(Date.now() + remainingMinutes * 60000);
            this.etaElem.textContent = `${String(eta.getHours()).padStart(2, '0')}:${String(eta.getMinutes()).padStart(2, '0')}`;
        }
    }

    updateNavigationGuidance(lat, lng) {
        if(!(this.navigationAllowed || this.demoMode && this.demoRouteAvailable)||Number.isFinite(this.navigationValidUntil)&&Date.now()>this.navigationValidUntil){this.suspendNavigation();return;}
        if (this.routeProjectionOffRoute || this.positionIsHistorical) return;
        if (!this.steps || this.steps.length === 0) return;

        let nearestIndex = -1;
        let nearestDistance = Number.POSITIVE_INFINITY;
        for (let i = this.currentStepIndex; i < this.steps.length; i++) {
            const step = this.steps[i];
            const stepLat = step.Latitude ?? step.latitude;
            const stepLng = step.Longitude ?? step.longitude;
            if (!Number.isFinite(stepLat) || !Number.isFinite(stepLng)) continue;

            const distance = L.latLng(lat, lng).distanceTo(L.latLng(stepLat, stepLng));
            if (distance < nearestDistance) {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }
        if (nearestIndex < 0) return;

        const currentStep = this.steps[nearestIndex];
        const type = (currentStep.ManobraTipo ?? currentStep.manobraTipo ?? '').toLowerCase();
        if (type === 'depart' && nearestIndex + 1 < this.steps.length) {
            nearestIndex++;
        } else if (nearestDistance < 25 && nearestIndex + 1 < this.steps.length) {
            nearestIndex++;
        }
        this.currentStepIndex = nearestIndex;

        const nextStep = this.steps[nearestIndex];
        const nextLat = nextStep.Latitude ?? nextStep.latitude;
        const nextLng = nextStep.Longitude ?? nextStep.longitude;
        const distanceMeters = Number.isFinite(nextLat) && Number.isFinite(nextLng)
            ? L.latLng(lat, lng).distanceTo(L.latLng(nextLat, nextLng))
            : 0;
        const roadName = nextStep.NomeVia ?? nextStep.nomeVia;
        const instruction = nextStep.Instrucao ?? nextStep.instrucao;
        const icon = nextStep.IconeBootstrap ?? nextStep.iconeBootstrap;
        if (this.maneuverRoad) this.maneuverRoad.textContent = roadName || 'Via sem nome';
        if (this.maneuverInstruction) this.maneuverInstruction.textContent = instruction || 'Continue na rota';
        if (this.maneuverIcon) this.maneuverIcon.className = `bi ${icon || 'bi-arrow-up'}`;
        if (this.maneuverDist) {
            this.maneuverDist.textContent = distanceMeters >= 1000
                ? `Em ${(distanceMeters / 1000).toFixed(1)} km`
                : `Em ${Math.max(0, Math.round(distanceMeters))} m`;
        }
    }

    // Emissão de telemetria contínua pelo condutor para o backend C# (RouteDeviationEngine)
    emitTelemetry(lat, lng, speedKmH, heading, accuracyMeters = null, isSimulated = false, capturedAt = new Date().toISOString()) {
        const now = Date.now();
        if (!isSimulated && window.mglGpsOutbox) {
            if (now - this.lastTelemetryEmitTime < (window.mglGpsOutbox.intervalMs?.() ?? 5000)) return;
            this.lastTelemetryEmitTime = now;
            return window.mglGpsOutbox.capture({ latitude: lat, longitude: lng, velocidadeKmH: speedKmH,
                direcao: heading, precisaoMetros: accuracyMeters, capturadoEmUtc: capturedAt })
                .catch(error => this.showToastAlert(error.message, 'bi-cloud-slash', 'var(--mgl-error)'));
        }
        if (now - this.lastTelemetryEmitTime < 2000) return; // Throttle 2s
        this.lastTelemetryEmitTime = now;

        if (this.hubConnection && this.hubConnection.state === signalR.HubConnectionState.Connected) {
            this.hubConnection.invoke(
                'SendVehicleTelemetry',
                this.codigoRastreio,
                lat,
                lng,
                speedKmH || 0,
                heading || 0,
                Number.isFinite(accuracyMeters) ? accuracyMeters : null,
                isSimulated)
                .catch(err => console.warn('Falha no streaming de telemetria:', err));
        }
    }

    setFollowVehicle(enabled) {
        this.followVehicle = Boolean(enabled);
        document.getElementById('btnFollowPosition')?.setAttribute('aria-pressed',String(this.followVehicle));
        if (this.followVehicle && this.currentLatLng && this.map && typeof this.map.setView === 'function') {
            const zoom = typeof this.map.getZoom === 'function' ? this.map.getZoom() : 15;
            this.map.setView(this.currentLatLng, Number.isFinite(zoom) ? Math.max(zoom, 15) : 15, { animate: true });
        }
    }

    // Sincronizar fila de GPS offline quando voltar à conexão
    syncOfflineGPS() {
        if (this.gpsOfflineQueue.length > 0 && this.isOnline) {
            // The legacy live method has no capture-time/idempotency contract.
            // Preserve old samples instead of publishing them as current GPS.
            this.showToastAlert('Há posições antigas pendentes. O envio de histórico GPS ainda não está disponível.', 'bi-cloud-slash', '#FFB020');
        }
    }

    // Aplicação da nova polilinha após recálculo OSRM com efeito de Cross-Fade
    applyRouteReroute(payload) {
        console.debug('Atualização de rota recebida.');

        // 1. Esmaece a rota anterior (Cross-Fade)
        if (this.routePolyline) {
            if (this.oldRoutePolyline) this.map.removeLayer(this.oldRoutePolyline);
            this.oldRoutePolyline = this.routePolyline;
            this.oldRoutePolyline.setStyle({
                color: '#94A3B8',
                dashArray: '6, 8',
                opacity: 0.45,
                weight: 4
            });
        }
        if (this.routePolylineOutline) {
            this.map.removeLayer(this.routePolylineOutline);
        }

        const newCoords = (payload.trajetoCoordenadas || []).map(c => [
            c.lat !== undefined ? c.lat : c.Latitude,
            c.lng !== undefined ? c.lng : c.Longitude
        ]);

        if (newCoords.length === 0) return;

        // 2. Cria nova rota com fade-in
        this.routePolylineOutline = L.polyline(newCoords, {
            color: '#0A1B2B',
            weight: 10,
            opacity: 0.85
        }).addTo(this.map);

        this.routePolyline = L.polyline(newCoords, {
            color: '#00C9B7',
            weight: 6,
            opacity: 0
        }).addTo(this.map);

        this.bringOverlaysToFront();

        let op = 0;
        const fadeIn = setInterval(() => {
            op += 0.15;
            if (op >= 1.0) {
                this.routePolyline.setStyle({ opacity: 1.0 });
                clearInterval(fadeIn);
                if (this.oldRoutePolyline) {
                    setTimeout(() => {
                        if (this.oldRoutePolyline) {
                            this.map.removeLayer(this.oldRoutePolyline);
                            this.oldRoutePolyline = null;
                        }
                    }, 2500);
                }
            } else {
                this.routePolyline.setStyle({ opacity: op });
            }
        }, 30);

        // 3. Atualiza métricas HUD e passos Turn-by-Turn
        if (this.distElem && payload.distanciaKm) {
            this.distElem.textContent = Number(payload.distanciaKm).toFixed(1);
        }
        this.tempoBaseMinutos = Number.isFinite(payload.tempoEstimadoMinutos) && payload.tempoEstimadoMinutos > 0
            ? payload.tempoEstimadoMinutos : 0;
        this.updateArrivalEstimate(this.tempoBaseMinutos > 0 ? this.tempoBaseMinutos : null);
        if (payload.passosNavegacao && payload.passosNavegacao.length > 0) {
            this.steps = payload.passosNavegacao;
            this.currentStepIndex = 0;
            const p = this.steps[0];
            if (this.maneuverRoad) this.maneuverRoad.textContent = p.nomeVia || p.NomeVia || 'Via Conectada';
            if (this.maneuverInstruction) this.maneuverInstruction.textContent = p.instrucao || p.Instrucao || 'Continue na rota';
            if (this.maneuverIcon) this.maneuverIcon.className = `bi ${p.iconeBootstrap || p.IconeBootstrap || 'bi-arrow-up'}`;
        }

        // 4. Exibe notificação imersiva de recálculo no HUD
        this.routeCoordinates = newCoords;
        this.initialCoordinates = newCoords;
        this.totalDistKm = Number(payload.distanciaKm) || this.totalDistKm;
        this.routeCumulativeMeters = [0];
        this.totalRouteMeters = 0;
        for (let i = 1; i < newCoords.length; i++) {
            this.totalRouteMeters += L.latLng(newCoords[i - 1]).distanceTo(L.latLng(newCoords[i]));
            this.routeCumulativeMeters.push(this.totalRouteMeters);
        }
        this.progressMeters = 0;
        if (this.currentLatLng) {
            this.updateRouteProgress(this.currentLatLng.lat, this.currentLatLng.lng);
            this.updateNavigationGuidance(this.currentLatLng.lat, this.currentLatLng.lng);
        }
        this.fitPlannedRoute();
        this.showToastAlert('Nova rota recalculada a partir da posição atual.', 'bi-sign-turn-right-fill', '#FFB020');
    }

    // Matchmaking da Camada de Tráfego: Atualiza o ETA via API sem mexer no traçado OSRM
    setupTrafficLayerMatch() {
        if(this.demoMode || this.navigationAllowed===false)return;
        if (!this.entregaId || this.initialCoordinates.length < 2) return;
        this.fetchTrafficConditions();
        this.trafficTimer = setInterval(() => this.fetchTrafficConditions(), 300000);
    }

    async fetchTrafficConditions() {
        if (this.demoMode || !this.entregaId || this.initialCoordinates.length < 2 || !this.currentLatLng
            || this.tempoBaseMinutos <= 0 || this.routeProjectionOffRoute) return;

        try {
            const lat = this.currentLatLng.lat;
            const lng = this.currentLatLng.lng;
            const url = `/api/entregas/${this.entregaId}/trafego-eta?latAtual=${lat}&lngAtual=${lng}&tempoBaseMinutos=${this.tempoBaseMinutos}`;

            const res = await fetch(url);
            if (!res.ok) return;

            const data = await res.json();
            if (this.destroyed || this.routeProjectionOffRoute) return;

            // Atualiza badge de tráfego
            if (this.trafficBadge) {
                this.trafficBadge.textContent = data.dadosAoVivoDisponiveis ? data.condicao : 'Sem trânsito ao vivo';
                this.trafficBadge.style.backgroundColor = data.dadosAoVivoDisponiveis ? (data.corHex || '#12C982') : '#64748B';
                this.trafficBadge.title = `${data.descricao} (${data.fonte})`;
            }

            // Atualiza ETA no HUD somando eventuais atrasos da malha
            if (Number.isFinite(data.tempoFinalMinutos) && data.tempoFinalMinutos > 0) {
                this.updateArrivalEstimate(data.tempoFinalMinutos);
                if (this.etaMinutesElem && Number.isFinite(data.atrasoMinutos) && data.atrasoMinutos > 0) {
                    this.etaMinutesElem.textContent += ` · +${data.atrasoMinutos} min de atraso estimado`;
                }
            }
        } catch (e) {
            console.warn('Erro ao obter camada de tráfego:', e);
        }
    }

    showToastAlert(msg, iconClass = 'bi-broadcast', iconColor = 'var(--mgl-warning)') {
        const toast = document.createElement('div');
        toast.className = 'mgl-hud-toast';
        toast.setAttribute('role', 'status');
        toast.style.borderColor = iconColor;
        const heading = document.createElement('div');
        heading.className = 'd-flex align-items-center justify-content-center gap-2 fw-bold mb-1';
        const icon = document.createElement('i');
        icon.className = `bi ${/^bi-[a-z0-9-]+$/.test(iconClass) ? iconClass : 'bi-broadcast'}`;
        icon.setAttribute('aria-hidden', 'true');
        icon.style.color = iconColor;
        const title = document.createElement('span');
        title.textContent = 'Atualização da viagem';
        heading.append(icon, title);
        const message = document.createElement('p');
        message.className = 'small mb-0';
        message.textContent = String(msg ?? '');
        toast.append(heading, message);
        document.body.appendChild(toast);
        setTimeout(() => {
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 400);
        }, 3200);
    }

    completeDelivery(status = 'Entregue') {
        this.destroy();
        this.updateConnectionStatus(status === 'Cancelada' ? 'cancelled' : 'completed');
        if (this.positionSourceBadge) {
            this.positionSourceBadge.dataset.state = 'completed';
            this.positionSourceBadge.textContent = Number.isFinite(this.lastPositionTimestamp)
                ? 'GPS encerrado · última posição preservada' : 'GPS encerrado · nenhuma posição recebida';
        }
    }

    destroy() {
        if (this.destroyed) return;
        this.destroyed = true;
        if (this.trafficTimer) clearInterval(this.trafficTimer);
        if (this.positionFreshnessTimer) clearInterval(this.positionFreshnessTimer);
        if(this.mapDragRegistered)this.map.off?.('dragstart',this.mapDragHandler);
        this.mapResizeObserver?.disconnect();
        if (this.mapResizeFrame) cancelAnimationFrame(this.mapResizeFrame);
        window.removeEventListener('online', this.onlineHandler);
        window.removeEventListener('offline', this.offlineHandler);
        window.removeEventListener('mgl:gps-confirmed', this.gpsReceiptHandler);
        if (this.map && typeof this.map.off === 'function' && this.baseLayerChangeHandler) {
            this.map.off('baselayerchange', this.baseLayerChangeHandler);
        }
        if (this.hubConnection) {
            this.hubConnection.invoke('LeaveLiveTracking', this.codigoRastreio)
                .catch(() => {})
                .finally(() => this.hubConnection.stop())
                .catch(() => {});
        }
    }
}
