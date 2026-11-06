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
public sealed class DeliveryRoadPlanning(MglDbContext db, OsrmRoadRouter router,DeliveryPlanningPublisher? publisher=null,TimeProvider? clock=null,RoadPreviewStore? drafts=null)
{
    private DateTime Now => (clock??TimeProvider.System).GetUtcNow().UtcDateTime;
    private static string Binding(Entrega e) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        e.TenantId,e.PedidoId,e.MotoristaId,e.VeiculoId,e.PerfilViagemVersao,
        e.EnderecoDestino,e.NumeroDestino,e.BairroDestino,e.CidadeDestino,e.EstadoDestino,e.CepDestino,
        e.Pedido?.OrigemEndereco,e.Pedido?.OrigemNumero,e.Pedido?.OrigemBairro,e.Pedido?.OrigemCidade,e.Pedido?.OrigemEstado,e.Pedido?.OrigemCep
    }))));
    private Task<Entrega?> Delivery(int id,CancellationToken ct) => db.Entregas.AsNoTracking().Include(e=>e.Pedido).SingleOrDefaultAsync(e=>e.Id==id,ct);
    private Task<bool> ActiveAdmin(int actor,Guid tenant,CancellationToken ct)=>db.Usuarios.AnyAsync(u=>u.Id==actor&&u.TenantId==tenant&&u.Ativo&&(u.Perfil=="Admin"||u.Perfil=="Administrador"),ct);
    private static string StopsIdentity(IReadOnlyList<ConfirmedRouteStop> stops)=>JsonSerializer.Serialize(stops.Select(s=>s with{ConfirmedAtUtc=null}));
    // Strict comparison: even a small change needs explicit reapproval; receipt times do not.
    private static string RouteIdentity(TruckRoutePlan p)=>JsonSerializer.Serialize(new {
        p.ProviderName,p.ProviderProfile,p.GeometrySource,p.GeometrySourceReference,p.EngineVersion,p.DatasetReference,p.ProviderGraphVersion,
        p.TrajetoCoordenadas,p.ProviderWaypoints,p.DistanciaKm,p.ProviderDurationSeconds,p.PassosNavegacao
    });
    private static bool PublicationConflict(Exception error)
    {
        // EF's execution strategy can wrap a provider serialization/deadlock
        // exception. Translate only known conflicts, never retry an approval.
        for(Exception? current=error;current!=null;current=current.InnerException)
            if(current is DbUpdateConcurrencyException
                or Microsoft.Data.Sqlite.SqliteException{SqliteErrorCode:5 or 6}
                or Npgsql.PostgresException{SqlState:"40001" or "40P01"}
                or Microsoft.Data.SqlClient.SqlException{Number:1205 or 3960})return true;
        return false;
    }
    private ApprovedRoadPreview Remember(Entrega e,int actor,IReadOnlyList<ConfirmedRouteStop> stops,TruckRoutePlan plan)
    {
        var expiry=Now.AddMinutes(5);plan.CalculatedAtUtc=Now;
        plan.RequestedStops=stops.Select(p=>new RouteCoordinate{Latitude=p.Latitude,Longitude=p.Longitude}).ToList();
        var id=RoutePresentation.ValidGeometry(plan)?drafts?.Put(new(e.TenantId,actor,e.Id,e.RoadPlanRevision,Binding(e),expiry,StopsIdentity(stops),JsonSerializer.Serialize(plan))):null;
        return new(plan,id,expiry,e.RoadPlanRevision);
    }
    public async Task<ApprovedRoadPreview?> CreateApprovalAsync(int id,int actor,IReadOnlyList<ConfirmedRouteStop> stops,string binding,CancellationToken ct=default)
    {
        var before=await Delivery(id,ct);
        if(before==null||!await ActiveAdmin(actor,before.TenantId,ct))return null;
        var plan=await PreviewAsync(id,stops,binding,ct);if(plan==null)return null;
        var after=await Delivery(id,ct);
        if(after==null||after.RoadPlanRevision!=before.RoadPlanRevision||Binding(after)!=binding||!await ActiveAdmin(actor,after.TenantId,ct))throw new PlanningContextChangedException();
        return Remember(after,actor,stops,plan);
    }
    public async Task<RoadPublicationResult> PublishApprovedAsync(int id,int actor,string? approvalId,IReadOnlyList<ConfirmedRouteStop> stops,string binding,CancellationToken ct=default)
    {
        var e=await Delivery(id,ct);
        if(e==null||!await ActiveAdmin(actor,e.TenantId,ct))throw new PlanningContextChangedException();
        var snapshot=drafts?.Take(approvalId,actor,id,e.TenantId);
        if(snapshot==null||snapshot.ExpiresAtUtc<=Now||snapshot.Binding!=binding||Binding(e)!=binding||snapshot.BaseRevision!=e.RoadPlanRevision||snapshot.StopsJson!=StopsIdentity(stops))throw new PlanningContextChangedException();
        var approved=JsonSerializer.Deserialize<TruckRoutePlan>(snapshot.PlanJson)!;
        // The browser supplies only an opaque identifier. Recalculate without reserving a revision.
        var fresh=await PreviewAsync(id,stops,binding,ct);
        var current=await Delivery(id,ct);
        if(fresh==null||current==null||snapshot.ExpiresAtUtc<=Now||current.RoadPlanRevision!=snapshot.BaseRevision||Binding(current)!=binding||!await ActiveAdmin(actor,current.TenantId,ct))throw new PlanningContextChangedException();
        if(!RoutePresentation.ValidGeometry(fresh))return new("Unavailable",Replacement:new(fresh,null,Now,snapshot.BaseRevision));
        if(RouteIdentity(approved)!=RouteIdentity(fresh))return new("Changed",Replacement:Remember(current,actor,stops,fresh),Previous:approved);

        // CAS + short serializable transaction: two approvals based on the same version cannot both win.
        try {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            var claimed=await db.Entregas.Where(d=>d.Id==id&&d.TenantId==snapshot.TenantId&&d.RoadPlanRevision==snapshot.BaseRevision&&d.StatusEntrega!="Entregue"&&d.StatusEntrega!="Cancelada")
                .ExecuteUpdateAsync(set=>set.SetProperty(d=>d.RoadPlanRevision,d=>d.RoadPlanRevision+1),ct);
            current=await Delivery(id,ct);
            if(claimed!=1||current==null||Binding(current)!=binding||snapshot.ExpiresAtUtc<=Now||!await ActiveAdmin(actor,snapshot.TenantId,ct))throw new PlanningContextChangedException();
            var row=new DeliveryRoadPlan{Id=Guid.NewGuid(),TenantId=snapshot.TenantId,EntregaId=id,Revision=snapshot.BaseRevision+1,
                CreatedAtUtc=Now,ValidUntilUtc=Now.AddHours(4),BindingHash=binding,LocationsJson=JsonSerializer.Serialize(stops.Select(p=>new RouteLocation(p.Latitude,p.Longitude)))};
            // Persist the server's approved snapshot, never geometry or metrics from the form.
            approved.PlanningId=row.Id;approved.PlanRevision=row.Revision;approved.ValidUntilUtc=row.ValidUntilUtc;
            approved.PerfilVeiculo="Pesado";approved.EixosVeiculo=current.QtdEixos;approved.Origem=stops[0].Address;approved.Destino=stops[^1].Address;
            approved.RequestedStops=stops.Select(p=>new RouteCoordinate{Latitude=p.Latitude,Longitude=p.Longitude}).ToList();
            approved.ConfirmedStops=stops.Select(p=>p with{ConfirmedAtUtc=Now}).ToList();
            row.PlanJson=JsonSerializer.Serialize(approved);db.DeliveryRoadPlans.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }catch(Exception error) when(PublicationConflict(error)){throw new PlanningContextChangedException();}
        await NotifyCurrentAsync(id,approved.PlanningId!.Value);return new("Published",Published:approved);
    }
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
