using System.Diagnostics;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;
using Xunit.Abstractions;
namespace MglConnect.Tests;

public sealed class GpsOperationalHistoryTests(ITestOutputHelper output)
{
    private static async Task Seed(SecurityHttpFixture f,int count)
    {
        await using var db=new MglDbContext(f.Options);await db.Database.OpenConnectionAsync();var connection=db.Database.GetDbConnection();
        await using var tx=await connection.BeginTransactionAsync();await using var command=connection.CreateCommand();command.Transaction=tx;
        command.CommandText="""
            INSERT INTO PosicoesGps (EventoId,TenantId,EntregaId,MotoristaId,VeiculoId,TurnoId,AtorUsuarioId,Latitude,Longitude,PrecisaoMetros,CapturadoEmUtc,RecebidoEmUtc,OrdenadoEmUtc,Origem,CapturaInformada,ForaDeOrdem)
            VALUES (@id,@tenant,@trip,@driver,@vehicle,@shift,@driver,-23.5,-46.6,8,@capture,@receipt,@capture,'GPS',1,0)
            """;
        var shift=await db.TurnosMotorista.AsNoTracking().SingleAsync(t=>t.EntregaId==f.Deliveries[0].Id);
        foreach(var (name,value) in new (string,object)[]{("@id",Guid.NewGuid()),("@tenant",f.TenantA),("@trip",f.Deliveries[0].Id),
            ("@driver",f.Users["driver-a"].Id),("@vehicle",f.Deliveries[0].VeiculoId!.Value),("@shift",shift.Id),
            ("@capture",DateTime.UtcNow.AddDays(-4)),("@receipt",DateTime.UtcNow.AddDays(-2))})
        {var p=command.CreateParameter();p.ParameterName=name;p.Value=value;command.Parameters.Add(p);}
        var start=DateTime.UtcNow.AddDays(-4);for(int n=0;n<count;n++){command.Parameters["@id"].Value=Guid.NewGuid();command.Parameters["@capture"].Value=start.AddSeconds(n*5);await command.ExecuteNonQueryAsync();}
        await tx.CommitAsync();
    }
    [Fact]
    public async Task RepresentativeVolumeUsesTimeIndexStreamsBoundedCsvAndLeavesActiveGpsAvailableDuringArchiving()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(gpsRetentionDays:0);await Seed(f,25001);
        using var admin=await f.LoginAsync("admin-a");using var driver=await f.LoginAsync("driver-a-other");
        var url=$"/GpsHistorico/Dados?entregaId={f.Deliveries[0].Id}";
        var clock=Stopwatch.StartNew();var page=await admin.Client.GetFromJsonAsync<JsonElement>(url);clock.Stop();
        Assert.Equal(25001,page.GetProperty("total").GetInt32());Assert.Equal(200,page.GetProperty("items").GetArrayLength());
        Assert.True(clock.Elapsed<TimeSpan.FromSeconds(15));output.WriteLine($"25,001 positions: indexed HTTP page {clock.ElapsedMilliseconds} ms.");
        var second=await admin.Client.GetFromJsonAsync<JsonElement>(url+"&pagina=2");
        Assert.Equal(200,second.GetProperty("items").GetArrayLength());Assert.Equal(2,second.GetProperty("pagina").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray().Select(p=>p.GetProperty("eventoId").GetGuid()).Intersect(second.GetProperty("items").EnumerateArray().Select(p=>p.GetProperty("eventoId").GetGuid())));
        await using(var db=new MglDbContext(f.Options))
        {
            var query=new GpsHistoryReader(db).Index(f.TenantA,f.Deliveries[0].Id,true,period:new(DateTime.UtcNow.AddDays(-4),DateTime.UtcNow))
                .OrderBy(p=>p.Instante).ThenBy(p=>p.Id).Take(200);
            await db.Database.OpenConnectionAsync();await using var explain=query.CreateDbCommand();explain.CommandText="EXPLAIN QUERY PLAN "+explain.CommandText;
            await using var plan=await explain.ExecuteReaderAsync();var details=new List<string>();while(await plan.ReadAsync())details.Add(plan.GetString(3));
            Assert.Contains(details,d=>d.Contains("IX_PosicoesGps_TenantId_EntregaId_OrdenadoEmUtc_Id"));output.WriteLine(string.Join("\n",details));
        }
        long before=f.WorkingSetBytes();clock.Restart();
        using(var request=new HttpRequestMessage(HttpMethod.Get,$"/GpsHistorico/Exportar?entregaId={f.Deliveries[0].Id}"))
        using(var response=await admin.Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("6",response.Headers.GetValues("X-Gps-Paginas").Single());
            using var text=new StreamReader(await response.Content.ReadAsStreamAsync(),Encoding.UTF8);int lines=0;while(await text.ReadLineAsync()!=null)lines++;
            Assert.Equal(5001,lines);
        }
        clock.Stop();output.WriteLine($"5,000-row streamed export: {clock.ElapsedMilliseconds} ms; process working-set delta {f.WorkingSetBytes()-before} bytes (not a peak measurement).");
        Assert.True(clock.Elapsed<TimeSpan.FromSeconds(15));
        var commands=new ReadCounter();
        await using(var measured=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>(f.Options).AddInterceptors(commands).Options))
        {
            var reader=new GpsHistoryReader(measured);var response=new DefaultHttpContext().Response;response.Body=Stream.Null;
            Assert.Equal(5000,await GpsCsvExport.WriteAsync(response,measured,reader,reader.Index(f.TenantA,f.Deliveries[0].Id,true),f.TenantA,0,5000,CancellationToken.None));
            Assert.InRange(commands.Count,1,130);output.WriteLine($"5,000-row export: {commands.Count} SQL read commands in bounded batches (no query per event).");
        }
        await f.ChangeAsync(db=>{var e=db.Entregas.Single(e=>e.Id==f.Deliveries[0].Id);e.StatusEntrega="Entregue";e.AtribuicaoAtiva=false;
            foreach(var shift in db.TurnosMotorista.Where(t=>t.EntregaId==e.Id)){shift.Ativo=false;shift.EncerradoEmUtc=DateTime.UtcNow;}});
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,"/Frota");
        await using var verify=new MglDbContext(f.Options);var active=f.Deliveries[1];var activeShift=await verify.TurnosMotorista.SingleAsync(t=>t.EntregaId==active.Id);
        var sample=new GpsEventInput(Guid.NewGuid(),active.Id,activeShift.Id,-23.6,-46.7,8,DateTimeOffset.UtcNow);
        using var message=driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{sample}});
        var archive=admin.Client.PostAsync("/GpsHistorico/Arquivar",new FormUrlEncodedContent(new Dictionary<string,string>{{"entregaId",f.Deliveries[0].Id.ToString()},{"__RequestVerificationToken",token}}));
        var ingest=driver.Client.SendAsync(message);await Task.WhenAll(archive,ingest);
        using var archived=await archive;using var ingested=await ingest;Assert.Equal(HttpStatusCode.Redirect,archived.StatusCode);Assert.Equal(HttpStatusCode.OK,ingested.StatusCode);
        using var retry=driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{sample}});using var acknowledged=await driver.Client.SendAsync(retry);
        Assert.Contains((await acknowledged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipts")[0].GetProperty("estado").GetString(),new[]{"confirmado","duplicado"});
        Assert.Equal(1,await verify.PosicoesGps.CountAsync(p=>p.EventoId==sample.EventoId));Assert.Equal(500,await verify.GpsEventosArquivados.CountAsync());
    }
    [Fact]
    public async Task CsvSanitizesTextDatesAndPaginationAndAdministrativeRoutesRejectOtherRolesAndTenants()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();await Seed(f,2);using var admin=await f.LoginAsync("admin-a");
        await f.ChangeAsync(db=>db.Usuarios.Single(u=>u.Login=="driver-a").Nome="  =HYPERLINK(\"https://invalid.local\")");
        var trip=f.Deliveries[0].Id;
        using(var csv=await admin.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={trip}"))
        {
            Assert.Equal(HttpStatusCode.OK,csv.StatusCode);var content=await csv.Content.ReadAsStringAsync();Assert.Contains("'  =HYPERLINK",content);Assert.Contains("T",content);Assert.Contains("Z;",content);
            Assert.DoesNotContain("SenhaHash",content);Assert.DoesNotContain("Cpf",content);Assert.DoesNotContain("Telefone",content);Assert.Equal(3,content.Split("\r\n",StringSplitOptions.RemoveEmptyEntries).Length);
        }
        foreach(var login in new[]{"client-a","driver-a","broker-a"})
        {
            using var other=await f.LoginAsync(login);
            using var denied=await other.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={trip}");Assert.True(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
        }
        using(var foreign=await f.LoginAsync("admin-b"))
        {using var denied=await foreign.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={trip}");Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);}
        using(var invalid=await admin.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={trip}&de=2026-10-10&ate=2026-10-09"))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        for(int n=0;n<2;n++){using var ok=await admin.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={trip}");Assert.Equal(HttpStatusCode.OK,ok.StatusCode);}
        using var limited=await admin.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={trip}");Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);
        await using var db=new MglDbContext(f.Options);Assert.Equal(3,await db.AuditoriaOperacional.CountAsync(a=>a.Evento=="GpsCsvExportado"));
    }
    [Fact]
    public async Task CompactionBetweenIndexReadAndPayloadReadHasNoMissingRecords()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();await Seed(f,3);
        await f.ChangeAsync(db=>{var e=db.Entregas.Single(e=>e.Id==f.Deliveries[0].Id);e.StatusEntrega="Entregue";e.AtribuicaoAtiva=false;foreach(var t in db.TurnosMotorista.Where(t=>t.EntregaId==e.Id))t.Ativo=false;});
        await using var db=new MglDbContext(f.Options);var reader=new GpsHistoryReader(db);var page=await reader.Index(f.TenantA,f.Deliveries[0].Id,true).ToListAsync();
        var service=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0}));var copy=await service.ArchiveNextAsync(f.Users["admin-a"].Id,f.Deliveries[0].Id);
        Assert.True((await service.CompactAsync(f.Users["admin-a"].Id,copy.ArquivoId!.Value,true)).Success);
        var positions=await reader.MaterializeAsync(page,CancellationToken.None);Assert.Equal(3,positions.Count);Assert.All(page,item=>Assert.NotNull(item.ArquivoId));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task StageFourUpgradePreservesGpsAndOnlyDerivesAnOrderingKey(bool knownCapture)
    {
        await using var connection=new SqliteConnection("Data Source=:memory:;Foreign Keys=True");await connection.OpenAsync();
        await using var db=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        var script=db.Database.GenerateCreateScript();
        foreach(var part in script.Split(';',StringSplitOptions.RemoveEmptyEntries))
        {
            if(part.Contains("GpsArquivos")||part.Contains("GpsEventosArquivados")||part.TrimStart().StartsWith("CREATE INDEX")&&part.Contains("OrdenadoEmUtc"))continue;
            var sql=System.Text.RegularExpressions.Regex.Replace(part,"^.*\\\"(?:OrdenadoEmUtc|ForaDeOrdem)\\\"[^\\n]*\\n?","",System.Text.RegularExpressions.RegexOptions.Multiline);
            if(!string.IsNullOrWhiteSpace(sql))
            {
                // DDL originates exclusively in EF metadata, never user values.
                using var command=connection.CreateCommand();command.CommandText=sql+";";await command.ExecuteNonQueryAsync();
            }
        }
        var driver=new Usuario{Nome="Synthetic",Login="upgrade",SenhaHash="unused",Perfil="Motorista"};var vehicle=new Veiculo{Placa="UPG1A11",Modelo="Synthetic"};var customer=new Cliente{RazaoSocial="Synthetic",Cnpj="UPGRADE-ONLY",Telefone="0000000000"};db.AddRange(driver,vehicle,customer);await db.SaveChangesAsync();
        var order=new Pedido{ClienteId=customer.Id,NumeroPedido="UPGRADE-ONLY",Descricao="Synthetic"};db.Add(order);await db.SaveChangesAsync();var trip=new Entrega{PedidoId=order.Id,MotoristaId=driver.Id,VeiculoId=vehicle.Id,CodigoRastreio="UPGRADE-ONLY"};db.Add(trip);await db.SaveChangesAsync();
        var id=Guid.NewGuid();var captured=DateTime.UtcNow.AddMinutes(-10);var received=DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PosicoesGps (EventoId,TenantId,EntregaId,MotoristaId,VeiculoId,AtorUsuarioId,Latitude,Longitude,CapturadoEmUtc,RecebidoEmUtc,Origem,CapturaInformada) VALUES ({id},{Guid.Empty},{trip.Id},{driver.Id},{vehicle.Id},{driver.Id},{-23d},{-46d},{(knownCapture?(DateTime?)captured:null)},{received},{"GPS"},{knownCapture})");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        var row=await db.PosicoesGps.SingleAsync();Assert.Equal(id,row.EventoId);Assert.Equal(knownCapture?captured:(DateTime?)null,row.CapturadoEmUtc);Assert.Equal(knownCapture?captured:received,row.OrdenadoEmUtc);Assert.Null(row.ForaDeOrdem);Assert.Equal(0,await db.GpsArquivos.CountAsync());
    }
    private sealed class ReadCounter:DbCommandInterceptor
    {
        public int Count {get;private set;}
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {Count++;return ValueTask.FromResult(result);}
    }

}
