using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;
public sealed class RoadApprovalTests
{
    internal sealed class Handler:HttpMessageHandler {
        public int Calls;public bool Changed,Fail,ShapeChanged;public TaskCompletionSource? Release;
        public TaskCompletionSource Both=new(TaskCreationOptions.RunContinuationsAsynchronously);private int waiting;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
            Interlocked.Increment(ref Calls);if(Release!=null){if(Interlocked.Increment(ref waiting)==2)Both.TrySetResult();await Release.Task.WaitAsync(ct);}
            return new(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(OsrmRoadRouterTests.ValidResponse.Replace("1250",Changed?"2500":"1250").Replace("[-46.632,-23.551]",ShapeChanged?"[-46.631,-23.5507],[-46.632,-23.551]":"[-46.632,-23.551]"))};
        }
    }
    internal sealed class Clock:TimeProvider {public DateTimeOffset At=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>At;}
    internal static ConfirmedRouteStop[] Stops()=>[new("Synthetic origin",-23.55,-46.63,"Map",true),new("Synthetic stop",-23.5505,-46.631,"Map",true),new("Synthetic destination",-23.551,-46.632,"Map",true)];
    internal static DeliveryRoadPlanning Service(MglDbContext db,Handler h,RoadPreviewStore store,Clock? clock=null){
        var factory=new Mock<IHttpClientFactory>();factory.Setup(x=>x.CreateClient("OsrmRoad")).Returns(()=>new HttpClient(h,false));
        return new(db,new(factory.Object,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"}}).Build()),clock:clock,drafts:store);
    }
    [Fact] public async Task ChangedProviderRequiresNewApprovalAndPublishesTheServerSnapshotExactly(){
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);using var store=new RoadPreviewStore();var h=new Handler();var s=Service(db,h,store);
        var id=f.Deliveries[0].Id;var actor=f.Users["admin-a"].Id;var binding=(await s.ContextAsync(id))!.Binding;var stops=Stops();
        var draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;Assert.NotNull(draft.ApprovalId);Assert.Null(draft.Plan.PlanningId);
        h.Changed=true;var changed=await s.PublishApprovedAsync(id,actor,draft.ApprovalId,stops,binding);
        Assert.Equal("Changed",changed.Status);Assert.NotEqual(draft.ApprovalId,changed.Replacement!.ApprovalId);Assert.Equal(1.25m,changed.Previous!.DistanciaKm);Assert.Equal(2.5m,changed.Replacement.Plan.DistanciaKm);
        Assert.Empty(await db.DeliveryRoadPlans.ToListAsync());Assert.Equal(0,(await s.ContextAsync(id))!.Revision);
        var result=await s.PublishApprovedAsync(id,actor,changed.Replacement.ApprovalId,stops,binding);Assert.Equal("Published",result.Status);
        var saved=(await s.ReadAsync(id))!;Assert.Equal(2.5m,saved.DistanciaKm);Assert.Equal(1,saved.PlanRevision);Assert.Equal(JsonSerializer.Serialize(changed.Replacement.Plan.TrajetoCoordenadas),JsonSerializer.Serialize(saved.TrajetoCoordenadas));
        Assert.Equal(changed.Replacement.Plan.CalculatedAtUtc,saved.CalculatedAtUtc);Assert.False(saved.RotaHomologadaParaVeiculo);Assert.Equal(3,saved.ConfirmedStops.Count);
        await Assert.ThrowsAsync<PlanningContextChangedException>(()=>s.PublishApprovedAsync(id,actor,changed.Replacement.ApprovalId,stops,binding));
        Assert.Single(await db.DeliveryRoadPlans.ToListAsync());Assert.Empty(await db.PosicoesGps.ToListAsync());
    }
    [Fact] public async Task GeometryChangeWithEqualMetricsStillRequiresReapproval(){
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);using var store=new RoadPreviewStore();var h=new Handler();var s=Service(db,h,store);
        var id=f.Deliveries[0].Id;var actor=f.Users["admin-a"].Id;var binding=(await s.ContextAsync(id))!.Binding;var stops=Stops();var draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;
        h.ShapeChanged=true;var result=await s.PublishApprovedAsync(id,actor,draft.ApprovalId,stops,binding);
        Assert.Equal("Changed",result.Status);Assert.Equal(result.Previous!.DistanciaKm,result.Replacement!.Plan.DistanciaKm);Assert.Equal(3,result.Replacement.Plan.TrajetoCoordenadas.Count);
        Assert.Empty(await db.DeliveryRoadPlans.ToListAsync());
        var accepted=await s.PublishApprovedAsync(id,actor,result.Replacement.ApprovalId,stops,binding);Assert.Equal("Published",accepted.Status);
        Assert.Equal(JsonSerializer.Serialize(result.Replacement.Plan.TrajetoCoordenadas),JsonSerializer.Serialize(accepted.Published!.TrajetoCoordenadas));
    }
    [Fact] public async Task AnotherAdministratorCannotUseOrConsumeTheOwnersApproval(){
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);using var store=new RoadPreviewStore();var s=Service(db,new Handler(),store);
        var other=new Usuario{TenantId=f.TenantA,Nome="Synthetic administrator",Login="approval-other-admin",SenhaHash="unused-fixture",Perfil="Admin"};db.Usuarios.Add(other);await db.SaveChangesAsync();
        var id=f.Deliveries[0].Id;var actor=f.Users["admin-a"].Id;var binding=(await s.ContextAsync(id))!.Binding;var stops=Stops();var draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;
        foreach(var wrong in new[]{other.Id,f.Users["admin-b"].Id})await Assert.ThrowsAsync<PlanningContextChangedException>(()=>s.PublishApprovedAsync(id,wrong,draft.ApprovalId,stops,binding));
        Assert.Empty(await db.DeliveryRoadPlans.ToListAsync());Assert.Equal("Published",(await s.PublishApprovedAsync(id,actor,draft.ApprovalId,stops,binding)).Status);
    }
    [Theory][InlineData("expiry")][InlineData("address")][InlineData("driver")][InlineData("authorization")][InlineData("order")][InlineData("token")]
    public async Task StaleOrTamperedApprovalCannotPublish(string change){
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);using var store=new RoadPreviewStore();var h=new Handler();var clock=new Clock();var s=Service(db,h,store,clock);
        var id=f.Deliveries[0].Id;var actor=f.Users["admin-a"].Id;var binding=(await s.ContextAsync(id))!.Binding;var stops=Stops();var draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;var ticket=draft.ApprovalId;
        switch(change){
            case "expiry":clock.At=clock.At.AddMinutes(6);break;
            case "address":await f.ChangeAsync(d=>d.Entregas.Single(e=>e.Id==id).EnderecoDestino="Changed synthetic address");break;
            case "driver":await f.ChangeAsync(d=>{var replacement=new Usuario{TenantId=f.TenantA,Nome="Synthetic replacement",Login="replacement-fixture",SenhaHash="unused-fixture",Perfil="Motorista"};d.Usuarios.Add(replacement);d.SaveChanges();d.Entregas.Single(e=>e.Id==id).MotoristaId=replacement.Id;});break;
            case "authorization":await f.ChangeAsync(d=>d.Usuarios.Single(u=>u.Id==actor).Ativo=false);break;
            case "order":(stops[0],stops[1])=(stops[1],stops[0]);break;
            case "token":ticket=new string('A',64);break;
        }
        await Assert.ThrowsAsync<PlanningContextChangedException>(()=>s.PublishApprovedAsync(id,actor,ticket,stops,binding));Assert.Empty(await db.DeliveryRoadPlans.ToListAsync());Assert.Equal(0,(await s.ContextAsync(id))!.Revision);
    }
    [Fact] public async Task FailureAndCancellationPreservePublishedVersion(){
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);using var store=new RoadPreviewStore();var h=new Handler();var s=Service(db,h,store);
        var id=f.Deliveries[0].Id;var actor=f.Users["admin-a"].Id;var binding=(await s.ContextAsync(id))!.Binding;var stops=Stops();var draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;
        var original=(await s.PublishApprovedAsync(id,actor,draft.ApprovalId,stops,binding)).Published!;
        draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;h.Fail=true;Assert.Equal("Unavailable",(await s.PublishApprovedAsync(id,actor,draft.ApprovalId,stops,binding)).Status);Assert.Equal(original.PlanningId,(await s.ReadAsync(id))!.PlanningId);
        h.Fail=false;draft=(await s.CreateApprovalAsync(id,actor,stops,binding))!;h.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);using var ct=new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.PublishApprovedAsync(id,actor,draft.ApprovalId,stops,binding,ct.Token));Assert.Single(await db.DeliveryRoadPlans.ToListAsync());Assert.Equal(1,(await s.ContextAsync(id))!.Revision);
    }
    [Fact] public async Task ConcurrentPublicationsHaveOneWinner()=>await VerifyConcurrentAsync();
    internal static async Task VerifyConcurrentAsync(string? postgres=null){
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false,postgresConnection:postgres);using var db1=new MglDbContext(f.Options);using var db2=new MglDbContext(f.Options);using var store=new RoadPreviewStore();var h=new Handler();var one=Service(db1,h,store);var two=Service(db2,h,store);
        var id=f.Deliveries[0].Id;var actor=f.Users["admin-a"].Id;var binding=(await one.ContextAsync(id))!.Binding;var stops=Stops();
        var a=(await one.CreateApprovalAsync(id,actor,stops,binding))!;var b=(await two.CreateApprovalAsync(id,actor,stops,binding))!;
        h.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Publish(DeliveryRoadPlanning s,string ticket){try{return (await s.PublishApprovedAsync(id,actor,ticket,stops,binding)).Status;}catch(PlanningContextChangedException){return "Conflict";}}
        var first=Publish(one,a.ApprovalId!);var second=Publish(two,b.ApprovalId!);await h.Both.Task.WaitAsync(TimeSpan.FromSeconds(3));h.Release.SetResult();
        Assert.Equal(new[]{"Conflict","Published"},(await Task.WhenAll(first,second)).OrderBy(x=>x));Assert.Single(await db1.DeliveryRoadPlans.ToListAsync());Assert.Equal(1,(await one.ContextAsync(id))!.Revision);
    }
}
