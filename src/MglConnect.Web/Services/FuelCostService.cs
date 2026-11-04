using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;

namespace MglConnect.Web.Services
{
    public class FuelCostService : IFuelCostService
    {
        private readonly MglDbContext _context;
        private readonly ITruckRoutingService _routingService;

        // Parâmetros Operacionais Padrão (Conforme especificação)
        public const decimal ConsumoPadraoCaminhaoKmL = 2.8m;
        public const decimal PrecoPadraoDieselS10 = 6.10m;

        public const decimal ConsumoPadraoVanKmL = 8.5m;
        public const decimal PrecoPadraoGasolina = 5.80m;

        public const decimal TarifaBasePedagioPorEixo = 18.50m;

        public FuelCostService(MglDbContext context, ITruckRoutingService routingService)
        {
            _context = context;
            _routingService = routingService;
        }

        public TripCostResult CalcularCustoViagem(
            decimal distanciaKm, 
            string perfilVeiculo, 
            int qtdEixos = 5, 
            decimal valorPedido = 0m, 
            decimal? precoCombustivelCustom = null, 
            decimal? consumoCustom = null, 
            decimal custoFixo = 0m)
        {
            bool isPesado = !string.Equals(perfilVeiculo, "Leve", StringComparison.OrdinalIgnoreCase);

            decimal consumoKmL = consumoCustom ?? (isPesado ? ConsumoPadraoCaminhaoKmL : ConsumoPadraoVanKmL);
            if (consumoKmL <= 0) consumoKmL = isPesado ? ConsumoPadraoCaminhaoKmL : ConsumoPadraoVanKmL;

            decimal precoLitro = precoCombustivelCustom ?? (isPesado ? PrecoPadraoDieselS10 : PrecoPadraoGasolina);
            if (precoLitro <= 0) precoLitro = isPesado ? PrecoPadraoDieselS10 : PrecoPadraoGasolina;

            int eixos = isPesado ? (qtdEixos > 0 ? qtdEixos : 5) : 1;

            // Fórmulas Financeiras e de Combustível
            decimal litros = distanciaKm>0 && consumoKmL > 0 ? Math.Round(distanciaKm / consumoKmL, 2) : 0m;
            decimal custoCombustivel = Math.Round(litros * precoLitro, 2);
            decimal custoPedagio = distanciaKm>0 ? Math.Round(TarifaBasePedagioPorEixo * eixos, 2) : 0m;
            decimal custoTotalViagem = distanciaKm>0 ? Math.Round(custoCombustivel + custoPedagio + custoFixo, 2) : 0m;
            decimal lucroEstimado = Math.Round(valorPedido - custoTotalViagem, 2);
            decimal margemPercentual = valorPedido > 0 
                ? Math.Round((lucroEstimado / valorPedido) * 100m, 2) 
                : 0m;

            return new TripCostResult
            {
                RouteCostAvailable=distanciaKm>0,
                DistanciaKm = Math.Max(0,distanciaKm),
                PerfilVeiculo = isPesado ? "Pesado" : "Leve",
                TipoCombustivel = isPesado ? "Diesel S10" : "Gasolina Comum / Flex",
                ConsumoKmL = consumoKmL,
                PrecoLitroCombustivel = precoLitro,
                LitrosCombustivel = litros,
                CustoCombustivel = custoCombustivel,
                QtdEixos = eixos,
                TarifaBasePedagioPorEixo = TarifaBasePedagioPorEixo,
                CustoPedagio = custoPedagio,
                CustoOperacionalFixo = custoFixo,
                CustoTotalViagem = custoTotalViagem,
                ValorPedido = valorPedido,
                LucroEstimado = distanciaKm>0?lucroEstimado:0,
                MargemPercentual = distanciaKm>0?margemPercentual:0
            };
        }

