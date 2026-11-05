using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public sealed class ControlledRouteDemoTests
{
    [Theory]
    [InlineData("Development",true,true)]
    [InlineData("Production",true,false)]
    [InlineData("Development",false,false)]
    public void ExplicitMarkerAndDevelopmentAreRequired(string environment,bool marker,bool expected)
    {
        var directory=Directory.CreateTempSubdirectory("mgl-controlled-gate-");
        try {
            using var db=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite($"Data Source={Path.Combine(directory.FullName,"demo.db")}").Options);
            if(marker)File.WriteAllText(Path.Combine(directory.FullName,".mgl-local-preview"),"");
            var env=new Mock<IWebHostEnvironment>();env.SetupGet(e=>e.EnvironmentName).Returns(environment);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"RoutingDemo:Enabled","true"},{"DemoData:Enabled","true"}}).Build();
            Assert.Equal(expected,new ControlledRouteDemo(config,env.Object,db).Enabled);
        } finally { directory.Delete(true); }
    }
    [Fact]
    public void CircuitHasCurvesAndSyntheticEstimatesWithoutOperationalTrust()
    {
        var plan=ControlledRouteDemo.Plan();var alternate=ControlledRouteDemo.Plan(true);
        Assert.True(plan.DemonstracaoControlada);Assert.True(plan.RotaDisponivel);
        Assert.False(plan.RotaHomologadaParaVeiculo);Assert.False(plan.RotaAprovada);Assert.Null(plan.Confianca);
        Assert.True(plan.DistanciaKm>(decimal)(ControlledRouteDemo.Distance(ControlledRouteDemo.Circuit[0],ControlledRouteDemo.Circuit[^1])/1000));
        Assert.True(plan.TempoEstimadoMinutos>0);Assert.True(plan.TrajetoCoordenadas.Count>=6);
        Assert.Equal("Origem",plan.TrajetoCoordenadas[0].Tipo);Assert.Equal("Destino",plan.TrajetoCoordenadas[^1].Tipo);
        Assert.NotEqual(plan.DistanciaKm,alternate.DistanciaKm);
        var failed=ControlledRouteDemo.Plan(failure:"Indisponível");Assert.Empty(failed.TrajetoCoordenadas);Assert.Equal(0,failed.DistanciaKm);Assert.Equal(0,failed.TempoEstimadoMinutos);
    }

    private static Dictionary<string,string> Settings(string? fault=null) => new() {
        ["ASPNETCORE_ENVIRONMENT"]="Development",["RoutingDemo__Enabled"]="true",["DemoData__Enabled"]="true",["RoutingDemo__Failure"]=fault??"" };

    [Fact]
    public async Task ControlledEndpointPersistsOnlySyntheticHistoryAndPreservesScopeAndIdempotency()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(hostingConfiguration:Settings());
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.DatabaseFile)!,".mgl-local-preview"),"");
        using var driver=await f.LoginAsync("driver-a");using var foreign=await f.LoginAsync("driver-b");using var customer=await f.LoginAsync("client-a");
        int trip=f.Deliveries[0].Id;string endpoint=$"/Entregas/{trip}/Demonstracao";
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.Client.GetAsync(endpoint)).StatusCode);
        Assert.Contains((await customer.Client.GetAsync(endpoint)).StatusCode,new[]{HttpStatusCode.Redirect,HttpStatusCode.Forbidden});
        var token=await SecurityHttpFixture.AntiForgeryAsync(driver.Client,$"/Entregas/Navegar/{trip}");
        var body=new DemoSampleInput(Guid.NewGuid(),3,false,DateTimeOffset.UtcNow);
        async Task<JsonElement> Send(DemoSampleInput input){using var request=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=JsonContent.Create(input)};request.Headers.Add("RequestVerificationToken",token);using var response=await driver.Client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();}
        using(var denied=await driver.Client.PostAsJsonAsync(endpoint,body))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var first=await Send(body);Assert.Equal("confirmado",first.GetProperty("receipt").GetProperty("estado").GetString());
        Assert.True(first.GetProperty("latest").GetProperty("isSimulated").GetBoolean());
        Assert.Equal("duplicado",(await Send(body)).GetProperty("receipt").GetProperty("estado").GetString());
        using(var mismatch=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=JsonContent.Create(body with{Sample=4})}) {
            mismatch.Headers.Add("RequestVerificationToken",token);Assert.Equal(HttpStatusCode.NotFound,(await driver.Client.SendAsync(mismatch)).StatusCode);
        }
        var late=await Send(body with{EventId=Guid.NewGuid(),Sample=1,CapturedAt=body.CapturedAt.AddMilliseconds(-1)});
        Assert.Equal(first.GetProperty("latest").GetProperty("eventId").GetGuid(),late.GetProperty("latest").GetProperty("eventId").GetGuid());
        var detour=await Send(new(Guid.NewGuid(),0,true,DateTimeOffset.UtcNow));Assert.True(detour.GetProperty("recalculated").GetBoolean());
        await using(var db=new MglDbContext(f.Options)){
            Assert.Equal(3,await db.PosicoesGps.CountAsync(p=>p.EntregaId==trip&&p.Origem=="Simulacao"));
            Assert.Null((await db.Entregas.FindAsync(trip))!.UltimaLatitudeGps);
            var shift=await db.TurnosMotorista.SingleAsync(t=>t.EntregaId==trip&&t.Ativo);
            var forbidden=await new GpsIngestionService(db).AcceptAsync(f.Users["driver-a"].Id,new(Guid.NewGuid(),trip,shift.Id,0,0,5,DateTimeOffset.UtcNow,Simulado:true));
            Assert.Equal("simulacao_nao_permitida",forbidden.Motivo);
        }
        using(var state=await driver.Client.GetAsync(endpoint)){Assert.Equal(HttpStatusCode.OK,state.StatusCode);Assert.Contains("-23.552",await state.Content.ReadAsStringAsync());}
        // Customer/public GPS projections continue to exclude every synthetic event.
        using(var history=await customer.Client.GetAsync($"/api/gps/historico/{trip}")){
            Assert.Equal(HttpStatusCode.OK,history.StatusCode);var json=await history.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(0,json.GetProperty("total").GetInt32());Assert.Equal(JsonValueKind.Null,json.GetProperty("ultimaPosicao").ValueKind);
        }
        await f.RestartAsync();using var restored=await f.LoginAsync("driver-a");
        using(var state=await restored.Client.GetAsync(endpoint)){Assert.Equal(HttpStatusCode.OK,state.StatusCode);Assert.Contains("-23.552",await state.Content.ReadAsStringAsync());}
        await f.ChangeAsync(db=>db.TurnosMotorista.Single(t=>t.EntregaId==trip&&t.Ativo).Ativo=false);
        Assert.Equal(HttpStatusCode.NotFound,(await restored.Client.GetAsync(endpoint)).StatusCode);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("provider")]
    public async Task MissingOrFailedProviderKeepsPositionAndReturnsNoInventedRoute(string fault)
    {
        await using var f=await SecurityHttpFixture.CreateAsync(hostingConfiguration:Settings(fault));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.DatabaseFile)!,".mgl-local-preview"),"");
        using var driver=await f.LoginAsync("driver-a");using var response=await driver.Client.GetAsync($"/Entregas/{f.Deliveries[0].Id}/Demonstracao");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var plan=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan");
        Assert.False(plan.GetProperty("rotaDisponivel").GetBoolean());Assert.Equal(0,plan.GetProperty("trajetoCoordenadas").GetArrayLength());
        Assert.Equal(0,plan.GetProperty("distanciaKm").GetDecimal());Assert.Equal(0,plan.GetProperty("tempoEstimadoMinutos").GetInt32());
    }
}
