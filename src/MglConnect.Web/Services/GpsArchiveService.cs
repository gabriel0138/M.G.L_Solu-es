using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
namespace MglConnect.Web.Services;

public sealed record GpsArchiveResult(bool Success,string Message,Guid? ArquivoId=null,int Quantidade=0);
public sealed class GpsArchiveIntegrityException(string message):Exception(message);
public sealed class GpsArchiveService(MglDbContext db,IOptions<GpsHistoryOptions>? options=null,TimeProvider? clock=null)
{
    private readonly GpsHistoryOptions _policy=options?.Value??new();
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    private const int MaximumExpandedBytes=2*1024*1024;
    public static PosicaoGps[] Verify(GpsArquivo archive)
    {
        if(archive.ConteudoGzip.Length>MaximumExpandedBytes)throw new GpsArchiveIntegrityException("Arquivo excede o limite de verificação.");
        try
        {
            using var input=new MemoryStream(archive.ConteudoGzip,false);using var gzip=new GZipStream(input,CompressionMode.Decompress);
            using var output=new MemoryStream();var buffer=new byte[8192];int read;
            while((read=gzip.Read(buffer))>0){if(output.Length+read>MaximumExpandedBytes)throw new GpsArchiveIntegrityException("Arquivo expandido excede o limite.");output.Write(buffer,0,read);}
            var bytes=output.ToArray();
            if(Convert.ToHexString(SHA256.HashData(bytes))!=archive.Sha256)throw new GpsArchiveIntegrityException("A integridade SHA-256 do arquivo não confere.");
            var rows=JsonSerializer.Deserialize<PosicaoGps[]>(bytes)??[];
            if(rows.Length!=archive.Quantidade||rows.Length is <1 or >1000||rows.Any(p=>p==null)||rows.Select(p=>p.EventoId).Distinct().Count()!=rows.Length
                ||rows.Select(p=>p.Id).Distinct().Count()!=rows.Length||rows.Any(p=>p.TenantId!=archive.TenantId||p.EntregaId!=archive.EntregaId)
                ||rows.Min(p=>p.Id)!=archive.PrimeiraSequencia||rows.Max(p=>p.Id)!=archive.UltimaSequencia)
                throw new GpsArchiveIntegrityException("O manifesto do arquivo não confere.");
            return rows;
        }
        catch(Exception ex) when(ex is InvalidDataException or JsonException or IOException)
        {throw new GpsArchiveIntegrityException("Arquivo GPS ilegível ou corrompido.");}
    }
    public static bool MatchesLedger(GpsEventoArquivado i,PosicaoGps p)=>i.Id==p.Id&&i.EventoId==p.EventoId
        &&i.TenantId==p.TenantId&&i.EntregaId==p.EntregaId&&i.MotoristaId==p.MotoristaId&&i.VeiculoId==p.VeiculoId
        &&i.AtorUsuarioId==p.AtorUsuarioId&&i.TurnoId==p.TurnoId&&i.CapturadoEmUtc==p.CapturadoEmUtc&&i.RecebidoEmUtc==p.RecebidoEmUtc
        &&i.OrdenadoEmUtc==(p.OrdenadoEmUtc??p.CapturadoEmUtc??p.RecebidoEmUtc)&&i.PrecisaoMetros==p.PrecisaoMetros
        &&i.Origem==p.Origem&&i.CapturaInformada==p.CapturaInformada&&i.ForaDeOrdem==p.ForaDeOrdem
        &&i.ImpressaoRequisicao==GpsFingerprint.Stored(p);
    public async Task<PosicaoGps> ReadEventAsync(GpsEventoArquivado index,CancellationToken ct=default)
    {
        var archive=await db.GpsArquivos.AsNoTracking().SingleAsync(a=>a.Id==index.ArquivoId&&a.TenantId==index.TenantId,ct);
        var row=Verify(archive).SingleOrDefault(p=>p.EventoId==index.EventoId&&p.Id==index.Id);
        if(row==null||!MatchesLedger(index,row))throw new GpsArchiveIntegrityException("Índice e arquivo GPS não correspondem.");
        return row;
    }
    public async Task<GpsArchiveResult> ArchiveNextAsync(int actorId,int deliveryId,CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var actor=await Admin(actorId,ct);if(actor==null)return new(false,"Acesso administrativo indisponível.");
        if(!await Closed(deliveryId,actor.TenantId,ct))return new(false,"Arquivamento exige viagem encerrada, sem reserva ou turno ativo.");
        var rows=await db.PosicoesGps.AsNoTracking().Where(p=>p.EntregaId==deliveryId&&p.TenantId==actor.TenantId
            &&!db.GpsEventosArquivados.Any(a=>a.EventoId==p.EventoId)).OrderBy(p=>p.Id)
            .Take(Math.Clamp(_policy.TamanhoLoteArquivo,1,1000)).ToArrayAsync(ct);
        if(rows.Length==0)return new(true,"Não há posições online sem arquivo verificado.");
        foreach(var row in rows){row.CapturadoEmUtc=row.CapturadoEmUtc.HasValue?GpsIngestionService.Utc(row.CapturadoEmUtc.Value):null;
            row.RecebidoEmUtc=GpsIngestionService.Utc(row.RecebidoEmUtc);row.OrdenadoEmUtc=GpsIngestionService.Utc(row.OrdenadoEmUtc??row.CapturadoEmUtc??row.RecebidoEmUtc);}
        var bytes=JsonSerializer.SerializeToUtf8Bytes(rows);if(bytes.Length>MaximumExpandedBytes)return new(false,"Lote excede o limite de memória do arquivo.");
        using var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionLevel.SmallestSize,true))gzip.Write(bytes);
        var compressed=output.ToArray();
        var stored=await db.GpsArquivos.Where(a=>a.TenantId==actor.TenantId).Select(a=>(long)a.ConteudoGzip.Length).SumAsync(ct);
        if(stored+compressed.Length>Math.Clamp(_policy.MaximoBytesArquivadosPorOrganizacao,1024,128L*1024*1024))
            return new(false,"Capacidade configurada de arquivos atingida. Nenhuma posição foi removida; providencie armazenamento antes de prosseguir.");
        var archive=new GpsArquivo{TenantId=actor.TenantId,EntregaId=deliveryId,CriadoPorUsuarioId=actorId,CriadoEmUtc=_clock.GetUtcNow().UtcDateTime,
            Quantidade=rows.Length,PrimeiraSequencia=rows[0].Id,UltimaSequencia=rows[^1].Id,ConteudoGzip=compressed,Sha256=Convert.ToHexString(SHA256.HashData(bytes))};
        db.GpsArquivos.Add(archive);
        db.GpsEventosArquivados.AddRange(rows.Select(p=>new GpsEventoArquivado{Id=p.Id,EventoId=p.EventoId,TenantId=p.TenantId,EntregaId=p.EntregaId,
            MotoristaId=p.MotoristaId,VeiculoId=p.VeiculoId,AtorUsuarioId=p.AtorUsuarioId,TurnoId=p.TurnoId,ArquivoId=archive.Id,
            CapturadoEmUtc=p.CapturadoEmUtc,RecebidoEmUtc=p.RecebidoEmUtc,OrdenadoEmUtc=p.OrdenadoEmUtc!.Value,PrecisaoMetros=p.PrecisaoMetros,
            Origem=p.Origem,CapturaInformada=p.CapturaInformada,ForaDeOrdem=p.ForaDeOrdem,ImpressaoRequisicao=GpsFingerprint.Stored(p)}));
        await db.SaveChangesAsync(ct);
        var persisted=await db.GpsArquivos.AsNoTracking().SingleAsync(a=>a.Id==archive.Id,ct);Verify(persisted);
        Audit(actorId,actor.TenantId,"GpsArquivoCriado",archive.Id.ToString(),$"Viagem {deliveryId}; {rows.Length} eventos; SHA256 {archive.Sha256}");
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        foreach(var entry in db.ChangeTracker.Entries<GpsEventoArquivado>().Where(e=>e.Entity.ArquivoId==archive.Id).ToArray())entry.State=EntityState.Detached;
        db.Entry(archive).State=EntityState.Detached;
        return new(true,"Lote arquivado e verificado. A cópia online permanece preservada.",archive.Id,rows.Length);
    }
    public async Task<GpsArchiveResult> CompactAsync(int actorId,Guid id,bool confirmed,CancellationToken ct=default)
    {
        if(!confirmed)return new(false,"Confirme explicitamente a compactação da cópia online.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var actor=await Admin(actorId,ct);if(actor==null)return new(false,"Acesso administrativo indisponível.");
        var archive=await db.GpsArquivos.SingleOrDefaultAsync(a=>a.Id==id&&a.TenantId==actor.TenantId,ct);
        if(archive==null)return new(false,"Arquivo não encontrado.");
        if(!await Closed(archive.EntregaId,actor.TenantId,ct))return new(false,"Uma viagem ativa nunca pode ser compactada.");
        var original=Verify(archive);
        var index=await db.GpsEventosArquivados.AsNoTracking().Where(p=>p.ArquivoId==id).ToArrayAsync(ct);
        if(index.Length!=original.Length||original.Any(p=>!index.Any(i=>MatchesLedger(i,p))))
            throw new GpsArchiveIntegrityException("Índice permanente incompleto. Cópia online preservada.");
        if(archive.CompactadoEmUtc.HasValue)return new(true,"Este lote já foi compactado.",id,archive.Quantidade);
        if(_clock.GetUtcNow().UtcDateTime<GpsIngestionService.Utc(archive.CriadoEmUtc).AddDays(Math.Clamp(_policy.DiasAntesCompactar,0,3650)))
            return new(false,"Retenção online ainda não cumprida. O prazo começa na criação do arquivo verificado.");
        var ids=original.Select(p=>p.Id).ToArray();
        var online=await db.PosicoesGps.AsNoTracking().Where(p=>ids.Contains(p.Id)&&p.TenantId==actor.TenantId&&p.EntregaId==archive.EntregaId).ToArrayAsync(ct);
        // Full records, including association and receipt clocks, must still match.
        foreach(var row in online){row.CapturadoEmUtc=row.CapturadoEmUtc.HasValue?GpsIngestionService.Utc(row.CapturadoEmUtc.Value):null;
            row.RecebidoEmUtc=GpsIngestionService.Utc(row.RecebidoEmUtc);row.OrdenadoEmUtc=GpsIngestionService.Utc(row.OrdenadoEmUtc??row.CapturadoEmUtc??row.RecebidoEmUtc);}
        if(online.Length!=original.Length||online.Any(p=>GpsFingerprint.Hash(p)!=GpsFingerprint.Hash(original.Single(o=>o.Id==p.Id))))
            throw new GpsArchiveIntegrityException("A cópia online diverge do arquivo; compactação recusada.");
        var removed=await db.PosicoesGps.Where(p=>ids.Contains(p.Id)&&p.TenantId==actor.TenantId&&p.EntregaId==archive.EntregaId).ExecuteDeleteAsync(ct);
        if(removed!=archive.Quantidade)throw new GpsArchiveIntegrityException("Compactação incompleta; transação cancelada.");
        archive.CompactadoPorUsuarioId=actorId;archive.CompactadoEmUtc=_clock.GetUtcNow().UtcDateTime;archive.Versao++;
        Audit(actorId,actor.TenantId,"GpsArquivoCompactado",id.ToString(),$"{removed} cópias online; arquivo e UUIDs preservados; retenção {_policy.DiasAntesCompactar} dias");
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        db.Entry(archive).State=EntityState.Detached;
        return new(true,"Cópia online compactada. Arquivo verificável, consulta e UUIDs permanecem disponíveis.",id,removed);
    }
    private Task<Usuario?> Admin(int id,CancellationToken ct)=>db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==id&&u.Ativo&&(u.Perfil=="Admin"||u.Perfil=="Administrador"),ct);
    private Task<bool> Closed(int id,Guid tenant,CancellationToken ct)=>db.Entregas.AnyAsync(e=>e.Id==id&&e.TenantId==tenant
        &&!e.AtribuicaoAtiva&&(e.StatusEntrega=="Entregue"||e.StatusEntrega=="Cancelada")&&!db.TurnosMotorista.Any(t=>t.EntregaId==id&&t.Ativo),ct);
    public void Audit(int actor,Guid tenant,string operation,string id,string details)=>db.AuditoriaOperacional.Add(new AuditoriaOperacional{
        TenantId=tenant,AtorUsuarioId=actor,Evento=operation,TipoEntidade="GpsArquivo",IdentificadorEntidade=id,Detalhes=details,OcorridoEmUtc=_clock.GetUtcNow().UtcDateTime});
}
