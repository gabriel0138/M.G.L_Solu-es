using System.Net;
using Xunit;
namespace MglConnect.Tests;
public sealed class PlanningNotificationTests
{
    [Fact]
    public async Task RevisionNotificationRevalidatesDriverAndNeverReachesCustomerOrForeignTenant()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();int trip=f.Deliveries[0].Id;string code=f.Deliveries[0].CodigoRastreio;
        using var admin=await f.LoginAsync("admin-a");using var driver=await f.LoginAsync("driver-a");using var client=await f.LoginAsync("client-a");using var other=await f.LoginAsync("admin-b");
        await using var a=await SecurityHubConnection.ConnectAsync(f,admin);await using var d=await SecurityHubConnection.ConnectAsync(f,driver);await using var c=await SecurityHubConnection.ConnectAsync(f,client);await using var b=await SecurityHubConnection.ConnectAsync(f,other);
        foreach(var hub in new[]{a,d,c})Assert.False((await hub.InvokeAsync("JoinLiveTracking",code)).TryGetProperty("error",out _));
        Assert.True((await b.InvokeAsync("JoinLiveTracking",code)).TryGetProperty("error",out _));
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,"/Roteamento?entregaId="+trip);
        async Task Calculate(){using var response=await admin.Client.PostAsync("/Roteamento/Planejar",new FormUrlEncodedContent(new Dictionary<string,string>{{"entregaId",trip.ToString()},{"coordenadas","-46.63,-23.55;-46.632,-23.551"},{"__RequestVerificationToken",token}}));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);}
        await Calculate();var payload=await d.EventPayloadAsync("PlanningUpdated",TimeSpan.FromSeconds(3));
        Assert.Equal(1,payload.GetProperty("revision").GetInt32());Assert.Equal(trip,payload.GetProperty("entregaId").GetInt32());Assert.False(payload.TryGetProperty("geometry",out _));
        Assert.True(await a.ReceivesAsync("PlanningUpdated",TimeSpan.FromSeconds(2)));Assert.False(await c.ReceivesAsync("PlanningUpdated",TimeSpan.FromMilliseconds(150)));Assert.False(await b.ReceivesAsync("PlanningUpdated",TimeSpan.FromMilliseconds(150)));
        await f.ChangeAsync(db=>db.Usuarios.Single(u=>u.Id==f.Users["driver-a"].Id).Ativo=false);
        await Calculate();Assert.True(await a.ReceivesAsync("PlanningUpdated",TimeSpan.FromSeconds(2)));Assert.False(await d.ReceivesAsync("PlanningUpdated",TimeSpan.FromMilliseconds(200)));
    }
}