        public async Task<TripCostResult> CalcularCustoParaEntregaAsync(int entregaId, decimal? precoCombustivelCustom = null)
        {
            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                .Include(e => e.Veiculo)
                .FirstOrDefaultAsync(e => e.Id == entregaId);

            if (entrega == null)
            {
                throw new ArgumentException("Entrega não encontrada.");
            }

            // Identifica perfil operacional
            bool isPesado = entrega.Veiculo == null || entrega.Veiculo.PesoToneladas > 3.5m || entrega.QtdEixos > 2;
            string perfil = isPesado ? "Pesado" : "Leve";

            // Usa a geometria disponível; não cria distância de fallback para estimar custos.
            var plano = await _routingService.CalcularRotaParaEntregaAsync(entregaId, perfil);

            decimal distanciaKm = RoutePresentation.ValidGeometry(plano) ? plano.DistanciaKm : 0m;
            int eixos = entrega.Veiculo != null ? entrega.Veiculo.QtdEixos : entrega.QtdEixos;
            decimal valorPedido = entrega.Pedido?.ValorTotal ?? 0m;

            return CalcularCustoViagem(
                distanciaKm, 
                perfil, 
                eixos, 
                valorPedido, 
                precoCombustivelCustom
            );
        }

        public async Task<FleetCostSummary> ObterResumoCustosFrotaAsync(decimal? precoDieselCustom = null, decimal? precoGasolinaCustom = null)
        {
            // Cast seguro para SQLite SUM (NotSupportedException)
            var faturamentoTotalDouble = await _context.Pedidos.SumAsync(p => (double?)p.ValorTotal) ?? 0.0;
            decimal faturamentoTotal = (decimal)faturamentoTotalDouble;

            var entregas = await _context.Entregas
                .Include(e => e.Veiculo)
                .ToListAsync();

            decimal totalCombustivel = 0m;
            decimal totalPedagios = 0m;
            decimal totalLitros = 0m;
            int unavailable=0;

            foreach (var ent in entregas)
            {
                bool isPesado = ent.Veiculo == null || ent.Veiculo.PesoToneladas > 3.5m || ent.QtdEixos > 2;
                var plan=await _routingService.CalcularRotaParaEntregaAsync(ent.Id,isPesado?"Pesado":"Leve");
                if(!RoutePresentation.ValidGeometry(plan)||plan.DistanciaKm<=0){unavailable++;continue;}
                decimal dist=plan.DistanciaKm;
                int eixos = ent.Veiculo?.QtdEixos ?? ent.QtdEixos;

                decimal preco = isPesado 
                    ? (precoDieselCustom ?? PrecoPadraoDieselS10) 
                    : (precoGasolinaCustom ?? PrecoPadraoGasolina);

                var custo = CalcularCustoViagem(dist, isPesado ? "Pesado" : "Leve", eixos, 0m, preco);

                totalCombustivel += custo.CustoCombustivel;
                totalPedagios += custo.CustoPedagio;
                totalLitros += custo.LitrosCombustivel;
            }

            decimal custoTotalOperacional = totalCombustivel + totalPedagios;
            decimal margemLucroLiquida = faturamentoTotal - custoTotalOperacional;
            decimal margemPercentual = faturamentoTotal > 0 
                ? Math.Round((margemLucroLiquida / faturamentoTotal) * 100m, 2) 
                : 0m;

            return new FleetCostSummary
            {
                ViagensSemEstimativa=unavailable,MargemDisponivel=unavailable==0,
                FaturamentoTotal = faturamentoTotal,
                CustoTotalCombustivel = totalCombustivel,
                CustoTotalPedagios = totalPedagios,
                CustoTotalOperacional = custoTotalOperacional,
                MargemLucroLiquida = unavailable==0?margemLucroLiquida:0,
                MargemLucroPercentual = unavailable==0?margemPercentual:0,
                TotalLitrosDiesel = totalLitros,
                TotalViagens = entregas.Count
            };
        }
    }
}
