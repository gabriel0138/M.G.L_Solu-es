using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;
namespace MglConnect.Tests;

public sealed class GpsRetentionTests
{
    private static async Task<(GpsEventInput Input,Guid Shift)> Input(SecurityHttpFixture f,DateTimeOffset? captured=null,double? accuracy=8)
    {
        await f.ChangeAsync(db=>{foreach(var t in db.TurnosMotorista)t.IniciadoEmUtc=DateTime.UtcNow.AddDays(-7);});
        await using var db=new MglDbContext(f.Options);var id=f.Deliveries[0].Id;var shift=await db.TurnosMotorista.SingleAsync(t=>t.EntregaId==id);
        return(new(Guid.NewGuid(),id,shift.Id,-23.5,-46.6,accuracy,captured??DateTimeOffset.UtcNow,40,90),shift.Id);
    }
    private static async Task<JsonElement> Send(SecurityHttpFixture.Session driver,GpsEventInput input)
    {
        using var message=driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{input}});
        using var response=await driver.Client.SendAsync(message);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipts")[0].Clone();
    }
    private static Task Close(SecurityHttpFixture f)=>f.ChangeAsync(db=>{var e=db.Entregas.Single(e=>e.Id==f.Deliveries[0].Id);e.StatusEntrega="Entregue";e.AtribuicaoAtiva=false;e.DataEntregaRealizada=DateTime.UtcNow;
        foreach(var t in db.TurnosMotorista.Where(t=>t.EntregaId==e.Id)){t.Ativo=false;t.EncerradoEmUtc=DateTime.UtcNow;}});
    [Fact]
    public async Task ArchiveCompactionKeepsHistoryDownloadIdentityAndOriginalReceiptAcrossRestart()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(gpsRetentionDays:0);using var driver=await f.LoginAsync("driver-a");using var admin=await f.LoginAsync("admin-a");
        var (input,_)=await Input(f);Assert.Equal("confirmado",(await Send(driver,input)).GetProperty("estado").GetString());await Close(f);
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,"/Frota");
        using(var result=await admin.Client.PostAsync("/GpsHistorico/Arquivar",new FormUrlEncodedContent(new Dictionary<string,string>{{"entregaId",input.EntregaId.ToString()},{"__RequestVerificationToken",token}})))Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        Guid id;
        await using(var db=new MglDbContext(f.Options)){var archive=await db.GpsArquivos.SingleAsync();id=archive.Id;Assert.Single(GpsArchiveService.Verify(archive));Assert.Equal(1,await db.PosicoesGps.CountAsync());Assert.Equal(1,await db.GpsEventosArquivados.CountAsync());}
        using(var download=await admin.Client.GetAsync("/GpsHistorico/Arquivo/"+id)){Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal("application/gzip",download.Content.Headers.ContentType!.MediaType);Assert.True(download.Headers.Contains("X-Content-SHA256"));}
        using(var compact=await admin.Client.PostAsync("/GpsHistorico/Compactar",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",id.ToString()},{"confirmar","true"},{"__RequestVerificationToken",token}})))Assert.Equal(HttpStatusCode.Redirect,compact.StatusCode);
        await using(var db=new MglDbContext(f.Options)){Assert.Equal(0,await db.PosicoesGps.CountAsync());Assert.Equal(1,await db.GpsEventosArquivados.CountAsync());Assert.NotNull((await db.GpsArquivos.SingleAsync()).CompactadoEmUtc);Assert.Equal(1,await db.AuditoriaOperacional.CountAsync(a=>a.Evento=="GpsArquivoCompactado"));}
        Assert.Equal("duplicado",(await Send(driver,input)).GetProperty("estado").GetString());
        var changed=await Send(driver,input with{Latitude=-22});Assert.Equal("evento_conflitante",changed.GetProperty("motivo").GetString());
        using var client=await f.LoginAsync("client-a");var history=await client.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{input.EntregaId}");Assert.Equal(1,history.GetProperty("total").GetInt32());Assert.True(history.GetProperty("items")[0].GetProperty("arquivado").GetBoolean());
        await f.RestartAsync();using var again=await f.LoginAsync("driver-a");Assert.Equal("duplicado",(await Send(again,input)).GetProperty("estado").GetString());
        await using var verify=new MglDbContext(f.Options);Assert.Equal(0,await verify.PosicoesGps.CountAsync());
    }
    [Fact]
    public async Task ActiveTripAndRetentionCannotBeBypassedAndSourceRemainsUntilExplicitCompaction()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var (input,_)=await Input(f);
        await using var db=new MglDbContext(f.Options);var ingest=new GpsIngestionService(db);await ingest.AcceptAsync(f.Users["driver-a"].Id,input);
        var archive=new GpsArchiveService(db);Assert.False((await archive.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId)).Success);await Close(f);
        var copy=await archive.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId);Assert.True(copy.Success);Assert.False((await archive.CompactAsync(f.Users["admin-a"].Id,copy.ArquivoId!.Value,true)).Success);
        Assert.False((await archive.CompactAsync(f.Users["admin-a"].Id,copy.ArquivoId.Value,false)).Success);Assert.Equal(1,await db.PosicoesGps.CountAsync());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CorruptPackageOrOnlineCopyCannotBeCompactedOrReusedAsANewUuid(bool source)
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var (input,_)=await Input(f);await using var db=new MglDbContext(f.Options);
        await new GpsIngestionService(db).AcceptAsync(f.Users["driver-a"].Id,input);await Close(f);
        var service=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0}));var file=await service.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId);
        db.ChangeTracker.Clear();await f.ChangeAsync(change=>{if(source)change.PosicoesGps.Single().Latitude=-22;else change.GpsArquivos.Single().ConteudoGzip=[1,2,3];});
        await Assert.ThrowsAsync<GpsArchiveIntegrityException>(()=>service.CompactAsync(f.Users["admin-a"].Id,file.ArquivoId!.Value,true));Assert.Equal(1,await db.PosicoesGps.CountAsync());
    }
    [Theory][InlineData(30d,"adequada",true)][InlineData(80d,"aproximada",false)][InlineData(900d,"baixa_precisao",false)][InlineData(null,"precisao_desconhecida",true)]
    public async Task ImpreciseValidCoordinatesAreAuditedWithoutReplacingReliableNavigation(double? accuracy,string quality,bool snapshot)
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var (input,_)=await Input(f,accuracy:accuracy);using var driver=await f.LoginAsync("driver-a");
        Assert.Equal("confirmado",(await Send(driver,input)).GetProperty("estado").GetString());await using var db=new MglDbContext(f.Options);
        var saved=await db.PosicoesGps.SingleAsync();Assert.Equal(accuracy,saved.PrecisaoMetros);Assert.Equal(quality,GpsQuality.Classify(saved.PrecisaoMetros));
        Assert.Equal(snapshot,(await db.Entregas.SingleAsync(e=>e.Id==input.EntregaId)).UltimaLatitudeGps.HasValue);
    }
    [Fact]
    public async Task FiveDayOfflineCaptureInsideARecordedClosedShiftIsHistoricalOnly()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var (input,_)=await Input(f,DateTimeOffset.UtcNow.AddDays(-5));await Close(f);using var driver=await f.LoginAsync("driver-a");
        var receipt=await Send(driver,input);Assert.Equal("confirmado",receipt.GetProperty("estado").GetString());Assert.False(receipt.GetProperty("atualizouPosicao").GetBoolean());
        var late=await Send(driver,input with{EventoId=Guid.NewGuid(),CapturadoEmUtc=DateTimeOffset.UtcNow.AddMinutes(1)});Assert.Equal("rejeitado",late.GetProperty("estado").GetString());
        await using var db=new MglDbContext(f.Options);Assert.Equal(input.CapturadoEmUtc.UtcDateTime,(await db.PosicoesGps.SingleAsync()).CapturadoEmUtc);Assert.Null((await db.Entregas.FindAsync(input.EntregaId))!.UltimaLatitudeGps);
    }
    [Fact]
    public async Task MovingQuotaAllowsNewDaysWithoutRaisingLifetimeNumbers()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var (input,_)=await Input(f);await using var db=new MglDbContext(f.Options);
        db.PosicoesGps.Add(new PosicaoGps{TenantId=f.TenantA,EventoId=Guid.NewGuid(),EntregaId=input.EntregaId,MotoristaId=f.Users["driver-a"].Id,
            AtorUsuarioId=f.Users["driver-a"].Id,VeiculoId=f.Deliveries[0].VeiculoId!.Value,TurnoId=input.TurnoId,Latitude=-23,Longitude=-46,
            CapturadoEmUtc=DateTime.UtcNow.AddDays(-2),RecebidoEmUtc=DateTime.UtcNow.AddDays(-2),OrdenadoEmUtc=DateTime.UtcNow.AddDays(-2)});await db.SaveChangesAsync();
        var service=new GpsIngestionService(db,options:Options.Create(new GpsHistoryOptions{MaximoPorViagem=1,MaximoPorOrganizacao=1}));
        Assert.Equal("confirmado",(await service.AcceptAsync(f.Users["driver-a"].Id,input)).Estado);
        Assert.Equal("limite_diario",(await service.AcceptAsync(f.Users["driver-a"].Id,input with{EventoId=Guid.NewGuid()})).Motivo);Assert.Equal(2,await db.PosicoesGps.CountAsync());
    }
    [Fact]
    public async Task ArchivedDataAndUuidCannotCrossCustomerOrganizationAssignmentOrAccountRevocation()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var(input,_)=await Input(f);using var driver=await f.LoginAsync("driver-a");await Send(driver,input);await Close(f);
        await using var db=new MglDbContext(f.Options);var archives=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0}));
        var file=await archives.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId);await archives.CompactAsync(f.Users["admin-a"].Id,file.ArquivoId!.Value,true);
        using(var foreign=await f.LoginAsync("admin-b"))
        {
            using var denied=await foreign.Client.GetAsync("/GpsHistorico/Arquivo/"+file.ArquivoId);Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            var token=await SecurityHttpFixture.AntiForgeryAsync(foreign.Client,"/Frota");
            using var compact=await foreign.Client.PostAsync("/GpsHistorico/Compactar",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",file.ArquivoId.ToString()!},{"confirmar","true"},{"__RequestVerificationToken",token}}));Assert.Equal(HttpStatusCode.NotFound,compact.StatusCode);
        }
        foreach(var login in new[]{"client-a-other","client-b","driver-a-other"})
        {using var actor=await f.LoginAsync(login);using var denied=await actor.Client.GetAsync($"/api/gps/historico/{input.EntregaId}");Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);}
        await f.ChangeAsync(change=>change.Entregas.Single(e=>e.Id==input.EntregaId).MotoristaId=f.Users["driver-a-other"].Id);
        var unauthorized=await Send(driver,input with{EventoId=Guid.NewGuid()});Assert.Equal("viagem_indisponivel",unauthorized.GetProperty("motivo").GetString());
        await f.ChangeAsync(change=>change.Usuarios.Single(u=>u.Id==f.Users["driver-a"].Id).TenantId=f.TenantB);
        using(var moved=await f.LoginAsync("driver-a")){var collision=await Send(moved,input);Assert.Equal("evento_conflitante",collision.GetProperty("motivo").GetString());}
        await f.ChangeAsync(change=>change.Usuarios.Single(u=>u.Id==f.Users["driver-a"].Id).Ativo=false);
        using var stale=driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{input}});using var revoked=await driver.Client.SendAsync(stale);Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        Assert.Equal(0,await db.PosicoesGps.CountAsync());Assert.Equal(1,await db.GpsEventosArquivados.CountAsync());
    }
    [Fact]
    public async Task LateAuthorizedHistoryCanArriveAfterAnArchiveWithoutRevivingLivePositionOrDuplicateRows()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var(input,_)=await Input(f,DateTimeOffset.UtcNow.AddHours(-1));using var driver=await f.LoginAsync("driver-a");await Send(driver,input);await Close(f);
        await using var db=new MglDbContext(f.Options);var service=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0}));
        var original=await service.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId);await service.CompactAsync(f.Users["admin-a"].Id,original.ArquivoId!.Value,true);
        var late=await Send(driver,input with{EventoId=Guid.NewGuid(),CapturadoEmUtc=input.CapturadoEmUtc.AddMinutes(-10),Latitude=-23.8});
        Assert.Equal("confirmado",late.GetProperty("estado").GetString());Assert.False(late.GetProperty("atualizouPosicao").GetBoolean());
        var next=await service.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId);Assert.True(next.Success);Assert.Equal(1,next.Quantidade);
        var history=await new GpsHistoryReader(db).PageAsync(f.TenantA,input.EntregaId,admin:true);Assert.Equal(2,history.Total);Assert.Equal(2,history.Items.Select(p=>p.Posicao.EventoId).Distinct().Count());
    }
    [Fact]
    public async Task PeriodQueriesShowOutOfOrderGapsQualityAndSimulationOnlyToAdministrators()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var(input,_)=await Input(f,DateTimeOffset.UtcNow.AddMinutes(-20));using var driver=await f.LoginAsync("driver-a");using var admin=await f.LoginAsync("admin-a");
        await Send(driver,input);await Send(driver,input with{EventoId=Guid.NewGuid(),CapturadoEmUtc=input.CapturadoEmUtc.AddMinutes(15)});
        var outOfOrder=input with{EventoId=Guid.NewGuid(),CapturadoEmUtc=input.CapturadoEmUtc.AddMinutes(5),PrecisaoMetros=80};await Send(driver,outOfOrder);
        await using(var hub=await SecurityHubConnection.ConnectAsync(f,admin))Assert.False((await hub.InvokeAsync("SendVehicleTelemetry",f.Deliveries[0].CodigoRastreio,-23d,-46d,30d,90d,10d,true)).TryGetProperty("error",out _));
        var full=await admin.Client.GetFromJsonAsync<JsonElement>($"/GpsHistorico/Dados?entregaId={input.EntregaId}");Assert.Equal(4,full.GetProperty("total").GetInt32());
        Assert.Contains(full.GetProperty("items").EnumerateArray(),p=>p.GetProperty("origem").GetString()=="Simulacao");
        var approximate=full.GetProperty("items").EnumerateArray().Single(p=>p.GetProperty("eventoId").GetGuid()==outOfOrder.EventoId);Assert.True(approximate.GetProperty("foraDeOrdem").GetBoolean());Assert.Equal("aproximada",approximate.GetProperty("qualidade").GetString());
        Assert.Contains(full.GetProperty("items").EnumerateArray(),p=>p.GetProperty("lacunaAnteriorSegundos").ValueKind==JsonValueKind.Number);
        using var customer=await f.LoginAsync("client-a");var privateHistory=await customer.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{input.EntregaId}");Assert.Equal(3,privateHistory.GetProperty("total").GetInt32());
        var start=Uri.EscapeDataString(input.CapturadoEmUtc.AddMinutes(1).ToString("O"));var end=Uri.EscapeDataString(input.CapturadoEmUtc.AddMinutes(6).ToString("O"));
        var filtered=await admin.Client.GetFromJsonAsync<JsonElement>($"/GpsHistorico/Dados?entregaId={input.EntregaId}&de={start}&ate={end}");Assert.Equal(1,filtered.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ArchiveCapacityKeepsAllOnlineRowsAndCorruptColdContentFailsClosedForDownloadCsvHistoryAndReplay()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(gpsRetentionDays:0);var(input,_)=await Input(f);
        await using var db=new MglDbContext(f.Options);var ingest=new GpsIngestionService(db);
        for(int n=0;n<40;n++)Assert.Equal("confirmado",(await ingest.AcceptAsync(f.Users["driver-a"].Id,input with{EventoId=n==0?input.EventoId:Guid.NewGuid(),Latitude=-23.5+n*.00001})).Estado);
        await Close(f);
        var bounded=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{MaximoBytesArquivadosPorOrganizacao=1024,DiasAntesCompactar=0}));
        Assert.False((await bounded.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId)).Success);
        Assert.Equal(40,await db.PosicoesGps.CountAsync());Assert.Equal(0,await db.GpsEventosArquivados.CountAsync());
        var archives=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0}));
        var copy=await archives.ArchiveNextAsync(f.Users["admin-a"].Id,input.EntregaId);Assert.True(copy.Success);
        Assert.True((await archives.CompactAsync(f.Users["admin-a"].Id,copy.ArquivoId!.Value,true)).Success);
        await f.ChangeAsync(change=>change.GpsArquivos.Single().ConteudoGzip=[1,2,3]);
        using var admin=await f.LoginAsync("admin-a");using var driver=await f.LoginAsync("driver-a");
        foreach(var endpoint in new[]{"/GpsHistorico/Dados?entregaId="+input.EntregaId,"/GpsHistorico/Exportar?entregaId="+input.EntregaId,"/GpsHistorico/Arquivo/"+copy.ArquivoId,"/api/gps/historico/"+input.EntregaId})
        {using var response=await admin.Client.GetAsync(endpoint);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.NotEqual("text/csv",response.Content.Headers.ContentType?.MediaType);}
        Assert.Equal("pendente",(await Send(driver,input)).GetProperty("estado").GetString());
        Assert.Equal(0,await db.PosicoesGps.CountAsync());Assert.Equal(40,await db.GpsEventosArquivados.CountAsync());
    }
    [Fact]
    public async Task CaptureOutsideSevenDaysOrAfterClosedShiftCannotBeRewrittenIntoAcceptedHistory()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var(input,_)=await Input(f,DateTimeOffset.UtcNow.AddDays(-8));
        using var driver=await f.LoginAsync("driver-a");Assert.Equal("horario_invalido",(await Send(driver,input)).GetProperty("motivo").GetString());
        await Close(f);await Task.Delay(20);
        var after=input with{EventoId=Guid.NewGuid(),CapturadoEmUtc=DateTimeOffset.UtcNow};
        Assert.Equal("turno_indisponivel",(await Send(driver,after)).GetProperty("motivo").GetString());
        await using var db=new MglDbContext(f.Options);Assert.Equal(0,await db.PosicoesGps.CountAsync());
    }

    [Theory]
    [InlineData("count")][InlineData("tenant")][InlineData("sequence")][InlineData("null")][InlineData("expansion")]
    public void ValidChecksumDoesNotOverrideManifestOrExpansionLimits(string defect)
    {
        var row=new PosicaoGps{Id=1,EventoId=Guid.NewGuid(),TenantId=Guid.NewGuid(),EntregaId=1};
        var archive=new GpsArquivo{TenantId=row.TenantId,EntregaId=1,Quantidade=1,PrimeiraSequencia=1,UltimaSequencia=1};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new[]{row});
        if(defect=="count")archive.Quantidade=2;
        if(defect=="tenant")archive.TenantId=Guid.NewGuid();
        if(defect=="sequence")archive.UltimaSequencia=2;
        if(defect=="null")bytes="[null]"u8.ToArray();
        if(defect=="expansion")bytes=new byte[2*1024*1024+1];
        archive.Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        using var output=new MemoryStream();using(var gzip=new System.IO.Compression.GZipStream(output,System.IO.Compression.CompressionLevel.SmallestSize,true))gzip.Write(bytes);
        archive.ConteudoGzip=output.ToArray();
        Assert.Throws<GpsArchiveIntegrityException>(()=>GpsArchiveService.Verify(archive));
    }

}
