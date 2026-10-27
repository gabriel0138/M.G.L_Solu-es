using System.Threading.Tasks;

namespace MglConnect.Web.Services
{
    public class TripCostResult
    {
        public decimal DistanciaKm { get; set; }
        public string PerfilVeiculo { get; set; } = "Pesado"; // "Pesado" (Caminhão) ou "Leve" (Van/Utilitário)
        public string TipoCombustivel { get; set; } = "Diesel S10";
        public decimal ConsumoKmL { get; set; }
        public decimal PrecoLitroCombustivel { get; set; }
        public decimal LitrosCombustivel { get; set; }
        public decimal CustoCombustivel { get; set; }
        public int QtdEixos { get; set; }
        public decimal TarifaBasePedagioPorEixo { get; set; } = 18.50m;
        public decimal CustoPedagio { get; set; }
        public decimal CustoOperacionalFixo { get; set; }
        public decimal CustoTotalViagem { get; set; }
        public decimal ValorPedido { get; set; }
        public decimal LucroEstimado { get; set; }
        public decimal MargemPercentual { get; set; }
    }

    public class FleetCostSummary
    {
        public decimal FaturamentoTotal { get; set; }
        public decimal CustoTotalCombustivel { get; set; }
        public decimal CustoTotalPedagios { get; set; }
        public decimal CustoTotalOperacional { get; set; }
        public decimal MargemLucroLiquida { get; set; }
        public decimal MargemLucroPercentual { get; set; }
        public decimal TotalLitrosDiesel { get; set; }
        public int TotalViagens { get; set; }
    }

    public interface IFuelCostService
    {
        TripCostResult CalcularCustoViagem(
            decimal distanciaKm, 
            string perfilVeiculo, 
            int qtdEixos = 5, 
            decimal valorPedido = 0m, 
            decimal? precoCombustivelCustom = null, 
            decimal? consumoCustom = null, 
            decimal custoFixo = 0m);

        Task<TripCostResult> CalcularCustoParaEntregaAsync(
            int entregaId, 
            decimal? precoCombustivelCustom = null);

        Task<FleetCostSummary> ObterResumoCustosFrotaAsync(
            decimal? precoDieselCustom = null, 
            decimal? precoGasolinaCustom = null);
    }
}
