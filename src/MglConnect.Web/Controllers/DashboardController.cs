using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly MglDbContext _context;
        private readonly IFuelCostService _fuelService;

        public DashboardController(MglDbContext context, IFuelCostService fuelService)
        {
            _context = context;
            _fuelService = fuelService;
        }

        public async Task<IActionResult> Index()
        {
            var totalClientes = await _context.Clientes.CountAsync(c => c.Ativo);
            var totalPedidos = await _context.Pedidos.CountAsync();
            var entregasPendentes = await _context.Entregas.CountAsync(e => e.StatusEntrega == "Pendente");
            var entregasEmTransporte = await _context.Entregas.CountAsync(e => e.StatusEntrega == "Em transporte");
            var entregasEntregues = await _context.Entregas.CountAsync(e => e.StatusEntrega == "Entregue");

            // Cast seguro SQLite contra NotSupportedException em SumAsync
            // Usa AsNoTracking para consultas puramente informativas (Dashboard)
            var valorTotalDouble = await _context.Pedidos.AsNoTracking().SumAsync(p => (double?)p.ValorTotal) ?? 0.0;
            var valorTotal = (decimal)valorTotalDouble;

            // Métricas de Custos Operacionais e Combustível da Frota (Módulo 3)
            var fleetCosts = await _fuelService.ObterResumoCustosFrotaAsync();

            var entregasRecentes = await _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .OrderByDescending(e => e.DataCriacao)
                .Take(5)
                .AsNoTracking()
                .ToListAsync();

            var pedidosSemEntrega = await _context.Pedidos
                .Include(p => p.Cliente)
                .Where(p => p.Entrega == null)
                .OrderByDescending(p => p.DataPedido)
                .Take(5)
                .AsNoTracking()
                .ToListAsync();

            var viewModel = new DashboardViewModel
            {
                TotalClientes = totalClientes,
                TotalPedidos = totalPedidos,
                EntregasPendentes = entregasPendentes,
                EntregasEmTransporte = entregasEmTransporte,
                EntregasEntregues = entregasEntregues,
                ValorTotalPedidos = valorTotal,
                CustoTotalCombustivel = fleetCosts.CustoTotalCombustivel,
                CustoTotalPedagios = fleetCosts.CustoTotalPedagios,
                CustoTotalFrota = fleetCosts.CustoTotalOperacional,
                MargemLucroLiquida = fleetCosts.MargemLucroLiquida,
                MargemLucroPercentual = fleetCosts.MargemLucroPercentual,
                TotalLitrosCombustivel = fleetCosts.TotalLitrosDiesel,
                EntregasRecentes = entregasRecentes,
                PedidosSemEntrega = pedidosSemEntrega
            };

            // ESG: Calcular emissões CO2 acumuladas (baseado em distância média por entrega)
            var totalEntregasConc = entregasEntregues;
            viewModel.TotalEntregasConcluidas = totalEntregasConc;
            // Estimativa: média 150km/entrega, 3.8 kg CO2/km para caminhão pesado (diesel)
            viewModel.EmissaoCo2TotalKg = Math.Round(totalEntregasConc * 150.0 * 3.8, 1);

            return View(viewModel);
        }
    }
}
