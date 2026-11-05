using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;

namespace MglConnect.Web.Services;

public sealed class DeliveryPlanningPublisher(MglDbContext db,IDriverShiftService shifts,TelemetrySubscriptions subscriptions,
    IHubContext<TelemetryHub> hub,ILogger<DeliveryPlanningPublisher> logger)
{
    public async Task PublishAsync(int trip,TruckRoutePlan plan)
    {
        try {
            var code=await db.Entregas.AsNoTracking().Where(e=>e.Id==trip).Select(e=>e.CodigoRastreio).SingleOrDefaultAsync();if(code==null)return;
            var audience=await TelemetryAudience.ResolveAsync(db,shifts,subscriptions,code,hub.Groups);
            var recipients=subscriptions.ForCode(code).Where(s=>s.Role is "Admin" or "Motorista").Select(s=>s.ConnectionId).Intersect(audience).ToArray();
            if(recipients.Length>0)await hub.Clients.Clients(recipients).SendAsync("PlanningUpdated",new {entregaId=trip,revision=plan.PlanRevision,planningId=plan.PlanningId,state=RoutePresentation.From(plan).State.ToString()});
        } catch(Exception e) when(e is not OutOfMemoryException) {
            // The persisted result remains readable after transport failure. No identity/location in logs.
            logger.LogWarning("Planning notification transport failed: {ResultCode}","notification_unavailable");
        }
    }
}
