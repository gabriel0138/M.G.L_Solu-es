using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public sealed class GpsPublisherAuthorizationTests
{
    [Theory]
    [InlineData("account")] [InlineData("customer")] [InlineData("shift")] [InlineData("tenant")] [InlineData("assignment")]
    public async Task DelayedRoutingRevalidatesTheSnapshotAndEachRecipientInsteadOfReusingAPastAudience(string reason)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(startServer:false);
        await using var db = new MglDbContext(fixture.Options);
        var trip=fixture.Deliveries[0];
        var receipt=await new GpsIngestionService(db).AcceptAsync(fixture.Users["driver-a"].Id,
            new GpsEventInput(Guid.NewGuid(),trip.Id,(await db.TurnosMotorista.SingleAsync(t=>t.EntregaId==trip.Id)).Id,
                -23,-46,10,DateTimeOffset.UtcNow,40,90));
        Assert.Equal("confirmado",receipt.Estado);
        var subscriptions=new TelemetrySubscriptions();
        var affected=reason is "shift" or "assignment"?"driver-a":"client-a";
        foreach(var (login,connection) in new[]{(affected,"affected"),("admin-a","control")})
        {
            var user=fixture.Users[login];
            subscriptions.Add(trip.CodigoRastreio,connection,new ClaimsPrincipal(new ClaimsIdentity(new[]{
                new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Role,user.Perfil),new Claim("TenantId",user.TenantId.ToString())},"fixture")));
        }
        var emissions=new List<(string Name,string[] Recipients)>();
        var clients=new Mock<IHubClients>();
        clients.Setup(c=>c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns((IReadOnlyList<string> recipients)=>{
            var ids=recipients.ToArray();var proxy=new Mock<IClientProxy>();
            proxy.Setup(p=>p.SendCoreAsync(It.IsAny<string>(),It.IsAny<object?[]>(),It.IsAny<CancellationToken>()))
                .Callback<string,object?[],CancellationToken>((name,_,_)=>emissions.Add((name,ids))).Returns(Task.CompletedTask);
            return proxy.Object;
        });
        var groups=new Mock<IGroupManager>();groups.Setup(g=>g.RemoveFromGroupAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var hub=new Mock<IHubContext<TelemetryHub>>();hub.SetupGet(h=>h.Clients).Returns(clients.Object);hub.SetupGet(h=>h.Groups).Returns(groups.Object);
        var engine=new Mock<IRouteDeviationEngine>();
        engine.Setup(e=>e.EvaluatePosition(It.IsAny<string>(),It.IsAny<double>(),It.IsAny<double>(),It.IsAny<double>(),It.IsAny<double>(),It.IsAny<double?>()))
            .Returns(new DeviationEvaluationResult{GpsDataAccepted=true,NeedsReroute=true});
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider=new TaskCompletionSource<TruckRoutePlan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var routing=new Mock<ITruckRoutingService>();
        routing.Setup(r=>r.RecalcularRotaDinamicaAsync(trip.CodigoRastreio,-23,-46,"Pesado")).Returns(()=>{entered.TrySetResult();return provider.Task;});
        var publisher=new GpsTelemetryPublisher(db,hub.Object,subscriptions,new DriverShiftService(db),engine.Object,routing.Object,NullLogger<GpsTelemetryPublisher>.Instance);
        var sending=publisher.PublishAsync(receipt);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("affected",Assert.Single(emissions).Recipients);
            await fixture.ChangeAsync(change=>{
                var user=change.Usuarios.Single(u=>u.Id==fixture.Users[affected].Id);
                switch(reason)
                {
                    case "account":user.Ativo=false;break;
                    case "customer":user.ClienteId=fixture.Customers[1].Id;break;
                    case "tenant":user.TenantId=fixture.TenantB;break;
                    case "shift":change.TurnosMotorista.Single(t=>t.EntregaId==trip.Id).Ativo=false;break;
                    case "assignment":
                        var delivery=change.Entregas.Single(e=>e.Id==trip.Id);delivery.MotoristaId=null;
                        delivery.UltimoGpsCapturadoEmUtc=null;delivery.UltimaLatitudeGps=null;delivery.UltimaLongitudeGps=null;
                        break;
                }
            });
        }
        finally{provider.TrySetResult(new TruckRoutePlan());}
        await sending;
        Assert.All(emissions.Skip(1),e=>Assert.DoesNotContain("affected",e.Recipients));
        if(reason=="assignment")Assert.Single(emissions);
        else Assert.Contains(emissions.Skip(1),e=>e.Recipients.Contains("control"));
    }
}
