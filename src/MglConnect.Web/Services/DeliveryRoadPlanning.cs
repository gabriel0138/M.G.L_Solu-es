using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services.RouteTrust;

namespace MglConnect.Web.Services;

// Planning is observation of a generic road route, never authorization of a truck.
// Reads do not call a provider, mutate history, or replace a failed attempt with an old route.
public sealed record ConfirmedRouteStop(string Address,double Latitude,double Longitude,string Source,bool Confirmed,string? CandidateLabel=null,DateTime? ConfirmedAtUtc=null)
{
    public bool IsValid()=>Confirmed && (Address?.Trim().Length??0) is >=3 and <=240 && CandidateLabel?.Length is not >600
        && Source is "Photon" or "Map" or "Manual" && new RouteLocation(Latitude,Longitude).Valid;
}
public sealed record PlanningAddressContext(string Origin,string Destination,string Binding,int Revision);
public sealed class PlanningContextChangedException : Exception;
public sealed class DeliveryRoadPlanning(MglDbContext db, OsrmRoadRouter router,DeliveryPlanningPublisher? publisher=null,TimeProvider? clock=null)
{
    private DateTime Now => (clock??TimeProvider.System).GetUtcNow().UtcDateTime;
    private static string Binding(Entrega e) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        e.TenantId,e.PedidoId,e.MotoristaId,e.VeiculoId,e.PerfilViagemVersao,
        e.EnderecoDestino,e.NumeroDestino,e.BairroDestino,e.CidadeDestino,e.EstadoDestino,e.CepDestino,
        e.Pedido?.OrigemEndereco,e.Pedido?.OrigemNumero,e.Pedido?.OrigemBairro,e.Pedido?.OrigemCidade,e.Pedido?.OrigemEstado,e.Pedido?.OrigemCep
    }))));
    private Task<Entrega?> Delivery(int id,CancellationToken ct) => db.Entregas.AsNoTracking().Include(e=>e.Pedido).SingleOrDefaultAsync(e=>e.Id==id,ct);
    // A draft is never a planning revision, GPS sample or published notification.
    public async Task<TruckRoutePlan?> PreviewAsync(int id,IReadOnlyList<ConfirmedRouteStop> stops,string binding,CancellationToken ct=default)
    {
        if(stops.Count is <2 or >10||stops.Any(p=>p==null||!p.IsValid()))return null;
        var points=stops.Select(p=>new RouteLocation(p.Latitude,p.Longitude)).ToList();
        if(points.Distinct().Count()<2)return null;
        var before=await Delivery(id,ct);
        if(before==null||before.StatusEntrega is "Entregue" or "Cancelada")return null;
        if(Binding(before)!=binding)throw new PlanningContextChangedException();
        var plan=(await router.RouteAsync(points,ct)).ToPlan();
        var after=await Delivery(id,ct);
        if(after==null||Binding(after)!=binding||after.StatusEntrega is "Entregue" or "Cancelada")throw new PlanningContextChangedException();
        plan.PerfilVeiculo="Pesado";plan.RotaAprovada=false;plan.RotaHomologadaParaVeiculo=false;plan.Confianca=null;
        return plan;
    }
    public async Task<PlanningAddressContext?> ContextAsync(int id,CancellationToken ct=default)
    {
        var e=await Delivery(id,ct);if(e==null)return null;
        static string Join(params string?[] pieces)=>string.Join(", ",pieces.Where(p=>!string.IsNullOrWhiteSpace(p)));
        return new(Join(e.Pedido?.OrigemEndereco,e.Pedido?.OrigemNumero,e.Pedido?.OrigemBairro,e.Pedido?.OrigemCidade,e.Pedido?.OrigemEstado,e.Pedido?.OrigemCep),
            Join(e.EnderecoDestino,e.NumeroDestino,e.BairroDestino,e.CidadeDestino,e.EstadoDestino,e.CepDestino),Binding(e),e.RoadPlanRevision);
    }
    private async Task NotifyCurrentAsync(int id,Guid attempt)
    {
        if(publisher==null)return;
        var current=await ReadAsync(id);if(current?.PlanningId==attempt)await publisher.PublishAsync(id,current);
    }
    public async Task<TruckRoutePlan?> ReadAsync(int id,CancellationToken ct=default)
    {
        var e=await Delivery(id,ct);if(e==null)return null;
        var row=await db.DeliveryRoadPlans.AsNoTracking().Where(p=>p.EntregaId==id&&p.TenantId==e.TenantId)
            .OrderByDescending(p=>p.Revision).ThenByDescending(p=>p.CreatedAtUtc).ThenByDescending(p=>p.Id).FirstOrDefaultAsync(ct);
        if(row==null)return null;
        if(row.BindingHash!=Binding(e)||row.ValidUntilUtc<=Now)
            return new(){PlanningId=row.Id,PlanRevision=row.Revision,ProviderName="OSRM",ProviderProfile="driving",ProviderFailure=ProviderFailure.MissingLocations,
                AvisoRota="Planejamento expirado ou vínculo/endereço alterado. Solicite novo cálculo."};
        return JsonSerializer.Deserialize<TruckRoutePlan>(row.PlanJson);
    }
    public async Task<TruckRoutePlan?> CalculateAsync(int id,IReadOnlyList<RouteLocation> locations,CancellationToken ct=default,int? expectedDriver=null,IReadOnlyList<ConfirmedRouteStop>? confirmedStops=null,string? expectedBinding=null,Guid? expectedPlanId=null,string? originLabel=null)
    {
        if(locations.Count is <2 or >10||locations.Any(p=>!p.Valid)||locations.Distinct().Count()<2)return null;
        var e=await Delivery(id,ct);if(e==null||expectedDriver.HasValue&&e.MotoristaId!=expectedDriver||e.StatusEntrega is "Entregue" or "Cancelada")return null;
        if(expectedBinding!=null&&Binding(e)!=expectedBinding)throw new PlanningContextChangedException();
        if(confirmedStops!=null&&(confirmedStops.Count!=locations.Count||confirmedStops.Any(p=>!p.IsValid())
            ||confirmedStops.Where((p,i)=>p.Latitude!=locations[i].Latitude||p.Longitude!=locations[i].Longitude).Any()))return null;
        // Claim a monotonic per-delivery revision under a short database transaction.
        // Never hold the write lock while waiting for the provider.
        await using var claim=await db.Database.BeginTransactionAsync(ct);
        await db.Entregas.Where(d=>d.Id==id&&d.TenantId==e.TenantId).ExecuteUpdateAsync(set=>set.SetProperty(d=>d.RoadPlanRevision,d=>d.RoadPlanRevision+1),ct);
        var claimed=await Delivery(id,ct);
        if(claimed==null||Binding(claimed)!=Binding(e))throw new PlanningContextChangedException();
        if(expectedPlanId.HasValue) {
            var previous=await db.DeliveryRoadPlans.AsNoTracking().Where(p=>p.EntregaId==id&&p.TenantId==e.TenantId).OrderByDescending(p=>p.Revision).ThenByDescending(p=>p.CreatedAtUtc).Select(p=>(Guid?)p.Id).FirstOrDefaultAsync(ct);
            if(previous!=expectedPlanId)throw new PlanningContextChangedException();
        }
        var row=new DeliveryRoadPlan{Id=Guid.NewGuid(),TenantId=e.TenantId,EntregaId=id,Revision=claimed.RoadPlanRevision,CreatedAtUtc=Now,
            ValidUntilUtc=Now.AddHours(4),BindingHash=Binding(e),LocationsJson=JsonSerializer.Serialize(locations)};
        row.PlanJson=JsonSerializer.Serialize(new TruckRoutePlan{PlanningId=row.Id,PlanRevision=row.Revision,IsCalculating=true,ProviderName="OSRM",ProviderProfile="driving",
            ProviderFailure=ProviderFailure.Disabled,AvisoRota="Cálculo em andamento; nenhuma geometria disponível."});
        db.DeliveryRoadPlans.Add(row);await db.SaveChangesAsync(ct);await claim.CommitAsync(ct);
        TruckRoutePlan plan;
        try{plan=(await router.RouteAsync(locations,ct)).ToPlan();}
        catch(OperationCanceledException){row.PlanJson=JsonSerializer.Serialize(new TruckRoutePlan{PlanningId=row.Id,PlanRevision=row.Revision,ProviderFailure=ProviderFailure.Timeout,AvisoRota="Cálculo cancelado. Solicite novo cálculo."});await db.SaveChangesAsync(CancellationToken.None);await NotifyCurrentAsync(id,row.Id);throw;}
        plan.PlanningId=row.Id;plan.PlanRevision=row.Revision;plan.CalculatedAtUtc=Now;plan.ValidUntilUtc=row.ValidUntilUtc;
        plan.PerfilVeiculo="Pesado";plan.EixosVeiculo=e.QtdEixos;
        plan.Origem=originLabel??confirmedStops?.FirstOrDefault()?.Address??e.Pedido?.OrigemEndereco??"Origem declarada";plan.Destino=confirmedStops?.LastOrDefault()?.Address??e.EnderecoDestino;
        plan.RequestedStops=locations.Select(p=>new RouteCoordinate{Latitude=p.Latitude,Longitude=p.Longitude}).ToList();
        plan.ConfirmedStops=confirmedStops?.Select(p=>p with{ConfirmedAtUtc=Now}).ToList()??[];
        // No physical verification is inferred from coordinates declared by an administrator.
        plan.RotaAprovada=false;plan.RotaHomologadaParaVeiculo=false;plan.Confianca=null;
        var current=await Delivery(id,ct);
        if(current==null||Binding(current)!=row.BindingHash||current.StatusEntrega is "Entregue" or "Cancelada") {
            plan=new(){PlanningId=row.Id,PlanRevision=row.Revision,ProviderFailure=ProviderFailure.MissingLocations,AvisoRota="Vínculo alterado durante o cálculo. Replaneje a viagem."};
        }
        row.PlanJson=JsonSerializer.Serialize(plan);await db.SaveChangesAsync(ct);
        await NotifyCurrentAsync(id,row.Id);
        // Last requested calculation wins, even if an older provider call finishes later.
        return await ReadAsync(id,ct);
    }
}
