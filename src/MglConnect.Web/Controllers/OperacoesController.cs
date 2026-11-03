using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers;

[Authorize(Roles = "Admin")]
[Route("Operacoes")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperacoesController(MglDbContext context, TimeProvider? clock = null) : Controller
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Posicoes")]
    public async Task<IActionResult> Posicoes(CancellationToken cancellationToken)
    {
        var items = await context.Entregas.AsNoTracking()
            .Where(e => e.AtribuicaoAtiva && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
            .OrderByDescending(e => e.UltimoGpsRecebidoEmUtc).ThenBy(e => e.Id)
            .Take(500)
            .Select(e => new FleetPositionViewModel
            {
                Id = e.Id,
                CodigoRastreio = e.CodigoRastreio,
                StatusEntrega = e.StatusEntrega,
                CidadeDestino = e.CidadeDestino,
                EstadoDestino = e.EstadoDestino,
                Motorista = e.Motorista == null ? null : e.Motorista.Nome,
                Veiculo = e.Veiculo == null ? null : e.Veiculo.Modelo,
                Placa = e.Veiculo == null ? null : e.Veiculo.Placa,
                Latitude = e.UltimaLatitudeGps,
                Longitude = e.UltimaLongitudeGps,
                PrecisaoMetros = e.UltimaPrecisaoGps,
                VelocidadeKmH = e.UltimaVelocidadeGps,
                Direcao = e.UltimaDirecaoGps,
                CapturadoEmUtc = e.UltimoGpsCapturadoEmUtc,
                RecebidoEmUtc = e.UltimoGpsRecebidoEmUtc
            }).ToListAsync(cancellationToken);

        // SQLite materializes DateTime with Kind=Unspecified. These values are
        // written only by the server clock and must serialize with UTC semantics.
        foreach (var item in items)
        {
            if (item.RecebidoEmUtc.HasValue) item.RecebidoEmUtc = DateTime.SpecifyKind(item.RecebidoEmUtc.Value, DateTimeKind.Utc);
            if (item.CapturadoEmUtc.HasValue) item.CapturadoEmUtc = DateTime.SpecifyKind(item.CapturadoEmUtc.Value, DateTimeKind.Utc);
        }

        return Json(new { items, consultadoEmUtc = _clock.GetUtcNow().UtcDateTime });
    }
}
