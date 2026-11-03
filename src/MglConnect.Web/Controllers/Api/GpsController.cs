using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers.Api;

[ApiController, Authorize]
[Route("api/gps")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GpsController(MglDbContext db, GpsIngestionService ingestion, GpsTelemetryPublisher publisher,
    ILogger<GpsController> logger, GpsHistoryReader reader, GpsHistoryAccess access) : ControllerBase
{
    [HttpPost("sincronizar"), Authorize(Roles = "Motorista", AuthenticationSchemes = MglConnectApiBearerDefaults.Scheme)]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Sincronizar(GpsBatchInput input, CancellationToken cancellationToken)
    {
        // Cookie mutations must supply MVC antiforgery. This API accepts only the
        // short-lived bearer obtained from the cookie-authenticated offline context.
        if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        if (input.Eventos == null || input.Eventos.Count is < 1 or > 50) return BadRequest(new { erro = "Envie de 1 a 50 eventos por lote." });
        var receipts = new List<GpsReceipt>(); object? metrics = null;
        foreach (var item in input.Eventos)
        {
            if (item == null) return BadRequest();
            GpsReceipt receipt;
            try { receipt = await ingestion.AcceptAsync(actorId, item, cancellationToken: cancellationToken); }
            catch (Exception ex) when (ex is DbUpdateException or DbException or GpsArchiveIntegrityException)
            {
                db.ChangeTracker.Clear();
                receipt = new(item.EventoId, "pendente", "tente_novamente");
                logger.LogWarning("O banco não confirmou um evento GPS ({ErrorType}).", ex.GetType().Name);
            }
            receipts.Add(receipt);
            try { metrics = await publisher.PublishAsync(receipt) ?? metrics; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Commit precedes notification: a hub outage must not turn a
                // persisted receipt into a rejection or cause data duplication.
                logger.LogWarning("GPS gravado; notificação indisponível ({ErrorType}).", ex.GetType().Name);
            }
        }
        return Ok(new { receipts, metrics, serverTimeUtc = DateTime.UtcNow });
    }

    [HttpGet("historico/{entregaId:int}")]
    [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme + "," + MglConnectApiBearerDefaults.Scheme)]
    public async Task<IActionResult> Historico(int entregaId, int pagina = 1, string? de = null, string? ate = null, CancellationToken cancellationToken = default)
    {
        if(!GpsPeriod.TryParse(de,ate,out var period))return BadRequest(new{erro="Período inválido; utilize datas ISO/UTC e início anterior ao fim."});
        var scope=await access.ResolveAsync(User,entregaId,cancellationToken);if(scope==null)return NotFound();
        var delivery=scope.Delivery;
        GpsHistoryPage result;
        try{result=await reader.PageAsync(scope.Actor.TenantId,entregaId,pagina,driverId:scope.Actor.Perfil=="Motorista"?scope.Actor.Id:null,period:period,ct:cancellationToken);}
        catch(GpsArchiveIntegrityException){return Conflict(new{erro="Integridade do histórico não confirmada; a operação deve revisar o arquivo."});}
        var items=result.Items.Select(entry=>new{
            sequencia=entry.Posicao.Id,eventoId=entry.Posicao.EventoId,latitude=entry.Posicao.Latitude,longitude=entry.Posicao.Longitude,
            precisaoMetros=entry.Posicao.PrecisaoMetros,capturadoEmUtc=entry.Posicao.CapturadoEmUtc.HasValue?(DateTime?)GpsIngestionService.Utc(entry.Posicao.CapturadoEmUtc.Value):null,
            recebidoEmUtc=GpsIngestionService.Utc(entry.Posicao.RecebidoEmUtc),origem=entry.Posicao.Origem,capturaInformada=entry.Posicao.CapturaInformada,
            qualidade=GpsQuality.Classify(entry.Posicao.PrecisaoMetros),adequadaMapa=GpsQuality.CanMap(entry.Posicao.PrecisaoMetros),
            foraDeOrdem=entry.Posicao.ForaDeOrdem,lacunaAnteriorSegundos=entry.LacunaAnteriorSegundos,arquivado=entry.Arquivado
        }).ToArray();
        var ultimaPosicao = delivery.UltimaLatitudeGps.HasValue && delivery.UltimaLongitudeGps.HasValue
            && delivery.UltimoGpsRecebidoEmUtc.HasValue ? new
        {
            codigoRastreio = delivery.CodigoRastreio, latitude = delivery.UltimaLatitudeGps.Value, longitude = delivery.UltimaLongitudeGps.Value,
            accuracyMeters = delivery.UltimaPrecisaoGps, speedKmH = delivery.UltimaVelocidadeGps, heading = delivery.UltimaDirecaoGps,
            timestamp = GpsIngestionService.Utc((delivery.UltimoGpsCapturadoEmUtc ?? delivery.UltimoGpsRecebidoEmUtc)!.Value),
            receivedAt = GpsIngestionService.Utc(delivery.UltimoGpsRecebidoEmUtc.Value), isSimulated = false,
            captureTimeKnown = delivery.UltimoGpsCapturadoEmUtc.HasValue
        } : null;
        return Ok(new { entregaId, pagina=result.Pagina, paginas=result.Paginas, total=result.Total, items, ultimaPosicao, consultadoEmUtc=DateTime.UtcNow });
    }
}
