using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;

namespace MglConnect.Web.Services;

public sealed record DemoSampleInput(Guid EventId, int Sample, bool Detour, DateTimeOffset CapturedAt);
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
        var plan=await demo.ForTripAsync(trip,ct);if(!plan.RotaDisponivel)return new { unavailable=true, mensagem=plan.AvisoRota };
        var path=input.Detour?ControlledRouteDemo.Detour:ControlledRouteDemo.Circuit;
        if(input.Sample<0||input.Sample>=path.Length || input.EventId==Guid.Empty)return null;
        var shift=await db.TurnosMotorista.Where(t=>t.EntregaId==trip&&t.Ativo&&t.MotoristaId==scope.Delivery.MotoristaId)
            .Select(t=>(Guid?)t.Id).SingleOrDefaultAsync(ct);
        var point=path[input.Sample];
        // Actor is always the assigned driver; administrators inspect, they do not impersonate.
        if(scope.Actor.Id!=scope.Delivery.MotoristaId)return null;
        var receipt=await ingestion.AcceptControlledDemoAsync(scope.Actor.Id,new(input.EventId,trip,shift,
            point.Lat,point.Lng,5,input.CapturedAt,20,0,true),ct);
        var latest=await demo.LatestAsync(trip,ct);
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
