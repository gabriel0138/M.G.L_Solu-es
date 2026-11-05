using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;

namespace MglConnect.Web.Services;

public sealed record DemoSampleInput(Guid EventId, int Sample, bool Detour, DateTimeOffset CapturedAt, Guid? PlanId = null);
public sealed class ControlledDemoNavigation(ControlledRouteDemo demo, MglDbContext db, GpsHistoryAccess access,
    IDriverShiftService shifts, GpsIngestionService ingestion, IHubContext<TelemetryHub> hub, TelemetrySubscriptions subscriptions)
{
    public async Task<GpsAccess?> ResolveAsync(ClaimsPrincipal user,int trip,CancellationToken ct)
    {
        if(!demo.Enabled)return null;
        var scope=await access.ResolveAsync(user,trip,ct);
        if(scope==null || (!scope.Admin && scope.Actor.Perfil!="Motorista")
            || !scope.Delivery.AtribuicaoAtiva || scope.Delivery.StatusEntrega!="Em transporte")return null;
        return await shifts.HasActiveShiftAsync(scope.Delivery.MotoristaId??0,trip)?scope:null;
    }
    public async Task<object?> CaptureAsync(ClaimsPrincipal user,int trip,DemoSampleInput input,CancellationToken ct)
    {
        var scope=await ResolveAsync(user,trip,ct);if(scope==null)return null;
        var plan=await demo.ForTripAsync(trip,ct);
        var previous=await db.PosicoesGps.AsNoTracking().SingleOrDefaultAsync(p=>p.EventoId==input.EventId&&p.EntregaId==trip&&p.MotoristaId==scope.Actor.Id&&p.Origem=="Simulacao",ct);
        if(previous!=null) {
            if(previous.CapturadoEmUtc!=input.CapturedAt.UtcDateTime)return null;
            (double Lat,double Lng)[] originalPath;
            if(input.PlanId.HasValue) {
                var json=await db.DeliveryRoadPlans.AsNoTracking().Where(p=>p.Id==input.PlanId&&p.EntregaId==trip&&p.TenantId==scope.Delivery.TenantId).Select(p=>p.PlanJson).SingleOrDefaultAsync(ct);
                if(json==null)return null;
                originalPath=System.Text.Json.JsonSerializer.Deserialize<TruckRoutePlan>(json)!.TrajetoCoordenadas.Select(p=>(p.Latitude,p.Longitude)).ToArray();
            } else originalPath=input.Detour?ControlledRouteDemo.Detour:ControlledRouteDemo.Circuit;
            if(input.Sample<0||input.Sample>=originalPath.Length)return null;
            var expected=originalPath[input.Sample];if(input.PlanId.HasValue&&input.Detour)expected=(expected.Lat-0.0015,expected.Lng);
            if(previous.Latitude!=expected.Lat||previous.Longitude!=expected.Lng)return null;
            return new {receipt=GpsIngestionService.Receipt(previous,scope.Delivery.CodigoRastreio,"duplicado",false),plan,latest=await demo.LatestAsync(trip,ct),recalculated=false};
        }
        if(!plan.RotaDisponivel)return new {unavailable=true,mensagem=plan.AvisoRota};
        var road=plan.PlanningId.HasValue;
        if(road && input.PlanId!=plan.PlanningId)return new {unavailable=true,mensagem="Planejamento alterado. Atualize antes de simular."};
        var path=road?plan.TrajetoCoordenadas.Select(p=>(Lat:p.Latitude,Lng:p.Longitude)).ToArray()
            :input.Detour?ControlledRouteDemo.Detour:ControlledRouteDemo.Circuit;
        if(input.Sample<0||input.Sample>=path.Length || input.EventId==Guid.Empty)return null;
        var shift=await db.TurnosMotorista.Where(t=>t.EntregaId==trip&&t.Ativo&&t.MotoristaId==scope.Delivery.MotoristaId)
            .Select(t=>(Guid?)t.Id).SingleOrDefaultAsync(ct);
        var point=path[input.Sample];
        // Explicit test deviation is observed independently of the projected route.
        if(road&&input.Detour)point=(point.Lat-0.0015,point.Lng);
        // Actor is always the assigned driver; administrators inspect, they do not impersonate.
        if(scope.Actor.Id!=scope.Delivery.MotoristaId)return null;
        var receipt=await ingestion.AcceptControlledDemoAsync(scope.Actor.Id,new(input.EventId,trip,shift,
            point.Lat,point.Lng,5,input.CapturedAt,20,0,true),ct);
        var latest=await demo.LatestAsync(trip,ct);
        if(road && input.Detour && receipt.Estado=="confirmado")
            await demo.RecalculateRoadAsync(trip,new(point.Lat,point.Lng),new(path[^1].Lat,path[^1].Lng),scope.Actor.Id,ct);
        var activePlan=await demo.ForTripAsync(trip,ct);
        if(receipt.Estado=="confirmado" && latest?.EventId==input.EventId)
        {
            var audience=await TelemetryAudience.ResolveAsync(db,shifts,subscriptions,scope.Delivery.CodigoRastreio,hub.Groups);
            var permitted=subscriptions.ForCode(scope.Delivery.CodigoRastreio).Where(s=>s.Role is "Admin" or "Motorista")
                .Select(s=>s.ConnectionId).Intersect(audience).ToArray();
            if(permitted.Length>0)await hub.Clients.Clients(permitted).SendAsync("ReceiveDemoState",new { plan=activePlan,latest },ct);
        }
        return new { receipt, plan=activePlan, latest, recalculated=input.Detour && latest?.EventId==input.EventId };
    }
}
