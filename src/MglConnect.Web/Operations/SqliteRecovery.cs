using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;

namespace MglConnect.Web.Operations;

public sealed record RecoveryFile(string Path, long Bytes, string Sha256);
public sealed record RecoveryManifest(int Version, string Provider, DateTime CreatedAtUtc,
    Dictionary<string,int> Rows, List<RecoveryFile> Files, bool ExternalKeysIncluded);

// Local operator tool only. Not registered as an HTTP service or exposed to tenants.
public static class SqliteRecovery
{
    public const string DatabaseName = "database.sqlite";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const int MaxFiles = 10000;

    public static async Task<RecoveryManifest> BackupAsync(string database, string destination, string? keys = null)
    {
        RequireFile(database);
        var staging = NewStaging(destination);
        var snapshot = Path.Combine(staging, DatabaseName);
        // Online Backup API includes committed WAL pages. Never copy a live .db file.
        using (File.Open(snapshot, FileMode.CreateNew, FileAccess.Write)) { }
        PrivateFile(snapshot);
        await using (var source = Open(database, readOnly:true))
        await using (var target = Open(snapshot, readOnly:false))
        {
            await source.OpenAsync(); await target.OpenAsync();
            source.BackupDatabase(target);
        }
        var counts = await ValidateDatabaseAsync(snapshot);
        await using (var db = Context(snapshot))
        {
            if (await db.Entregas.AnyAsync(e => e.FotoComprovantePath != null && e.FotoComprovantePath != ""))
                throw new InvalidDataException("POD com referência externa exige backup coordenado de anexos; pacote não publicado.");
            Directory.CreateDirectory(Path.Combine(staging,"gps"));
            // One bounded archive at a time; never load all compressed payloads together.
            var ids = await db.GpsArquivos.Select(a => a.Id).ToListAsync();
            foreach (var id in ids)
            {
                if (ids.Count > MaxFiles-100) throw new InvalidDataException("Quantidade de arquivos excede o limite do pacote.");
                var archive = await db.GpsArquivos.AsNoTracking().SingleAsync(a => a.Id==id);
                var path = Path.Combine(staging,"gps",id.ToString("N")+".json.gz");
                await File.WriteAllBytesAsync(path,archive.ConteudoGzip); PrivateFile(path);
            }
        }
        if (keys != null)
        {
            RequireDirectory(keys);
            var output = Path.Combine(staging,"keys"); Directory.CreateDirectory(output); PrivateDirectory(output);
            if(Directory.EnumerateDirectories(keys).Any())throw new InvalidDataException("Subdiretórios de chaves não suportados neste pacote.");
            foreach (var source in Directory.EnumerateFiles(keys))
            {
                RequireFile(source);
                if (new FileInfo(source).Length > 1024*1024) throw new InvalidDataException("Chave excede o limite do pacote.");
                var target = Path.Combine(output,Path.GetFileName(source));
                File.Copy(source,target,overwrite:false); PrivateFile(target);
                VerifyKey(target);
            }
            if (!Directory.EnumerateFiles(output).Any()) throw new InvalidDataException("Diretório de chaves vazio.");
        }
        var entries = new List<RecoveryFile>();
        foreach (var file in SafeFiles(staging))
            entries.Add(await Describe(staging,file));
        var manifest = new RecoveryManifest(1,"Sqlite",DateTime.UtcNow,counts,entries,keys!=null);
        var manifestPath = Path.Combine(staging,"manifest.json");
        await File.WriteAllTextAsync(manifestPath,JsonSerializer.Serialize(manifest,Json)); PrivateFile(manifestPath);
        await VerifyAsync(staging);
        Directory.Move(staging,Path.GetFullPath(destination)); // No overwrite; atomic publication on the same filesystem.
        return manifest;
    }

