/**
 * M.G.L Connect - Módulo de Rastreamento em Tempo Real & Suavização Cartográfica
 * Arquitetura: OpenStreetMap / Esri + rotas OSRM e telemetria SignalR
 * Offline-First: Fila local de GPS e POD salva no IndexedDB, sincronizada via Service Worker.
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
        this.map = options.map;
        this.entregaId = options.entregaId;
        this.codigoRastreio = (options.codigoRastreio || '').trim().toUpperCase();
        this.initialCoordinates = options.coordinates || [];
        this.routeCoordinates = this.initialCoordinates;
        this.steps = options.steps || [];
        this.totalDistKm = options.totalDistKm || 0;
        this.totalFuelLiters = options.totalFuelLiters || 0;
        this.tempoBaseMinutos = options.tempoBaseMinutos || 120;
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
        this.followVehicle = false;

        // Camadas Leaflet
        this.routePolylineOutline = null;
        this.routePolyline = null;
        this.oldRoutePolyline = null;
        this.vehicleMarker = null;
        this.destinationMarker = null;

        // Estado de Animação e Interpolação do Veículo
        this.currentLatLng = null;
        this.targetLatLng = null;
        this.currentHeading = 0;
        this.targetHeading = 0;
        this.animFrameId = null;
        this.animStartTime = 0;
        this.animDuration = 1800; // 1.8s de transição suave
        this.startLatLng = null;

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
        window.addEventListener('online', () => {
            this.isOnline = true;
            this.syncOfflineGPS();
        });
        window.addEventListener('offline', () => {
            this.isOnline = false;
        });

        this.init();

        // Sincronizar dados offline ao retomar conexão (Service Worker already listens for 'sync' event,
        // mas também podemos fazer polling imediato quando o navegador volta online)
        if (this.isOnline) {
            this.syncOfflineGPS();
        }
    }

    init() {
        this.setupRouteLayers(this.initialCoordinates);
        this.setupVehicleMarker();
        this.setupSignalR();
        this.setupTrafficLayerMatch();
        this.setupLayerListeners();
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

        this.map.fitBounds(this.routePolyline.getBounds(), { padding: [50, 50] });

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

    setupVehicleMarker(startPoint = this.initialCoordinates[0]) {
        if (!startPoint) return;
        this.currentLatLng = L.latLng(startPoint[0], startPoint[1]);
        this.startLatLng = this.currentLatLng;

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
        if (!this.map) return;
        this.map.on('baselayerchange', (e) => {
            this.activeLayerName = e.name;
            this.bringOverlaysToFront();
        });
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

        this.showToastAlert(`Camada ativa: <strong>${nextKey}</strong>`, 'bi-layers-fill', '#00C9B7');
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
            return;
        }

        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl('/telemetryHub')
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .build();

        // 1. Recebimento de Coordenadas em Tempo Real (outros dispositivos / central)
        this.hubConnection.on('ReceiveVehiclePosition', (payload) => {
            if (payload.codigoRastreio === this.codigoRastreio) {
                if (this.positionSourceBadge) {
                    this.positionSourceBadge.textContent = payload.isSimulated
                        ? 'SIMULAÇÃO — POSIÇÃO FICTÍCIA'
                        : 'POSIÇÃO GPS';
                    this.positionSourceBadge.classList.toggle('text-warning', Boolean(payload.isSimulated));
                    this.positionSourceBadge.classList.toggle('text-success', !payload.isSimulated);
                }
                this.updateVehiclePosition(payload.latitude, payload.longitude, payload.speedKmH, payload.heading);
            }
        });

        // 2. Recebimento de Recálculo Dinâmico de Rota (Off-Route acionado no C#)
        this.hubConnection.on('ReceiveRouteRecalculated', (payload) => {
            if (payload.codigoRastreio === this.codigoRastreio) {
                this.applyRouteReroute(payload);
            }
        });

        // 3. Telemetria Diagnóstica (XTE / ATD)
        this.hubConnection.on('ReceiveTelemetryMetrics', (metrics) => {
            if (!metrics.gpsDataAccepted) {
                this.showToastAlert(metrics.motivo || 'A posição GPS foi ignorada por baixa qualidade.', 'bi-geo-alt-fill', '#FFB020');
            } else if (metrics.isOffRoute) {
                this.showToastAlert(`Desvio detectado: ${metrics.crossTrackDistanceMeters}m fora da via.`, 'bi-exclamation-triangle-fill', '#FFB020');
            }
        });

        this.hubConnection.on('ReceiveRouteRecalculationFailed', (payload) => {
            if (payload.codigoRastreio === this.codigoRastreio) {
                this.showToastAlert(payload.mensagem, 'bi-exclamation-triangle-fill', '#EF4444');
            }
        });

        const joinLiveTracking = () => this.hubConnection.invoke('JoinLiveTracking', this.codigoRastreio);
        this.hubConnection.onreconnected(joinLiveTracking);

        this.hubConnection.start()
            .then(joinLiveTracking)
            .then(() => {
                console.log('Conectado ao TelemetryHub para remessa:', this.codigoRastreio);
            })
            .catch(err => console.error('Erro ao conectar ao TelemetryHub:', err));
    }

    // Atualiza a posição do veículo com interpolação suave via requestAnimationFrame
    updateVehiclePosition(lat, lng, speedKmH, heading, snap = false) {
        this.targetLatLng = L.latLng(lat, lng);
        this.targetHeading = heading !== undefined && heading !== null ? heading : this.currentHeading;
        if (this.followVehicle) {
            this.map.panTo(this.targetLatLng, { animate: true, duration: 0.4 });
        }

        if (this.speedElem && speedKmH !== undefined) {
            this.speedElem.textContent = Math.round(speedKmH);
        }

        if (snap || !this.currentLatLng) {
            if (this.animFrameId) cancelAnimationFrame(this.animFrameId);
            this.currentLatLng = this.targetLatLng;
            this.currentHeading = this.targetHeading;
            if (!this.vehicleMarker) {
                this.setupVehicleMarker([lat, lng]);
            } else {
                this.vehicleMarker.setLatLng(this.currentLatLng);
            }
            const iconElem = document.getElementById('cockpitVehicleMarker');
            if (iconElem) iconElem.style.transform = `rotate(${this.currentHeading.toFixed(1)}deg)`;
            delete this.map.getContainer().dataset.mapStatus;
            this.updateRouteProgress(lat, lng);
            this.updateNavigationGuidance(lat, lng);
            return;
        }

        this.startLatLng = this.currentLatLng;
        this.animStartTime = performance.now();

        if (this.animFrameId) {
            cancelAnimationFrame(this.animFrameId);
        }

        this.animateStep = (timestamp) => {
            const elapsed = timestamp - this.animStartTime;
            const progress = Math.min(1.0, elapsed / this.animDuration);

            // Interpolação suave cúbica (Ease-Out)
            const easeProgress = 1 - Math.pow(1 - progress, 3);

            const curLat = this.startLatLng.lat + (this.targetLatLng.lat - this.startLatLng.lat) * easeProgress;
            const curLng = this.startLatLng.lng + (this.targetLatLng.lng - this.startLatLng.lng) * easeProgress;

            this.currentLatLng = L.latLng(curLat, curLng);
            if (this.vehicleMarker) {
                this.vehicleMarker.setLatLng(this.currentLatLng);
            }

            // Interpolação de ângulo de rotação (menor arco angular)
            const angleDiff = ((this.targetHeading - this.currentHeading + 540) % 360) - 180;
            const curHeading = this.currentHeading + (angleDiff * easeProgress);

            const iconElem = document.getElementById('cockpitVehicleMarker');
            if (iconElem) {
                iconElem.style.transform = `rotate(${curHeading.toFixed(1)}deg)`;
            }

            if (progress < 1.0) {
                this.animFrameId = requestAnimationFrame(this.animateStep);
            } else {
                this.currentHeading = this.targetHeading;
            }
        };

        this.animFrameId = requestAnimationFrame(this.animateStep);
        this.updateRouteProgress(lat, lng);
        this.updateNavigationGuidance(lat, lng);
    }

    updateRouteProgress(lat, lng) {
        if (!this.routeCoordinates || this.routeCoordinates.length < 2 || this.totalRouteMeters <= 0) return;

        const current = L.latLng(lat, lng);
        let nearestIndex = 0;
        let nearestDistance = Number.POSITIVE_INFINITY;
        for (let i = 0; i < this.routeCoordinates.length; i++) {
            const distance = current.distanceTo(L.latLng(this.routeCoordinates[i]));
            if (distance < nearestDistance) {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        this.progressMeters = Math.max(this.progressMeters, this.routeCumulativeMeters[nearestIndex]);
        const progress = this.progressMeters / this.totalRouteMeters;
        const remainingKm = (this.totalDistKm * (1 - progress));
        if (this.progressBar) this.progressBar.style.width = `${Math.round(progress * 100)}%`;
        if (this.distElem) this.distElem.textContent = remainingKm.toFixed(1);
        if (this.fuelElem) this.fuelElem.textContent = (this.totalFuelLiters * (1 - progress)).toFixed(1);

        const remainingMinutes = Math.round(this.tempoBaseMinutos * (1 - progress));
        if (this.etaMinutesElem && remainingMinutes > 0) {
            this.etaMinutesElem.textContent = `${remainingMinutes} min`;
        }
        if (this.etaElem && remainingMinutes > 0) {
            const eta = new Date(Date.now() + remainingMinutes * 60000);
            this.etaElem.textContent = `${String(eta.getHours()).padStart(2, '0')}:${String(eta.getMinutes()).padStart(2, '0')}`;
        }
    }

    updateNavigationGuidance(lat, lng) {
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
    emitTelemetry(lat, lng, speedKmH, heading, accuracyMeters = null, isSimulated = false) {
        const now = Date.now();
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
        if (this.followVehicle && this.currentLatLng) {
            this.map.setView(this.currentLatLng, Math.max(this.map.getZoom(), 15), { animate: true });
        }
    }

    // Sincronizar fila de GPS offline quando voltar à conexão
    syncOfflineGPS() {
        if (this.gpsOfflineQueue.length > 0 && this.isOnline) {
            // Enviar todas as coordenadas acumuladas offline para o backend
            this.gpsOfflineQueue.forEach(pos => {
                this.emitTelemetry(pos.lat, pos.lng, pos.speedKmH, pos.heading);
            });
            this.gpsOfflineQueue = [];
            this.showToastAlert('Dados offline sincronizados com sucesso!', 'bi-cloud-sync', '#12C982');
        }
    }

    // Aplicação da nova polilinha após recálculo OSRM com efeito de Cross-Fade
    applyRouteReroute(payload) {
        console.log('Aplicando rota recalculada:', payload);

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
        if (this.etaMinutesElem && payload.tempoEstimadoMinutos) {
            this.etaMinutesElem.textContent = `${payload.tempoEstimadoMinutos} min`;
        }
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
        this.map.fitBounds(this.routePolyline.getBounds(), { padding: [50, 50] });
        this.showToastAlert('Nova rota recalculada a partir da posição atual.', 'bi-sign-turn-right-fill', '#FFB020');
    }

    // Matchmaking da Camada de Tráfego: Atualiza o ETA via API sem mexer no traçado OSRM
    setupTrafficLayerMatch() {
        if (!this.entregaId || this.initialCoordinates.length < 2) return;
        this.fetchTrafficConditions();
        this.trafficTimer = setInterval(() => this.fetchTrafficConditions(), 300000);
    }

    async fetchTrafficConditions() {
        if (!this.entregaId || this.initialCoordinates.length < 2) return;

        try {
            const lat = this.currentLatLng ? this.currentLatLng.lat : this.initialCoordinates[0][0];
            const lng = this.currentLatLng ? this.currentLatLng.lng : this.initialCoordinates[0][1];
            const url = `/api/entregas/${this.entregaId}/trafego-eta?latAtual=${lat}&lngAtual=${lng}&tempoBaseMinutos=${this.tempoBaseMinutos}`;

            const res = await fetch(url);
            if (!res.ok) return;

            const data = await res.json();

            // Atualiza badge de tráfego
            if (this.trafficBadge) {
                this.trafficBadge.textContent = data.dadosAoVivoDisponiveis ? data.condicao : 'Sem trânsito ao vivo';
                this.trafficBadge.style.backgroundColor = data.dadosAoVivoDisponiveis ? (data.corHex || '#12C982') : '#64748B';
                this.trafficBadge.title = `${data.descricao} (${data.fonte})`;
            }

            // Atualiza ETA no HUD somando eventuais atrasos da malha
            if (this.etaMinutesElem && data.tempoFinalMinutos) {
                this.etaMinutesElem.textContent = `${data.tempoFinalMinutos} min`;
                if (data.atrasoMinutos > 0) {
                    this.etaMinutesElem.innerHTML = `${data.tempoFinalMinutos} min <span class="badge bg-danger ms-1">+${data.atrasoMinutos}m</span>`;
                }
            }

            if (this.etaElem && data.tempoFinalMinutos) {
                const agora = new Date();
                agora.setMinutes(agora.getMinutes() + data.tempoFinalMinutos);
                const h = String(agora.getHours()).padStart(2, '0');
                const m = String(agora.getMinutes()).padStart(2, '0');
                this.etaElem.textContent = `${h}:${m}`;
            }
        } catch (e) {
            console.warn('Erro ao obter camada de tráfego:', e);
        }
    }

    showToastAlert(msg, iconClass = 'bi-broadcast', iconColor = '#FFB020') {
        const toast = document.createElement('div');
        toast.className = 'position-fixed top-50 start-50 translate-middle p-3 bg-dark text-white rounded-4 border shadow-lg z-3 text-center mgl-hud-toast';
        toast.style.maxWidth = '360px';
        toast.style.backdropFilter = 'blur(12px)';
        toast.style.backgroundColor = 'rgba(10, 27, 43, 0.96)';
        toast.style.borderColor = iconColor;
        toast.style.zIndex = '9999';
        toast.innerHTML = `
            <div class="d-flex align-items-center gap-2 mb-1 justify-content-center fw-bold" style="color: ${iconColor};">
                <i class="bi ${iconClass} fs-4"></i> Telemetria M.G.L HUD
            </div>
            <div class="fs-7 text-white-50">${msg}</div>
        `;
        document.body.appendChild(toast);
        setTimeout(() => {
            toast.style.transition = 'opacity 0.4s ease';
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 400);
        }, 3200);
    }

    destroy() {
        if (this.trafficTimer) clearInterval(this.trafficTimer);
        if (this.animFrameId) cancelAnimationFrame(this.animFrameId);
        if (this.hubConnection) {
            this.hubConnection.invoke('LeaveLiveTracking', this.codigoRastreio)
                .catch(() => {})
                .finally(() => this.hubConnection.stop());
        }
    }
}
