using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class FrotaController(MglDbContext database,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    MglConnect.Web.Services.IPasswordHasher passwords) : Controller
{
    private const int PageSize = 20;

    [HttpGet]
    public async Task<IActionResult> Index(string? termo, string? status, int paginaVeiculos = 1, int paginaMotoristas = 1) =>
        View(await LoadAsync(termo, status, paginaVeiculos, paginaMotoristas));

    [HttpGet]
    public async Task<IActionResult> Painel(string? termo, string? status, int paginaVeiculos = 1, int paginaMotoristas = 1) =>
        PartialView("_RosterPanel", await LoadAsync(termo, status, paginaVeiculos, paginaMotoristas));

    private async Task<FleetRosterViewModel> LoadAsync(string? termo, string? status, int paginaVeiculos, int paginaMotoristas)
    {
        termo = (termo?.Trim() ?? "");
        if (termo.Length > 100) termo = termo[..100];
        status = status is "ativos" or "atribuidos" or "sem-atribuicao" or "inativos" ? status : "todos";
        var assignments = database.Entregas.AsNoTracking().Where(e => e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte");
        var vehicles = database.Veiculos.AsNoTracking();
        var drivers = database.Usuarios.AsNoTracking().Where(u => u.Perfil == "Motorista");
        var vehicleActive = await vehicles.CountAsync(v => v.Ativo);
        var vehicleUnassigned = await vehicles.CountAsync(v => v.Ativo && !assignments.Any(e => e.VeiculoId == v.Id));
        var driverActive = await drivers.CountAsync(d => d.Ativo);
        var activeShifts = await database.TurnosMotorista.CountAsync(t => t.Ativo);
        if (termo.Length > 0)
        {
            var search = termo.ToLowerInvariant();
            vehicles = Search(vehicles, search, v => v.Modelo, v => v.Placa);
            drivers = Search(drivers, search, d => d.Nome, d => d.Login);
        }
        vehicles = status switch
        {
            "ativos" => vehicles.Where(v => v.Ativo), "inativos" => vehicles.Where(v => !v.Ativo),
            "atribuidos" => vehicles.Where(v => assignments.Any(e => e.VeiculoId == v.Id)),
            "sem-atribuicao" => vehicles.Where(v => v.Ativo && !assignments.Any(e => e.VeiculoId == v.Id)), _ => vehicles
        };
        drivers = status switch
        {
            "ativos" => drivers.Where(d => d.Ativo), "inativos" => drivers.Where(d => !d.Ativo),
            "atribuidos" => drivers.Where(d => assignments.Any(e => e.MotoristaId == d.Id)),
            "sem-atribuicao" => drivers.Where(d => d.Ativo && !assignments.Any(e => e.MotoristaId == d.Id)), _ => drivers
        };
        var vehicleCount = await vehicles.CountAsync();
        var driverCount = await drivers.CountAsync();
        paginaVeiculos = Math.Clamp(paginaVeiculos, 1, Pages(vehicleCount));
        paginaMotoristas = Math.Clamp(paginaMotoristas, 1, Pages(driverCount));
        var vehicleRows = await vehicles.OrderBy(v => v.Modelo).ThenBy(v => v.Id)
            .Skip((paginaVeiculos - 1) * PageSize).Take(PageSize)
            .Select(v => new { v.Id, v.Modelo, v.Placa, v.CapacidadeCargaKg, v.Ativo }).ToListAsync();
        var driverRows = await drivers.OrderBy(d => d.Nome).ThenBy(d => d.Id)
            .Skip((paginaMotoristas - 1) * PageSize).Take(PageSize)
            .Select(d => new { d.Id, d.Nome, d.Ativo }).ToListAsync();
        var vehicleIds = vehicleRows.Select(v => v.Id).ToArray();
        var driverIds = driverRows.Select(d => d.Id).ToArray();
        var links = await assignments.Where(e => vehicleIds.Contains(e.VeiculoId ?? 0) || driverIds.Contains(e.MotoristaId ?? 0))
            .Select(e => new FleetAssignment(e.Id, e.CodigoRastreio, e.StatusEntrega, e.MotoristaId, e.VeiculoId)).ToListAsync();
        var byVehicle = links.ToLookup(e => e.VeiculoId);
        var byDriver = links.ToLookup(e => e.MotoristaId);
        var shifts = await database.TurnosMotorista.Where(t => t.Ativo && driverIds.Contains(t.MotoristaId))
            .GroupBy(t => t.MotoristaId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(t => t.Id, t => t.Count);
        return new FleetRosterViewModel
        {
            Termo = termo, Status = status, VeiculosAtivos = vehicleActive, VeiculosSemAtribuicao = vehicleUnassigned,
            MotoristasAtivos = driverActive, TotalTurnosAtivos = activeShifts,
            TotalVeiculos = vehicleCount, TotalMotoristas = driverCount,
            PaginaVeiculos = paginaVeiculos, PaginaMotoristas = paginaMotoristas,
            PaginasVeiculos = Pages(vehicleCount), PaginasMotoristas = Pages(driverCount),
            Veiculos = vehicleRows.Select(v => new FleetVehicle(v.Id, v.Modelo, v.Placa, v.CapacidadeCargaKg, v.Ativo, byVehicle[v.Id].ToArray())).ToArray(),
            Motoristas = driverRows.Select(d => new FleetDriver(d.Id, d.Nome, d.Ativo, shifts.GetValueOrDefault(d.Id), byDriver[d.Id].ToArray())).ToArray()
        };
    }

    // SQLite LOWER is ASCII-only. Fold Portuguese capitals before LOWER so search
    // remains in SQL and behaves consistently with the other supported providers.
    private static IQueryable<T> Search<T>(IQueryable<T> query, string term, params Expression<Func<T, string>>[] fields)
    {
        Expression? combined = null;
        var parameter = Expression.Parameter(typeof(T), "resource");
        foreach (var field in fields)
        {
            Expression text = Expression.Property(parameter, ((MemberExpression)field.Body).Member.Name);
            foreach (var capital in "ÁÀÂÃÉÊÍÓÔÕÚÜÇ")
                text = Expression.Call(text, nameof(string.Replace), null,
                    Expression.Constant(capital.ToString()), Expression.Constant(char.ToLowerInvariant(capital).ToString()));
            text = Expression.Call(text, nameof(string.ToLower), null);
            var contains = Expression.Call(text, nameof(string.Contains), null, Expression.Constant(term));
            combined = combined == null ? contains : Expression.OrElse(combined, contains);
        }
        return query.Where(Expression.Lambda<Func<T, bool>>(combined!, parameter));
    }

    private static int Pages(int total) => Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
}
