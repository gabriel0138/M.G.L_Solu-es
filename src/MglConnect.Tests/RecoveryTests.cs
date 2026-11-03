using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Operations;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public sealed class RecoveryTests
{
    private static string Destination()=>Path.Combine(Directory.CreateTempSubdirectory("mgl-stage6-recovery-").FullName,"package");
    internal static async Task<GpsEventInput> Prepare(SecurityHttpFixture f)
    {
        using var driver=await f.LoginAsync("driver-a");
        await using var db=new MglDbContext(f.Options);var trip=f.Deliveries[0];var shift=await db.TurnosMotorista.SingleAsync(t=>t.EntregaId==trip.Id);
        var sample=new GpsEventInput(Guid.NewGuid(),trip.Id,shift.Id,-23.5,-46.6,8,DateTimeOffset.UtcNow);
        foreach(var input in new[]{sample,sample with{EventoId=Guid.NewGuid(),Latitude=-23.51}})
        {using var message=driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{input}});using var response=await driver.Client.SendAsync(message);Assert.Equal("confirmado",(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipts")[0].GetProperty("estado").GetString());}
        using var pod=driver.Api(HttpMethod.Post,$"/api/entregas/{trip.Id}/pod",new{nomeRecebedor="Synthetic recovery receiver",documentoRecebedor="SYNTHETIC-ONLY",localidade="Fixture"});
        using var completed=await driver.Client.SendAsync(pod);Assert.Equal(HttpStatusCode.OK,completed.StatusCode);return sample;
    }

    [Fact]
    public async Task ConsistentOnlineBackupRestoresOrderPodGpsKeysAuthorizationAndOriginalReceiptsInANewHttpServer()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var sample=await Prepare(f);
        await using(var db=new MglDbContext(f.Options))
        {
            var service=new GpsArchiveService(db,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0,TamanhoLoteArquivo=1}));
            var file=await service.ArchiveNextAsync(f.Users["admin-a"].Id,sample.EntregaId);
            Assert.True((await service.CompactAsync(f.Users["admin-a"].Id,file.ArquivoId!.Value,true)).Success);
        }
        using var original=await f.LoginAsync("driver-a");var package=Destination();
        var manifest=await SqliteRecovery.BackupAsync(f.DatabaseFile,package,f.KeysPath);
        Assert.Equal(1,manifest.Rows["POD"]);Assert.Equal(1,manifest.Rows["GpsArquivos"]);
        Assert.Equal(1,manifest.Rows["PosicoesGps"]);Assert.Equal(1,manifest.Rows["GpsEventosArquivados"]);
        Assert.True(manifest.ExternalKeysIncluded);
        await using var restored=await SecurityHttpFixture.CreateAsync(restorePackage:package);
        using var customer=await restored.LoginAsync("client-a");using var other=await restored.LoginAsync("client-a-other");
        using var foreign=await restored.LoginAsync("client-b");using var admin=await restored.LoginAsync("admin-a");
        var order=f.Deliveries[0].PedidoId;
        using(var pdf=await customer.Client.GetAsync($"/Cliente/Pedido/{order}/Comprovante"))
        {Assert.Equal(HttpStatusCode.OK,pdf.StatusCode);Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync()));}
        foreach(var actor in new[]{other,foreign})
        {using var denied=await actor.Client.GetAsync($"/api/gps/historico/{sample.EntregaId}");Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);using var pdf=await actor.Client.GetAsync($"/Cliente/Pedido/{order}/Comprovante");Assert.Equal(HttpStatusCode.NotFound,pdf.StatusCode);}
        using(var csv=await admin.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={sample.EntregaId}"))Assert.Equal(HttpStatusCode.OK,csv.StatusCode);
        using(var message=original.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{sample}}))
        using(var response=await customer.Client.SendAsync(message)) // Explicit original bearer wins over customer cookie.
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("duplicado",(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipts")[0].GetProperty("estado").GetString());}
        var history=await customer.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{sample.EntregaId}");Assert.Equal(2,history.GetProperty("total").GetInt32());
        await restored.RestartAsync();using var again=await restored.LoginAsync("client-a");Assert.Equal(2,(await again.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{sample.EntregaId}")).GetProperty("total").GetInt32());
        await using var verify=new MglDbContext(restored.Options);Assert.Equal("Concluido",(await verify.Pedidos.FindAsync(order))!.Status);
        Assert.Equal(1,await verify.GpsEventosArquivados.CountAsync());
    }

    [Fact]
    public async Task BackupIncludesCommittedWalAndExcludesUncommittedChanges()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);var package=Destination();
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=f.DatabaseFile,Pooling=false}.ToString());await connection.OpenAsync();
        using(var setup=connection.CreateCommand()){setup.CommandText="PRAGMA journal_mode=WAL";Assert.Equal("wal",await setup.ExecuteScalarAsync());}
        await using var tx=await connection.BeginTransactionAsync();await using(var change=connection.CreateCommand())
        {change.Transaction=(SqliteTransaction)tx;change.CommandText="UPDATE Usuarios SET Nome='UNCOMMITTED'";await change.ExecuteNonQueryAsync();}
        await SqliteRecovery.BackupAsync(f.DatabaseFile,package);
        await using var restored=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite($"Data Source={Path.Combine(package,SqliteRecovery.DatabaseName)}").Options);
        Assert.False(await restored.Usuarios.AnyAsync(u=>u.Nome=="UNCOMMITTED"));
    }

    [Theory][InlineData("file")][InlineData("manifest")][InlineData("extra")][InlineData("link")]
    public async Task CorruptionUnexpectedFilesAndLinksCannotBeRestoredOrOverwriteADestination(string defect)
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);var package=Destination();await SqliteRecovery.BackupAsync(f.DatabaseFile,package);
        if(defect=="file")await File.AppendAllTextAsync(Path.Combine(package,SqliteRecovery.DatabaseName),"tampered");
        if(defect=="manifest")await File.WriteAllTextAsync(Path.Combine(package,"manifest.json"),"{}");
        if(defect=="extra")await File.WriteAllTextAsync(Path.Combine(package,"unexpected"),"untrusted");
        if(defect=="link")File.CreateSymbolicLink(Path.Combine(package,"link"),f.DatabaseFile);
        var target=Destination();await Assert.ThrowsAnyAsync<Exception>(()=>SqliteRecovery.RestoreAsync(package,target));Assert.False(Directory.Exists(target));
        Assert.True(File.Exists(f.DatabaseFile));
    }

    [Fact]
    public async Task ExistingDestinationAndNestedRestoreAreRefusedAndPackagePermissionsArePrivate()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);var package=Destination();await SqliteRecovery.BackupAsync(f.DatabaseFile,package);
        await Assert.ThrowsAsync<IOException>(()=>SqliteRecovery.BackupAsync(f.DatabaseFile,package));
        await Assert.ThrowsAsync<IOException>(()=>SqliteRecovery.RestoreAsync(package,package));
        await Assert.ThrowsAsync<IOException>(()=>SqliteRecovery.RestoreAsync(package,Path.Combine(package,"nested")));
        if(!OperatingSystem.IsWindows())Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(Path.Combine(package,SqliteRecovery.DatabaseName)));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task FailureAfterActualArchiveOrCompactionWriteRollsBackAndCanBeRetried(bool compact)
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var sample=await Prepare(f);
        var policy=Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0});Guid? id=null;
        if(compact){await using var db=new MglDbContext(f.Options);id=(await new GpsArchiveService(db,policy).ArchiveNextAsync(f.Users["admin-a"].Id,sample.EntregaId)).ArquivoId;}
        await using(var interrupted=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>(f.Options).AddInterceptors(new Interrupt(compact)).Options))
        {
            var service=new GpsArchiveService(interrupted,policy);
            var error=await Record.ExceptionAsync(async()=>{if(compact)await service.CompactAsync(f.Users["admin-a"].Id,id!.Value,true);else await service.ArchiveNextAsync(f.Users["admin-a"].Id,sample.EntregaId);});
            Assert.NotNull(error);Assert.IsType<IOException>(error.GetBaseException());
        }
        await using var verify=new MglDbContext(f.Options);Assert.Equal(2,await verify.PosicoesGps.CountAsync());
        Assert.Equal(compact?2:0,await verify.GpsEventosArquivados.CountAsync());
        var retry=new GpsArchiveService(verify,policy);id??=(await retry.ArchiveNextAsync(f.Users["admin-a"].Id,sample.EntregaId)).ArquivoId;
        Assert.True((await retry.CompactAsync(f.Users["admin-a"].Id,id!.Value,true)).Success);
        Assert.Equal(0,await verify.PosicoesGps.CountAsync());Assert.Equal(2,await verify.GpsEventosArquivados.CountAsync());
    }

    [Theory][InlineData("receipt")][InlineData("order")][InlineData("sequence")][InlineData("source")]
    public async Task AlteredLedgerOrOnlineCopyCannotPassRecoveryIntegrity(string defect)
    {
        await using var f=await SecurityHttpFixture.CreateAsync();var sample=await Prepare(f);
        await using(var db=new MglDbContext(f.Options))Assert.True((await new GpsArchiveService(db).ArchiveNextAsync(f.Users["admin-a"].Id,sample.EntregaId)).Success);
        await f.ChangeAsync(db=>{
            if(defect=="source")db.PosicoesGps.First().Latitude=-22;
            else {var row=db.GpsEventosArquivados.First();if(defect=="receipt")row.RecebidoEmUtc=row.RecebidoEmUtc.AddMinutes(1);
                else if(defect=="order")row.OrdenadoEmUtc=row.OrdenadoEmUtc.AddMinutes(1);else row.ForaDeOrdem=!(row.ForaDeOrdem??false);}
        });
        var destination=Destination();await Assert.ThrowsAsync<InvalidDataException>(()=>SqliteRecovery.BackupAsync(f.DatabaseFile,destination));
        Assert.False(Directory.Exists(destination));await using var verify=new MglDbContext(f.Options);Assert.Equal(2,await verify.PosicoesGps.CountAsync());
    }
    [Fact]
    public async Task UncoordinatedExternalPodPhotoRefusesToPublishAnIncompleteBackup()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);
        await f.ChangeAsync(db=>db.Entregas.First().FotoComprovantePath="/synthetic/external-photo.png");
        var destination=Destination();await Assert.ThrowsAsync<InvalidDataException>(()=>SqliteRecovery.BackupAsync(f.DatabaseFile,destination));
        Assert.False(Directory.Exists(destination));Assert.True(File.Exists(f.DatabaseFile));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task IncompleteOrExternalEntityKeyFilesCannotProduceAConfirmedBackup(bool externalEntity)
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);
        var keys=Directory.CreateTempSubdirectory("mgl-stage6-key-fixture-").FullName;
        await File.WriteAllTextAsync(Path.Combine(keys,"key.xml"),externalEntity?"<!DOCTYPE key [<!ENTITY secret SYSTEM 'file:///not-to-read'>]><key>&secret;</key>":"<key>");
        var destination=Destination();await Assert.ThrowsAnyAsync<System.Xml.XmlException>(()=>SqliteRecovery.BackupAsync(f.DatabaseFile,destination,keys));Assert.False(Directory.Exists(destination));
    }
    private sealed class Interrupt(bool compact):DbCommandInterceptor
    {
        private void Check(DbCommand command)
        {if(command.CommandText.Contains(compact?"DELETE FROM \"PosicoesGps\"":"INSERT INTO \"GpsEventosArquivados\"",StringComparison.Ordinal))throw new IOException("Injected interruption after actual write.");}
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default)
        {Check(command);return ValueTask.FromResult(result);}
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,CommandExecutedEventData data,int result,CancellationToken ct=default)
        {Check(command);return ValueTask.FromResult(result);}
    }
}