    public static async Task<RecoveryManifest> VerifyAsync(string package)
    {
        RequireDirectory(package);
        var manifestPath = Path.Combine(package,"manifest.json"); RequireFile(manifestPath);
        if (new FileInfo(manifestPath).Length > 2*1024*1024) throw new InvalidDataException("Manifesto excede o limite.");
        var manifest = JsonSerializer.Deserialize<RecoveryManifest>(await File.ReadAllTextAsync(manifestPath),Json)
            ?? throw new InvalidDataException("Manifesto ausente.");
        if (manifest.Version!=1 || manifest.Provider!="Sqlite" || manifest.Files==null || manifest.Rows==null || manifest.Files.Count is <1 or >MaxFiles
            || manifest.Files.Select(f=>f.Path).Distinct(StringComparer.Ordinal).Count()!=manifest.Files.Count
            || !manifest.Files.Any(f=>f.Path==DatabaseName)) throw new InvalidDataException("Manifesto inválido.");
        var actual = SafeFiles(package).Take(MaxFiles+2).Select(f=>Path.GetRelativePath(package,f).Replace('\\','/'))
            .Where(f=>f!="manifest.json").Order().ToArray();
        if (!actual.SequenceEqual(manifest.Files.Select(f=>f.Path).Order())) throw new InvalidDataException("Inventário do pacote diverge.");
        foreach (var entry in manifest.Files)
        {
            if (Path.IsPathRooted(entry.Path) || entry.Path.Split('/').Any(p=>p is ".." or "." or ""))
                throw new InvalidDataException("Caminho inválido no manifesto.");
            var file = Path.Combine(package,entry.Path); RequireFile(file);
            if (await Describe(package,file)!=entry) throw new InvalidDataException("Checksum ou tamanho do pacote diverge.");
        }
        var snapshot = Path.Combine(package,DatabaseName);
        var counts = await ValidateDatabaseAsync(snapshot);
        if (!counts.OrderBy(p=>p.Key).SequenceEqual(manifest.Rows.OrderBy(p=>p.Key))) throw new InvalidDataException("Contagens divergem do manifesto.");
        await using var db = Context(snapshot);
        foreach (var id in await db.GpsArquivos.Select(a=>a.Id).ToListAsync())
        {
            var archive = await db.GpsArquivos.AsNoTracking().SingleAsync(a=>a.Id==id);
            var sidecar = Path.Combine(package,"gps",id.ToString("N")+".json.gz");
            RequireFile(sidecar);
            var external = await File.ReadAllBytesAsync(sidecar);
            if (!archive.ConteudoGzip.SequenceEqual(external)) throw new InvalidDataException("Arquivo GPS externo diverge do banco.");
        }
        foreach(var key in actual.Where(f=>f.StartsWith("keys/",StringComparison.Ordinal)))VerifyKey(Path.Combine(package,key));
        if (manifest.ExternalKeysIncluded && !actual.Any(f=>f.StartsWith("keys/",StringComparison.Ordinal)))
            throw new InvalidDataException("Chaves declaradas não encontradas.");
        return manifest;
    }

