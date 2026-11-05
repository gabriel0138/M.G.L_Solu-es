using System.Collections.Generic;
using System.Threading.Tasks;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services
{
    public class RouteCoordinate
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Nome { get; set; }
        public string? Tipo { get; set; } // "Origem", "Destino", "Waypoint", "Alerta"
        public string? Descricao { get; set; }
    }

    public class RouteStep
    {
        public int Ordem { get; set; }
        public string NomeVia { get; set; } = string.Empty;
        public string Instrucao { get; set; } = string.Empty;
        public string ManobraTipo { get; set; } = "turn";
        public string ManobraModificador { get; set; } = "straight";
        public double DistanciaMetros { get; set; }
        public double DuracaoSegundos { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string IconeBootstrap { get; set; } = "bi-arrow-up";
    }

    public class TruckRestrictionAlert
    {
        public string Tipo { get; set; } = string.Empty; // "Altura", "Peso", "Horario", "Largura"
        public string Severidade { get; set; } = "Perigo"; // "Perigo", "Atencao", "Informativo"
        public string Mensagem { get; set; } = string.Empty;
        public string Localizacao { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public sealed record RoadAlternative(List<RouteCoordinate> Geometry, decimal DistanceKm, double DurationSeconds);

    public class TruckRoutePlan
    {
        public Guid? PlanningId { get; set; }
        public DateTime? CalculatedAtUtc { get; set; }
        public DateTime? ValidUntilUtc { get; set; }
        public string? EngineVersion { get; set; }
        public string? DatasetReference { get; set; }
        // Synthetic GPS rehearsal is independent of the origin of the geometry.
        public bool SimulationEnabled { get; set; }
        public List<RouteCoordinate> RequestedStops { get; set; } = new();
        public List<RouteCoordinate> ProviderWaypoints { get; set; } = new();
        public int AlternativeCount { get; set; }
        public List<RoadAlternative> Alternatives { get; set; } = new();
        public string? ProviderResponseSha256 { get; set; }
        public string? ProviderGraphVersion { get; set; }
        public string? GeometrySourceReference { get; set; }
        public string ProviderName { get; set; } = "Não consultado";
        public string ProviderProfile { get; set; } = "Não informado";
        public string GeometrySource { get; set; } = "Unavailable";
        public double? ProviderDurationSeconds { get; set; }
        public MglConnect.Web.Services.RouteTrust.ProviderFailure ProviderFailure { get; set; }
        public bool DemonstracaoControlada { get; set; }
        public MglConnect.Web.Services.RouteTrust.RouteTrustResult? Confianca { get; set; }
        public string Origem { get; set; } = string.Empty;
        public string Destino { get; set; } = string.Empty;
        public bool RotaDisponivel { get; set; }
        public bool RotaHomologadaParaVeiculo { get; set; }
        public string AvisoRota { get; set; } = string.Empty;
        public decimal DistanciaKm { get; set; }
        public int TempoEstimadoMinutos { get; set; }
        public decimal? CustoPedagioEstimado { get; set; }
        public bool RotaAprovada { get; set; }
        public decimal AlturaVeiculo { get; set; }
        public decimal PesoVeiculo { get; set; }
        public int EixosVeiculo { get; set; }
        public string TipoCarga { get; set; } = "Carga Geral";
        public string PerfilVeiculo { get; set; } = "Pesado"; // "Pesado" (Caminhão / Carga Pesada) ou "Leve" (Utilitário / Veículo Leve)
        public string CorredorRodoviario { get; set; } = string.Empty;
        public List<TruckRestrictionAlert> Alertas { get; set; } = new();
        public List<RouteCoordinate> TrajetoCoordenadas { get; set; } = new();
        public List<RouteStep> PassosNavegacao { get; set; } = new();
    }

    public interface ITruckRoutingService
    {
        Task<TruckRoutePlan> CalcularRotaPesadaAsync(
            string origem, 
            string destino, 
            decimal alturaMetros, 
            decimal pesoToneladas, 
            decimal larguraMetros, 
            int qtdEixos, 
            string tipoCarga,
            string perfilVeiculo = "Pesado");

        Task<TruckRoutePlan> CalcularRotaParaEntregaAsync(int entregaId, string perfilVeiculo = "Pesado");

        Task<TruckRoutePlan?> RecalcularRotaDinamicaAsync(
            string codigoRastreio, 
            double latAtual, 
            double lonAtual, 
            string perfilVeiculo = "Pesado");

        Task<TruckRoutePlan> ObterOuInicializarRotaAsync(string codigoRastreio, string perfilVeiculo = "Pesado");
    }
}
