using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MglConnect.Web.Services;

namespace MglConnect.Web.Services
{
    public class DeviationEvaluationResult
    {
        public bool GpsDataAccepted { get; set; } = true;
        public bool NeedsReroute { get; set; }
        public bool IsOffRoute { get; set; }
        public int ActiveSegmentIndex { get; set; }
        public int OffRouteFramesCount { get; set; }
        public double CrossTrackDistanceMeters { get; set; }
        public double AlongTrackDistanceMeters { get; set; }
        public double AzimuthDivergenceDegrees { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }

    public class VehicleTelemetryState
    {
        public string CodigoRastreio { get; set; } = string.Empty;
        public int EntregaId { get; set; }
        public string PerfilVeiculo { get; set; } = "Pesado";
        public double DestinoLatitude { get; set; }
        public double DestinoLongitude { get; set; }
        public string DestinoNome { get; set; } = string.Empty;
        public int ActiveSegmentIndex { get; set; } = 0;
        public int OffRouteFramesCount { get; set; } = 0;
        public bool IsOffRoute { get; set; } = false;
        public double LastKnownLatitude { get; set; }
        public double LastKnownLongitude { get; set; }
        public double LastHeading { get; set; }
        public double LastSpeedKmH { get; set; }
        public double LastGpsAccuracyMeters { get; set; } = -1;
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastAcceptedPositionUtc { get; set; } = DateTime.MinValue;
        public DateTime LastRerouteUtc { get; set; } = DateTime.MinValue;
        public List<RouteCoordinate> RouteCoordinates { get; set; } = new();
    }

    public interface IRouteDeviationEngine
    {
        void RegisterActiveRoute(
            string codigoRastreio, 
            int entregaId, 
            string perfilVeiculo, 
            List<RouteCoordinate> routeCoordinates, 
            double destinoLat, 
            double destinoLon, 
            string? destinoNome = null);

        DeviationEvaluationResult EvaluatePosition(
            string codigoRastreio, 
            double currentLat, 
            double currentLng, 
            double currentSpeedKmH, 
            double currentHeading,
            double? accuracyMeters = null);

        void UpdateRouteAfterReroute(string codigoRastreio, List<RouteCoordinate> newRouteCoordinates);

        VehicleTelemetryState? GetState(string codigoRastreio);

        bool RemoveState(string codigoRastreio);
    }
}