    public static async Task RestoreAsync(string package,string destination)
    {
        if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(package).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new IOException("Restauração dentro do pacote de origem recusada.");
        await VerifyAsync(package);
        var staging = NewStaging(destination);
        foreach (var source in SafeFiles(package))
        {
            var path = Path.Combine(staging,Path.GetRelativePath(package,source));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); PrivateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(source,path,overwrite:false); PrivateFile(path);
        }
        // Reverify the copied bytes, closing the source-change window before publication.
        await VerifyAsync(staging);
        Directory.Move(staging,Path.GetFullPath(destination));
    }

    public static async Task<Dictionary<string,int>> ValidateDatabaseAsync(string database)
    {
        RequireFile(database);
        await using var connection = Open(database,readOnly:true); await connection.OpenAsync();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="PRAGMA integrity_check";
            if (!Equals(await command.ExecuteScalarAsync(),"ok")) throw new InvalidDataException("Integridade SQLite inválida.");
            command.CommandText="PRAGMA foreign_key_check";
            await using var result=await command.ExecuteReaderAsync();
            if(await result.ReadAsync())throw new InvalidDataException("Referência relacional inválida.");
        }
        await using var db=Context(database);
        var counts = new Dictionary<string,int> {
            ["Usuarios"]=await db.Usuarios.CountAsync(),["Clientes"]=await db.Clientes.CountAsync(),
            ["Pedidos"]=await db.Pedidos.CountAsync(),["Entregas"]=await db.Entregas.CountAsync(),
            ["Veiculos"]=await db.Veiculos.CountAsync(),["EventosRastreio"]=await db.EventosRastreio.CountAsync(),
            ["POD"]=await db.Entregas.CountAsync(e=>e.DataHoraRecebimento!=null),
            ["TurnosMotorista"]=await db.TurnosMotorista.CountAsync(),["PosicoesGps"]=await db.PosicoesGps.CountAsync(),
            ["GpsArquivos"]=await db.GpsArquivos.CountAsync(),["GpsEventosArquivados"]=await db.GpsEventosArquivados.CountAsync(),
            ["AuditoriaOperacional"]=await db.AuditoriaOperacional.CountAsync(),["DataProtectionKeys"]=await db.DataProtectionKeys.CountAsync()
        };
        await using(var schema=connection.CreateCommand())
        {
            schema.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RotaAvaliacoes'";
            if(Convert.ToInt32(await schema.ExecuteScalarAsync())>0)
            {
                counts["RotaAvaliacoes"]=await db.RotaAvaliacoes.CountAsync();
                var ids=await db.RotaAvaliacoes.Select(a=>a.Id).ToListAsync();
                foreach(var id in ids)
                {
                    var record=await db.RotaAvaliacoes.AsNoTracking().SingleAsync(a=>a.Id==id);
                    MglConnect.Web.Services.RouteTrust.RouteTrustResult? result;
                    try{result=JsonSerializer.Deserialize<MglConnect.Web.Services.RouteTrust.RouteTrustResult>(record.ResultadoJson);}catch(JsonException){throw new InvalidDataException("Registro de avaliação de rota ilegível.");}
                    if(result==null||result.CalculationId!=record.Id||result.RulesVersion!=record.RegrasHash||result.Geometry==null
                        ||result.Geometry.Count>20000||result.Geometry.Any(p=>!double.IsFinite(p.Latitude)||!double.IsFinite(p.Longitude))
                        ||result.Geometry.Count>0&&MglConnect.Web.Services.RouteTrust.RouteFingerprint.Geometry(result.Geometry)!=result.GeometrySha256
                        ||result.NavigationAllowed&&(result.Synthetic||result.Obsolete||result.Confidence!=MglConnect.Web.Services.RouteTrust.RouteConfidence.Validada))
                        throw new InvalidDataException("Avaliação de rota inconsistente; pacote não confirmado.");
                }
            }
        }
        await using(var profilesSchema=connection.CreateCommand())
        {
            profilesSchema.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PerfisViagemRevisoes'";
            if(Convert.ToInt32(await profilesSchema.ExecuteScalarAsync())>0)
            {
                counts["PerfisViagemRevisoes"]=await db.PerfisViagemRevisoes.CountAsync();
                await foreach(var revision in db.PerfisViagemRevisoes.AsNoTracking().AsAsyncEnumerable())
                {
                    var p=MglConnect.Web.Services.RouteTrust.OperationalTruckProfile.Read(revision.PerfilJson);
                    if(p.ReadError!=null||p.TripId!=revision.EntregaId||p.VehicleId!=revision.VeiculoId||revision.Revisao<1)
                        throw new InvalidDataException("Revisão de perfil de viagem inconsistente; pacote não confirmado.");
                    try{using var physical=JsonDocument.Parse(revision.CadastroVeiculoJson);if(physical.RootElement.GetProperty("Id").GetInt32()!=revision.VeiculoId)throw new InvalidDataException("Snapshot de cadastro incompatível.");}
                    catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException){throw new InvalidDataException("Snapshot de cadastro ilegível.");}
                }
            }
        }
        int ledger=0;
        foreach(var id in await db.GpsArquivos.Select(a=>a.Id).ToListAsync())
        {
            var archive=await db.GpsArquivos.AsNoTracking().SingleAsync(a=>a.Id==id);
            var positions=GpsArchiveService.Verify(archive);
            var index=await db.GpsEventosArquivados.AsNoTracking().Where(p=>p.ArquivoId==id).ToListAsync();
            if(index.Count!=positions.Length || positions.Any(p=>!index.Any(i=>GpsArchiveService.MatchesLedger(i,p))))
                throw new InvalidDataException("Índice permanente GPS diverge do arquivo.");
            var ids=positions.Select(p=>p.Id).ToArray();
            var online=await db.PosicoesGps.AsNoTracking().Where(p=>ids.Contains(p.Id)).ToListAsync();
            foreach(var point in online)
            {
                point.CapturadoEmUtc=point.CapturadoEmUtc.HasValue?GpsIngestionService.Utc(point.CapturadoEmUtc.Value):null;
                point.RecebidoEmUtc=GpsIngestionService.Utc(point.RecebidoEmUtc);
                point.OrdenadoEmUtc=GpsIngestionService.Utc(point.OrdenadoEmUtc??point.CapturadoEmUtc??point.RecebidoEmUtc);
            }
            if(online.Count!=(archive.CompactadoEmUtc.HasValue?0:positions.Length)
                ||online.Any(p=>GpsFingerprint.Hash(p)!=GpsFingerprint.Hash(positions.Single(o=>o.Id==p.Id))))
                throw new InvalidDataException("Cópia online diverge do arquivo GPS.");
            ledger+=index.Count;
        }
        if(ledger!=counts["GpsEventosArquivados"])throw new InvalidDataException("Índice GPS incompleto.");
        return counts;
    }

    private static void VerifyKey(string path)
    {
        if(Path.GetExtension(path)!=".xml"||new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Arquivo de chave inválido.");
        using var reader=System.Xml.XmlReader.Create(path,new System.Xml.XmlReaderSettings{
            DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
        var document=System.Xml.Linq.XDocument.Load(reader);
        if(document.Root?.Name.LocalName is not ("key" or "revocation"))throw new InvalidDataException("XML de chave inválido.");
    }
    private static MglDbContext Context(string database)=>new(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(Open(database,true).ConnectionString).Options);
    private static SqliteConnection Open(string path,bool readOnly)=>new(new SqliteConnectionStringBuilder {
        DataSource=Path.GetFullPath(path),Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=15}.ToString());
    private static async Task<RecoveryFile> Describe(string root,string file)
    {
        await using var stream=File.OpenRead(file);
        return new(Path.GetRelativePath(root,file).Replace('\\','/'),stream.Length,Convert.ToHexString(await SHA256.HashDataAsync(stream)));
    }
    private static string NewStaging(string destination)
    {
        destination=Path.GetFullPath(destination);
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("Destino já existe; sobrescrita recusada.");
        RequireDirectory(Path.GetDirectoryName(destination)!);
        var staging=Path.Combine(Path.GetDirectoryName(destination)!,".mgl-incomplete-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging); PrivateDirectory(staging);return staging;
    }
    private static IEnumerable<string> SafeFiles(string root,int depth=0)
    {
        if(depth>8)throw new InvalidDataException("Profundidade do pacote excedida.");
        RequireDirectory(root);
        foreach(var child in Directory.EnumerateFileSystemEntries(root))
        {
            if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Links simbólicos não são permitidos em pacotes.");
            if(Directory.Exists(child)){foreach(var file in SafeFiles(child,depth+1))yield return file;}
            else {RequireFile(child);yield return child;}
        }
    }
    private static void NoLinks(string path)
    {
        for(var current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Links simbólicos não são permitidos.");
    }
    private static void RequireFile(string path){if(!File.Exists(path))throw new FileNotFoundException("Arquivo operacional ausente.");NoLinks(path);}
    private static void RequireDirectory(string path){if(!Directory.Exists(path))throw new DirectoryNotFoundException("Diretório operacional ausente.");NoLinks(path);}
    private static void PrivateFile(string path){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
    private static void PrivateDirectory(string path){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}
}
