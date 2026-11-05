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
public sealed class DeliveryRoadPlanning(MglDbContext db, OsrmRoadRouter router)
{
    private static string Binding(Entrega e) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        e.TenantId,e.PedidoId,e.MotoristaId,e.VeiculoId,e.PerfilViagemVersao,
        e.EnderecoDestino,e.NumeroDestino,e.BairroDestino,e.CidadeDestino,e.EstadoDestino,e.CepDestino,
        e.Pedido?.OrigemEndereco,e.Pedido?.OrigemNumero,e.Pedido?.OrigemBairro,e.Pedido?.OrigemCidade,e.Pedido?.OrigemEstado,e.Pedido?.OrigemCep
    }))));
    private Task<Entrega?> Delivery(int id,CancellationToken ct) => db.Entregas.AsNoTracking().Include(e=>e.Pedido).SingleOrDefaultAsync(e=>e.Id==id,ct);
    public async Task<TruckRoutePlan?> ReadAsync(int id,CancellationToken ct=default)
    {
        var e=await Delivery(id,ct);if(e==null)return null;
        var row=await db.DeliveryRoadPlans.AsNoTracking().Where(p=>p.EntregaId==id&&p.TenantId==e.TenantId)
            .OrderByDescending(p=>p.CreatedAtUtc).ThenByDescending(p=>p.Id).FirstOrDefaultAsync(ct);
        if(row==null)return null;
        if(row.BindingHash!=Binding(e)||row.ValidUntilUtc<=DateTime.UtcNow)
            return new(){PlanningId=row.Id,ProviderName="OSRM",ProviderProfile="driving",ProviderFailure=ProviderFailure.MissingLocations,
                AvisoRota="Planejamento expirado ou vínculo/endereço alterado. Solicite novo cálculo."};
        return JsonSerializer.Deserialize<TruckRoutePlan>(row.PlanJson);
    }
    public async Task<TruckRoutePlan?> CalculateAsync(int id,IReadOnlyList<RouteLocation> locations,CancellationToken ct=default,int? expectedDriver=null)
    {
        if(locations.Count is <2 or >10||locations.Any(p=>!p.Valid)||locations.Distinct().Count()<2)return null;
        var e=await Delivery(id,ct);if(e==null||expectedDriver.HasValue&&e.MotoristaId!=expectedDriver||e.StatusEntrega is "Entregue" or "Cancelada")return null;
        var row=new DeliveryRoadPlan{Id=Guid.NewGuid(),TenantId=e.TenantId,EntregaId=id,CreatedAtUtc=DateTime.UtcNow,
            ValidUntilUtc=DateTime.UtcNow.AddHours(4),BindingHash=Binding(e),LocationsJson=JsonSerializer.Serialize(locations)};
        row.PlanJson=JsonSerializer.Serialize(new TruckRoutePlan{PlanningId=row.Id,ProviderName="OSRM",ProviderProfile="driving",
            ProviderFailure=ProviderFailure.Disabled,AvisoRota="Cálculo em andamento; nenhuma geometria disponível."});
        db.DeliveryRoadPlans.Add(row);await db.SaveChangesAsync(ct);
        TruckRoutePlan plan;
        try{plan=(await router.RouteAsync(locations,ct)).ToPlan();}
        catch(OperationCanceledException){row.PlanJson=JsonSerializer.Serialize(new TruckRoutePlan{PlanningId=row.Id,ProviderFailure=ProviderFailure.Timeout,AvisoRota="Cálculo cancelado. Solicite novo cálculo."});await db.SaveChangesAsync(CancellationToken.None);throw;}
        plan.PlanningId=row.Id;plan.CalculatedAtUtc=DateTime.UtcNow;plan.ValidUntilUtc=row.ValidUntilUtc;
        plan.PerfilVeiculo="Pesado";plan.EixosVeiculo=e.QtdEixos;
        plan.Origem=e.Pedido?.OrigemEndereco??"Origem declarada";plan.Destino=e.EnderecoDestino;
        plan.RequestedStops=locations.Select(p=>new RouteCoordinate{Latitude=p.Latitude,Longitude=p.Longitude}).ToList();
        // No physical verification is inferred from coordinates declared by an administrator.
        plan.RotaAprovada=false;plan.RotaHomologadaParaVeiculo=false;plan.Confianca=null;
        var current=await Delivery(id,ct);
        if(current==null||Binding(current)!=row.BindingHash||current.StatusEntrega is "Entregue" or "Cancelada") {
            plan=new(){PlanningId=row.Id,ProviderFailure=ProviderFailure.MissingLocations,AvisoRota="Vínculo alterado durante o cálculo. Replaneje a viagem."};
        }
        row.PlanJson=JsonSerializer.Serialize(plan);await db.SaveChangesAsync(ct);
        // Last requested calculation wins, even if an older provider call finishes later.
        return await ReadAsync(id,ct);
    }
}
