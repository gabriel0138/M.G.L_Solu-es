using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DashboardController : Controller
{
    private readonly MglDbContext _context;

    public DashboardController(MglDbContext context, IFuelCostService fuelService) => _context = context;

    public async Task<IActionResult> Index(DateOnly? de, DateOnly? ate)
    {
        if (!ValidPeriod(de, ate))
        {
            ModelState.AddModelError(string.Empty, "Informe um período válido, com a data inicial anterior à final.");
            return View(new DashboardViewModel { PeriodoInicio = de, PeriodoFim = ate });
        }
        return View(await LoadAsync(de, ate));
    }

    [HttpGet]
    public async Task<IActionResult> Painel(DateOnly? de, DateOnly? ate)
    {
        if (!ValidPeriod(de, ate)) return BadRequest(new { erro = "Período inválido." });
        return PartialView("_OperationalPanel", await LoadAsync(de, ate));
    }

    private bool ValidPeriod(DateOnly? de, DateOnly? ate) => ModelState.IsValid && (!de.HasValue || !ate.HasValue || de <= ate);

    private async Task<DashboardViewModel> LoadAsync(DateOnly? de, DateOnly? ate)
    {
        var now = DateTime.UtcNow;
        var orders = _context.Pedidos.AsNoTracking().AsQueryable();
        var deliveries = _context.Entregas.AsNoTracking().AsQueryable();
        if (de.HasValue)
        {
            var start = de.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            orders = orders.Where(p => p.DataPedido >= start);
            deliveries = deliveries.Where(e => e.DataCriacao >= start);
        }
        if (ate.HasValue)
        {
            // Include the final date with its full subsecond precision.
            var end = ate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            orders = orders.Where(p => p.DataPedido <= end);
            deliveries = deliveries.Where(e => e.DataCriacao <= end);
        }
        var queue = orders.Where(p => (p.Status == "Registrado" || p.Status == "EmProcessamento") && p.Entrega == null);
        return new DashboardViewModel
        {
            PeriodoInicio = de, PeriodoFim = ate, UltimaAtualizacaoUtc = now,
            TotalClientes = await _context.Clientes.CountAsync(c => c.Ativo),
            TotalPedidos = await orders.CountAsync(),
            PedidosAguardandoDespacho = await queue.CountAsync(),
            EntregasPendentes = await deliveries.CountAsync(e => e.StatusEntrega == "Pendente"),
            EntregasEmTransporte = await deliveries.CountAsync(e => e.StatusEntrega == "Em transporte"),
            EntregasEntregues = await deliveries.CountAsync(e => e.StatusEntrega == "Entregue"),
            EntregasAtrasadas = await deliveries.CountAsync(e => (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte") && e.DataPrevisaoEntrega < now),
            ValorTotalPedidos = Math.Round((decimal)(await orders.Where(p => p.Status != "Cancelado").SumAsync(p => (double?)p.ValorTotal) ?? 0), 2),
            VeiculosAtivos = await _context.Veiculos.CountAsync(v => v.Ativo),
            VeiculosEmUso = await _context.Entregas.Where(e => e.AtribuicaoAtiva && e.VeiculoId != null).Select(e => e.VeiculoId).Distinct().CountAsync(),
            AlertasAbertos = await _context.AlertasEmergencia.CountAsync(a => a.Status == "Aberto" || a.Status == "Reconhecido" || a.Status == "EmAtendimento"),
            EntregasRecentes = await deliveries.Include(e => e.Motorista).Include(e => e.Pedido).ThenInclude(p => p!.Cliente).OrderByDescending(e => e.DataCriacao).Take(5).ToListAsync(),
            PedidosSemEntrega = await queue.Include(p => p.Cliente).OrderBy(p => p.DataPedido).Take(5).ToListAsync()
        };
    }
}
