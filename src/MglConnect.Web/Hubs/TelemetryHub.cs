using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MglConnect.Web.Data;
using MglConnect.Web.Services;

namespace MglConnect.Web.Hubs
{
    [Authorize]
    public class TelemetryHub : Hub
    {
        private readonly IRouteDeviationEngine _deviationEngine;
        private readonly ITruckRoutingService _routingService;
        private readonly ILogger<TelemetryHub> _logger;
        private readonly MglDbContext _context;
        private readonly IDriverShiftService _shiftService;

        public TelemetryHub(
            IRouteDeviationEngine deviationEngine,
            ITruckRoutingService routingService,
            ILogger<TelemetryHub> logger,
            MglDbContext context,
            IDriverShiftService shiftService)
        {
            _deviationEngine = deviationEngine;
            _routingService = routingService;
            _logger = logger;
            _context = context;
            _shiftService = shiftService;
        }

        public async Task JoinLiveTracking(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio))
                throw new HubException("Código de rastreio inválido.");

            var key = codigoRastreio.Trim().ToUpperInvariant();
            if (!await PodeConsultarEntregaAsync(key))
                throw new HubException("Você não tem acesso ao rastreamento desta entrega.");

            await Groups.AddToGroupAsync(Context.ConnectionId, key);
            _logger.LogInformation("Conexão {ConnectionId} ingressou em um grupo de telemetria autorizado.", Context.ConnectionId);
        }

        public async Task LeaveLiveTracking(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio)) return;

            var key = codigoRastreio.Trim().ToUpperInvariant();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        }

        public async Task SendVehicleTelemetry(
            string codigoRastreio,
            double latitude,
            double longitude,
            double speedKmH,
            double heading,
            double? accuracyMeters = null,
            bool isSimulated = false)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio))
                throw new HubException("Código de rastreio inválido.");

            var key = codigoRastreio.Trim().ToUpperInvariant();
            if (!await PodeOperarEntregaAsync(key))
                throw new HubException("Somente o motorista atribuído ou um administrador pode enviar telemetria.");
            if (isSimulated && Context.User?.IsInRole("Admin") != true)
                throw new HubException("Somente um administrador pode enviar telemetria simulada.");
            if (!double.IsFinite(latitude) || latitude is < -90 or > 90
                || !double.IsFinite(longitude) || longitude is < -180 or > 180
                || !double.IsFinite(speedKmH) || speedKmH is < 0 or > 200
                || !double.IsFinite(heading) || heading is < 0 or >= 360
                || (accuracyMeters.HasValue
                    && (!double.IsFinite(accuracyMeters.Value) || accuracyMeters.Value < 0 || accuracyMeters.Value > 1000)))
                throw new HubException("Dados de telemetria inválidos.");

            var devResult = _deviationEngine.EvaluatePosition(
                key, latitude, longitude, speedKmH, heading, accuracyMeters);

            await Clients.Caller.SendAsync("ReceiveTelemetryMetrics", new
            {
                gpsDataAccepted = devResult.GpsDataAccepted,
                gpsAccuracyMeters = accuracyMeters,
                motivo = devResult.Motivo,
                isOffRoute = devResult.IsOffRoute,
                crossTrackDistanceMeters = devResult.CrossTrackDistanceMeters,
                alongTrackDistanceMeters = devResult.AlongTrackDistanceMeters,
                azimuthDivergenceDegrees = devResult.AzimuthDivergenceDegrees,
                offRouteFramesCount = devResult.OffRouteFramesCount,
                activeSegmentIndex = devResult.ActiveSegmentIndex
            });

            if (!devResult.GpsDataAccepted)
            {
                return;
            }

            var payload = new
            {
                codigoRastreio = key,
                latitude,
                longitude,
                speedKmH = Math.Round(speedKmH, 1),
                heading = Math.Round(heading, 1),
                accuracyMeters,
                isSimulated,
                timestamp = DateTime.UtcNow
            };

            // 1. Notifica central e portal do cliente em tempo real
            await Clients.Group(key).SendAsync("ReceiveVehiclePosition", payload);

            // 3. Gatilho de Rerouting Automatico OSRM
            if (devResult.NeedsReroute)
            {
                var state = _deviationEngine.GetState(key);
                _logger.LogWarning(
                    "Desvio confirmado para a entrega {EntregaId}: XTE={Xte}m por {Frames} amostras.",
                    state?.EntregaId,
                    devResult.CrossTrackDistanceMeters,
                    devResult.OffRouteFramesCount);

                try
                {
                    var perfilVeiculo = state?.PerfilVeiculo ?? "Pesado";
                    var novoPlano = await _routingService.RecalcularRotaDinamicaAsync(
                        key, latitude, longitude, perfilVeiculo);
                    if (novoPlano == null)
                    {
                        await EnviarFalhaDeRecálculoAsync(key,
                            "Não foi possível validar uma rota alternativa. Pare em local seguro e solicite revisão do despachante.");
                        return;
                    }

                    var reroutePayload = new
                    {
                        codigoRastreio = key,
                        origem = novoPlano.Origem,
                        destino = novoPlano.Destino,
                        distanciaKm = novoPlano.DistanciaKm,
                        tempoEstimadoMinutos = novoPlano.TempoEstimadoMinutos,
                        trajetoCoordenadas = novoPlano.TrajetoCoordenadas.Select(c => new { lat = c.Latitude, lng = c.Longitude }).ToList(),
                        passosNavegacao = novoPlano.PassosNavegacao,
                        timestamp = DateTime.UtcNow,
                        isSimulated = false,
                        verifiedForVehicle = novoPlano.RotaHomologadaParaVeiculo,
                        mensagem = novoPlano.AvisoRota
                    };

                    await Clients.Group(key).SendAsync("ReceiveRouteRecalculated", reroutePayload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Falha ao recalcular rota da entrega {EntregaId} ({ErrorType}).",
                        state?.EntregaId,
                        ex.GetType().Name);
                    await EnviarFalhaDeRecálculoAsync(key,
                        "O serviço de roteamento está indisponível. Mantenha a rota original e solicite revisão do despachante.");
                }
            }
        }

        // Metodo utilitario para demostracao e homologacao de desvio forcado
        public async Task SimulateDeviationTrigger(string codigoRastreio, double deltaLat, double deltaLng)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio))
                throw new HubException("Código de rastreio inválido.");

            var key = codigoRastreio.Trim().ToUpperInvariant();
            if (Context.User?.IsInRole("Admin") != true)
                throw new HubException("Somente um administrador pode executar simulações.");
            if (!double.IsFinite(deltaLat) || !double.IsFinite(deltaLng)
                || Math.Abs(deltaLat) > 1 || Math.Abs(deltaLng) > 1)
                throw new HubException("Deslocamento de simulação inválido.");

            var state = _deviationEngine.GetState(key);
            if (state == null || !await PodeOperarEntregaAsync(key))
                throw new HubException("A entrega não está disponível para simulação.");

            double fakeLat = state.LastKnownLatitude + deltaLat;
            double fakeLng = state.LastKnownLongitude + deltaLng;
            double fakeSpeed = 55.0;
            double fakeHeading = (state.LastHeading + 60.0) % 360.0;

            // Simula 3 frames consecutivos para forcar o disparo do desvio
            for (int i = 0; i < 3; i++)
            {
                await SendVehicleTelemetry(
                    key,
                    fakeLat + (i * 0.0001),
                    fakeLng + (i * 0.0001),
                    fakeSpeed,
                    fakeHeading,
                    isSimulated: true);
            }
        }

        private Task EnviarFalhaDeRecálculoAsync(string codigoRastreio, string mensagem) =>
            Clients.Group(codigoRastreio).SendAsync("ReceiveRouteRecalculationFailed", new
            {
                codigoRastreio,
                mensagem,
                revisaoDespachanteNecessaria = true,
                timestamp = DateTime.UtcNow
            });

        private async Task<bool> PodeConsultarEntregaAsync(string codigoRastreio)
        {
            if (!int.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return false;

            if (Context.User?.IsInRole("Admin") == true)
                return await _context.Usuarios.AnyAsync(user =>
                    user.Id == userId && user.Ativo
                    && (user.Perfil == "Admin" || user.Perfil == "Administrador"))
                    && await _context.Entregas.AnyAsync(e => e.CodigoRastreio == codigoRastreio);

            if (Context.User?.IsInRole("Motorista") == true)
            {
                var deliveryId = await _context.Entregas
                    .Where(e => e.CodigoRastreio == codigoRastreio
                        && e.MotoristaId == userId
                        && e.AtribuicaoAtiva)
                    .Select(e => (int?)e.Id)
                    .FirstOrDefaultAsync();
                return deliveryId.HasValue
                    && await _shiftService.HasActiveShiftAsync(userId, deliveryId.Value);
            }

            if (Context.User?.IsInRole("Cliente") == true
                || Context.User?.IsInRole("Intermediador") == true)
            {
                var expectedProfile = Context.User.IsInRole("Cliente") ? "Cliente" : "Intermediador";
                var clienteId = await _context.Usuarios
                    .Where(u => u.Id == userId && u.Ativo
                        && u.Perfil == expectedProfile)
                    .Select(u => u.ClienteId)
                    .FirstOrDefaultAsync();
                return clienteId.HasValue && await _context.Entregas.AnyAsync(
                    e => e.CodigoRastreio == codigoRastreio
                        && e.Pedido != null
                        && e.Pedido.ClienteId == clienteId.Value);
            }

            return false;
        }

        private async Task<bool> PodeOperarEntregaAsync(string codigoRastreio)
        {
            if (!int.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return false;

            if (Context.User?.IsInRole("Admin") == true)
                return await _context.Usuarios.AnyAsync(user =>
                    user.Id == userId && user.Ativo
                    && (user.Perfil == "Admin" || user.Perfil == "Administrador"))
                    && await _context.Entregas.AnyAsync(e => e.CodigoRastreio == codigoRastreio);

            if (Context.User?.IsInRole("Motorista") != true)
                return false;

            var deliveryId = await _context.Entregas
                .Where(e => e.CodigoRastreio == codigoRastreio
                    && e.MotoristaId == userId
                    && e.AtribuicaoAtiva
                    && e.StatusEntrega == "Em transporte")
                .Select(e => (int?)e.Id)
                .FirstOrDefaultAsync();
            return deliveryId.HasValue
                && await _shiftService.HasActiveShiftAsync(userId, deliveryId.Value);
        }
    }
}