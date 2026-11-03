using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using Microsoft.AspNetCore.SignalR;
using MglConnect.Web.Hubs;
using System.Data;
using Microsoft.AspNetCore.RateLimiting;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using MglConnect.Web.ViewModels;
namespace MglConnect.Web.Controllers;
[Authorize(Roles="Admin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class RouteAssessmentController(MglDbContext db,RouteTrustService trust,ITruckRoutingService routes, IHubContext<TelemetryHub> hub,TelemetrySubscriptions subscriptions,IDriverShiftService shifts,IRouteDeviationEngine deviations,ILogger<RouteAssessmentController> logger):Controller
{
    private Task<bool> Owns(MglConnect.Web.Models.Entrega e,CancellationToken ct)=>db.Usuarios.AnyAsync(u=>u.Id==int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)&&u.Ativo&&(u.Perfil=="Admin"||u.Perfil=="Administrador")&&u.TenantId==e.TenantId,ct);
    [HttpGet("Rotas/Entrega/{id:int}")]
    public async Task<IActionResult> Index(int id,CancellationToken ct,Guid? avaliacao=null)=>await Page(id,null,ct,avaliacao);
    private async Task<IActionResult> Page(int id,TruckProfileForm? form,CancellationToken ct,Guid? selected=null)
    {
        var e=await trust.Delivery(id,ct);if(e==null||!await Owns(e,ct))return NotFound();
        var p=TripProfiles.Read(e);
        var assessment=await trust.CurrentAsync(id,ct);
        if(selected.HasValue)
        {
            var historic=await db.RotaAvaliacoes.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==selected&&a.EntregaId==id,ct);if(historic==null)return NotFound();
            var original=JsonSerializer.Deserialize<RouteTrustResult>(historic.ResultadoJson)!;
            if(assessment?.CalculationId!=original.CalculationId||assessment.Obsolete){original.Obsolete=true;original.NavigationAllowed=false;original.Reasons.Add("Registro histórico; não representa uma autorização atual.");}
            assessment=original;
        }
        return View("Index",new RouteAssessmentViewModel{Delivery=e,Assessment=assessment,
            Profile=form??TruckProfileForm.From(p,TripProfiles.Revision(e)),ProfileHistory=await db.PerfisViagemRevisoes.AsNoTracking().Where(r=>r.EntregaId==id).OrderByDescending(r=>r.Revisao).Take(20).ToListAsync(ct),History=await db.RotaAvaliacoes.AsNoTracking().Where(a=>a.EntregaId==id).OrderByDescending(a=>a.CriadoEmUtc).Take(20).ToListAsync(ct)});
    }
    [HttpPost("Rotas/Entrega/{id:int}/Perfil"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(int id,TruckProfileForm form,CancellationToken ct)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var delivery=await trust.Delivery(id,ct);if(delivery==null||!await Owns(delivery,ct))return NotFound();if(delivery.StatusEntrega is "Entregue" or "Cancelada")return Conflict("Viagem encerrada: o perfil histórico é preservado e não pode ser reescrito.");if(delivery.VeiculoId==null)return Conflict("Atribua um veículo antes de declarar o perfil.");
        OperationalTruckProfile? profile=null;
        if(ModelState.IsValid)try{profile=form.ToProfile(id);foreach(var error in profile.Validate())ModelState.AddModelError("",error);}catch(Exception ex)when(ex is FormatException or OverflowException){ModelState.AddModelError("","Cargas por eixo: use números em kg com ponto decimal, separados por ponto e vírgula.");}
        if(!ModelState.IsValid){Response.StatusCode=400;return await Page(id,form,ct);}
        var vehicle=await db.Veiculos.SingleAsync(v=>v.Id==delivery.VeiculoId&&v.TenantId==delivery.TenantId,ct);
        var tracked=await db.Entregas.SingleAsync(e=>e.Id==id,ct);
        if(form.Revision!=TripProfiles.Revision(delivery)){Response.StatusCode=409;ModelState.AddModelError("","Perfil alterado por outra operação. Reabra a página.");return await Page(id,form,ct);}
        profile!.VehicleId=vehicle.Id;
        tracked.PerfilViagemJson=JsonSerializer.Serialize(profile);tracked.PerfilViagemVersao=form.Revision+1;
        db.PerfisViagemRevisoes.Add(new(){TenantId=delivery.TenantId,EntregaId=id,VeiculoId=vehicle.Id,AtorUsuarioId=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            Revisao=tracked.PerfilViagemVersao,CriadoEmUtc=DateTime.UtcNow,PerfilJson=tracked.PerfilViagemJson,CadastroVeiculoJson=TripProfiles.VehicleSnapshot(vehicle)});
        db.AuditoriaOperacional.Add(new(){TenantId=delivery.TenantId,AtorUsuarioId=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),Evento="PerfilOperacionalDeclarado",TipoEntidade="Entrega",IdentificadorEntidade=id.ToString(),Detalhes=$"Revisão {tracked.PerfilViagemVersao}; veículo {vehicle.Id}",OcorridoEmUtc=DateTime.UtcNow});
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Conflict("Perfil alterado durante a gravação. Reabra a página.");}
        await transaction.CommitAsync(ct);deviations.RemoveState(delivery.CodigoRastreio);
        try{var audience=await TelemetryAudience.ResolveAsync(db,shifts,subscriptions,delivery.CodigoRastreio,hub.Groups);
            if(audience.Count>0)await hub.Clients.Clients(audience).SendAsync("ReceiveRouteTrustInvalidated",new{codigoRastreio=delivery.CodigoRastreio,navigationAllowed=false,mensagem="Perfil alterado; solicite nova avaliação."},ct);}
        catch(Exception ex)when(ex is not OperationCanceledException){logger.LogWarning("Perfil salvo; notificação indisponível ({ErrorType}).",ex.GetType().Name);}
        TempData["MensagemSucesso"]="Perfil declarado salvo. Avaliações anteriores precisam ser recalculadas.";return RedirectToAction(nameof(Index),new{id});
    }
    [HttpPost("Rotas/Entrega/{id:int}/Calcular"),ValidateAntiForgeryToken,EnableRateLimiting("route-assess")]
    public async Task<IActionResult> Calculate(int id,CancellationToken ct)
    {
        if(await trust.Delivery(id,ct)==null)return NotFound();
        await routes.CalcularRotaParaEntregaAsync(id,"Pesado");return RedirectToAction(nameof(Index),new{id});
    }
}
