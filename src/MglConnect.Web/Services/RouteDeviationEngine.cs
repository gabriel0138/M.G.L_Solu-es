using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MglConnect.Web.Services
{
    public class RouteDeviationEngine : IRouteDeviationEngine
    {
        private const double EarthRadiusMeters = 6371000.0;
        public const double DeviationThresholdMeters = 50.0;     // 50 metros de tolerância lateral
        public const double MaximumAcceptedGpsAccuracyMeters = 50.0;
        public const int ConsecutiveFramesThreshold = 3;         // 3 transmissões consecutivas fora
        public const double AzimuthDivergenceThreshold = 30.0;   // 30 graus de divergência angular
        private static readonly TimeSpan RerouteCooldown = TimeSpan.FromSeconds(8);

        private readonly ConcurrentDictionary<string, VehicleTelemetryState> _states = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<RouteDeviationEngine> _logger;

        public RouteDeviationEngine(ILogger<RouteDeviationEngine> logger)
        {
            _logger = logger;
        }

        public void RegisterActiveRoute(
            string codigoRastreio, 
            int entregaId, 
            string perfilVeiculo, 
            List<RouteCoordinate> routeCoordinates, 
            double destinoLat, 
            double destinoLon, 
            string? destinoNome = null)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio)) return;

            var key = codigoRastreio.Trim().ToUpperInvariant();
            var state = _states.GetOrAdd(key, _ => new VehicleTelemetryState
            {
                CodigoRastreio = key,
                EntregaId = entregaId,
                PerfilVeiculo = perfilVeiculo
            });

            lock (state)
            {
                state.EntregaId = entregaId;
                state.PerfilVeiculo = perfilVeiculo;
                state.RouteCoordinates = new List<RouteCoordinate>(routeCoordinates ?? new List<RouteCoordinate>());
                state.DestinoLatitude = destinoLat;
                state.DestinoLongitude = destinoLon;
                state.DestinoNome = destinoNome ?? "Destino da Entrega";
                state.ActiveSegmentIndex = 0;
                state.OffRouteFramesCount = 0;
                state.IsOffRoute = false;
                state.LastUpdateUtc = DateTime.UtcNow;
            }

            _logger.LogInformation(
                "Rota em memória registrada para a entrega {EntregaId} com {Count} waypoints.",
                entregaId,
                routeCoordinates?.Count ?? 0);
        }

        public DeviationEvaluationResult EvaluatePosition(
            string codigoRastreio, 
            double currentLat, 
            double currentLng, 
            double currentSpeedKmH, 
            double currentHeading,
            double? accuracyMeters = null)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio) || !_states.TryGetValue(codigoRastreio.Trim(), out var state))
            {
                return new DeviationEvaluationResult
                {
                    NeedsReroute = false,
                    IsOffRoute = false,
                    Motivo = "Estado de telemetria não inicializado no motor."
                };
            }

            lock (state)
            {
                if (!double.IsFinite(currentLat) || currentLat is < -90 or > 90
                    || !double.IsFinite(currentLng) || currentLng is < -180 or > 180
                    || !double.IsFinite(currentSpeedKmH) || currentSpeedKmH is < 0 or > 200
                    || !double.IsFinite(currentHeading) || currentHeading is < 0 or >= 360
                    || (accuracyMeters.HasValue
                        && (!double.IsFinite(accuracyMeters.Value) || accuracyMeters.Value < 0)))
                {
                    return RejectedGpsResult(state, "A amostra GPS contém valores inválidos.");
                }

                if (accuracyMeters > MaximumAcceptedGpsAccuracyMeters)
                {
                    return RejectedGpsResult(state, "Precisão GPS insuficiente para avaliar desvio.");
                }

                var now = DateTime.UtcNow;
                if (state.LastAcceptedPositionUtc != DateTime.MinValue)
                {
                    var elapsedSeconds = (now - state.LastAcceptedPositionUtc).TotalSeconds;
                    if (elapsedSeconds is > 0 and <= 120)
                    {
                        var traveledMeters = HaversineDistanceMeters(
                            state.LastKnownLatitude,
                            state.LastKnownLongitude,
                            currentLat,
                            currentLng);
                        var allowedMeters = Math.Max(currentSpeedKmH, state.LastSpeedKmH) / 3.6 * elapsedSeconds * 1.5
                            + 2 * (accuracyMeters ?? 15) + 35;
                        if (traveledMeters > allowedMeters)
                        {
                            return RejectedGpsResult(state, "Salto GPS incompatível com o intervalo e a velocidade informados.");
                        }
                    }
                }

                state.LastKnownLatitude = currentLat;
                state.LastKnownLongitude = currentLng;
                state.LastHeading = currentHeading;
                state.LastSpeedKmH = currentSpeedKmH;
                state.LastGpsAccuracyMeters = accuracyMeters ?? -1;
                state.LastUpdateUtc = now;
                state.LastAcceptedPositionUtc = now;

                var coords = state.RouteCoordinates;
                if (coords == null || coords.Count < 2)
                {
                    return new DeviationEvaluationResult
                    {
                        NeedsReroute = false,
                        IsOffRoute = false,
                        Motivo = "Polilinha de rota insuficiente (< 2 pontos)."
                    };
                }

                // 1. Busca Local O(k) com janela deslizante em torno do segmento ativo
                int totalSegments = coords.Count - 1;
                int searchStart = Math.Max(0, state.ActiveSegmentIndex - 2);
                int searchEnd = Math.Min(totalSegments - 1, state.ActiveSegmentIndex + 8);

                double minCrossTrack = double.MaxValue;
                double bestAlongTrack = 0;
                double bestSegmentBearing = 0;
                int bestSegmentIdx = state.ActiveSegmentIndex;

                for (int i = searchStart; i <= searchEnd; i++)
                {
                    var p1 = coords[i];
                    var p2 = coords[i + 1];

                    var distanceToSegment = MeasureDistanceToSegment(
                        p1.Latitude, p1.Longitude,
                        p2.Latitude, p2.Longitude,
                        currentLat, currentLng);
                    if (distanceToSegment.DistanceMeters < minCrossTrack)
                    {
                        minCrossTrack = distanceToSegment.DistanceMeters;
                        bestAlongTrack = distanceToSegment.AlongTrackMeters;
                        bestSegmentBearing = distanceToSegment.Bearing;
                        bestSegmentIdx = i;
                    }
                }

                // Se a busca local não encontrou projeção (ex: desvio brusco), faz varredura global
                if (minCrossTrack > 500.0)
                {
                    for (int i = 0; i < totalSegments; i++)
                    {
                        var p1 = coords[i];
                        var p2 = coords[i + 1];
                        var distanceToSegment = MeasureDistanceToSegment(
                            p1.Latitude, p1.Longitude,
                            p2.Latitude, p2.Longitude,
                            currentLat, currentLng);
                        if (distanceToSegment.DistanceMeters < minCrossTrack)
                        {
                            minCrossTrack = distanceToSegment.DistanceMeters;
                            bestAlongTrack = distanceToSegment.AlongTrackMeters;
                            bestSegmentBearing = distanceToSegment.Bearing;
                            bestSegmentIdx = i;
                        }
                    }
                }

                // Atualiza o segmento ativo caso o veículo tenha progredido para frente
                if (bestSegmentIdx >= state.ActiveSegmentIndex)
                {
                    state.ActiveSegmentIndex = bestSegmentIdx;
                }

                // 2. Análise de Desvio Angular (Delta Theta)
                double azimuthDivergence = 0.0;
                bool angularDivergenceConfirmed = false;
                if (currentSpeedKmH > 5.0 && currentHeading >= 0.0)
                {
                    azimuthDivergence = CalculateAngularDifference(currentHeading, bestSegmentBearing);
                    angularDivergenceConfirmed = azimuthDivergence >= AzimuthDivergenceThreshold;
                }

                // 3. Filtro de Histerese Espaço-Temporal
                var effectiveThreshold = Math.Max(
                    DeviationThresholdMeters,
                    (accuracyMeters ?? 0) * 2);
                bool isLateralExceeded = minCrossTrack > effectiveThreshold;

                if (isLateralExceeded)
                {
                    state.OffRouteFramesCount++;
                }
                else
                {
                    state.OffRouteFramesCount = 0;
                    state.IsOffRoute = false;
                }

                // Confirmação de desvio: 3 frames consecutivos com XTE > 50m
                // Se além disso houver divergência de azimute (>= 30°), o desvio é incontroverso
                bool confirmedOffRoute = state.OffRouteFramesCount >= ConsecutiveFramesThreshold;
                state.IsOffRoute = confirmedOffRoute;

                bool canReroute = confirmedOffRoute && (DateTime.UtcNow - state.LastRerouteUtc) >= RerouteCooldown;

                if (canReroute)
                {
                    state.LastRerouteUtc = DateTime.UtcNow;
                }

                return new DeviationEvaluationResult
                {
                    NeedsReroute = canReroute,
                    IsOffRoute = confirmedOffRoute,
                    GpsDataAccepted = true,
                    ActiveSegmentIndex = state.ActiveSegmentIndex,
                    OffRouteFramesCount = state.OffRouteFramesCount,
                    CrossTrackDistanceMeters = Math.Round(minCrossTrack, 1),
                    AlongTrackDistanceMeters = Math.Round(bestAlongTrack, 1),
                    AzimuthDivergenceDegrees = Math.Round(azimuthDivergence, 1),
                    Motivo = confirmedOffRoute 
                        ? $"Desvio confirmado ({minCrossTrack:F1}m > {effectiveThreshold:F1}m por {state.OffRouteFramesCount} frames, DeltaAzimute: {azimuthDivergence:F1}°)"
                        : "Veículo em conformidade com o traçado rodoviário planejado."
                };
            }
        }

        private static DeviationEvaluationResult RejectedGpsResult(VehicleTelemetryState state, string reason)
        {
            state.OffRouteFramesCount = 0;
            state.IsOffRoute = false;
            return new DeviationEvaluationResult
            {
                GpsDataAccepted = false,
                IsOffRoute = false,
                OffRouteFramesCount = 0,
                ActiveSegmentIndex = state.ActiveSegmentIndex,
                Motivo = reason
            };
        }

        private static (double DistanceMeters, double AlongTrackMeters, double Bearing) MeasureDistanceToSegment(
            double startLat,
            double startLng,
            double endLat,
            double endLng,
            double pointLat,
            double pointLng)
        {
            var segmentLength = HaversineDistanceMeters(startLat, startLng, endLat, endLng);
            var bearing = CalculateBearing(startLat, startLng, endLat, endLng);
            var distanceFromStart = HaversineDistanceMeters(startLat, startLng, pointLat, pointLng);
            var bearingToPoint = CalculateBearing(startLat, startLng, pointLat, pointLng);
            var alongTrack = distanceFromStart * Math.Cos(ToRadians(
                CalculateAngularDifference(bearing, bearingToPoint)));

            if (alongTrack <= 0)
            {
                return (distanceFromStart, 0, bearing);
            }

            if (alongTrack >= segmentLength)
            {
                return (HaversineDistanceMeters(endLat, endLng, pointLat, pointLng), segmentLength, bearing);
            }

            var crossTrack = Math.Abs(CalculateCrossTrackDistance(
                startLat, startLng, endLat, endLng, pointLat, pointLng));
            return (crossTrack, alongTrack, bearing);
        }

        public void UpdateRouteAfterReroute(string codigoRastreio, List<RouteCoordinate> newRouteCoordinates)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio) || !_states.TryGetValue(codigoRastreio.Trim(), out var state))
                return;

            lock (state)
            {
                state.RouteCoordinates = new List<RouteCoordinate>(newRouteCoordinates ?? new List<RouteCoordinate>());
                state.ActiveSegmentIndex = 0;
                state.OffRouteFramesCount = 0;
                state.IsOffRoute = false;
                state.LastRerouteUtc = DateTime.UtcNow;
            }

            _logger.LogInformation(
                "Nova rota em memória aplicada para a entrega {EntregaId} com {Count} waypoints.",
                state.EntregaId,
                newRouteCoordinates?.Count ?? 0);
        }

        public VehicleTelemetryState? GetState(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio)) return null;
            _states.TryGetValue(codigoRastreio.Trim(), out var state);
            return state;
        }

        public bool RemoveState(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio)) return false;
            return _states.TryRemove(codigoRastreio.Trim(), out _);
        }

        // ==========================================
        // CÁLCULOS MATEMÁTICOS E GEODÉSICOS
        // ==========================================

        public static double CalculateCrossTrackDistance(double lat1, double lon1, double lat2, double lon2, double lat3, double lon3)
        {
            double d13 = HaversineDistanceMeters(lat1, lon1, lat3, lon3) / EarthRadiusMeters;
            double theta13 = ToRadians(CalculateBearing(lat1, lon1, lat3, lon3));
            double theta12 = ToRadians(CalculateBearing(lat1, lon1, lat2, lon2));

            double sinDxt = Math.Sin(d13) * Math.Sin(theta13 - theta12);
            // Clamp para evitar erros numéricos em arcsin
            sinDxt = Math.Clamp(sinDxt, -1.0, 1.0);

            double dXt = Math.Asin(sinDxt) * EarthRadiusMeters;
            return dXt;
        }

        public static double CalculateAlongTrackDistance(double lat1, double lon1, double lat2, double lon2, double lat3, double lon3)
        {
            double d13 = HaversineDistanceMeters(lat1, lon1, lat3, lon3) / EarthRadiusMeters;
            double dXt = CalculateCrossTrackDistance(lat1, lon1, lat2, lon2, lat3, lon3) / EarthRadiusMeters;

            double cosD13 = Math.Cos(d13);
            double cosDxt = Math.Cos(dXt);

            if (Math.Abs(cosDxt) < 1e-9) return 0.0;

            double ratio = cosD13 / cosDxt;
            ratio = Math.Clamp(ratio, -1.0, 1.0);

            double dAt = Math.Acos(ratio) * EarthRadiusMeters;
            return double.IsNaN(dAt) ? 0.0 : dAt;
        }

        public static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusMeters * c;
        }

        public static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
        {
            double dLon = ToRadians(lon2 - lon1);
            double y = Math.Sin(dLon) * Math.Cos(ToRadians(lat2));
            double x = Math.Cos(ToRadians(lat1)) * Math.Sin(ToRadians(lat2)) -
                       Math.Sin(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Cos(dLon);

            double initialBearing = ToDegrees(Math.Atan2(y, x));
            return (initialBearing + 360.0) % 360.0;
        }

        public static double CalculateAngularDifference(double bearing1, double bearing2)
        {
            double diff = Math.Abs(bearing1 - bearing2) % 360.0;
            return diff > 180.0 ? 360.0 - diff : diff;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
        private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
    }
}
