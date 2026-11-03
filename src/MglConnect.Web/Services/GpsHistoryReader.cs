using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
namespace MglConnect.Web.Services;

public sealed class GpsHistoryIndex
{
    public long Id { get; set; }
    public Guid EventoId { get; set; }
    public Guid TenantId { get; set; }
    public int EntregaId { get; set; }
    public int MotoristaId { get; set; }
    public int VeiculoId { get; set; }
    public Guid? TurnoId { get; set; }
    public DateTime? CapturadoEmUtc { get; set; }
    public DateTime RecebidoEmUtc { get; set; }
    public DateTime Instante { get; set; }
    public double? PrecisaoMetros { get; set; }
    public string Origem { get; set; }="GPS";
    public bool? ForaDeOrdem { get; set; }
    public Guid? ArquivoId { get; set; }
}
public sealed record GpsHistoryPoint(PosicaoGps Posicao,bool Arquivado,double? LacunaAnteriorSegundos,string? Motorista,string? Placa,string? Veiculo);
public sealed record GpsHistoryPage(int Total,int Pagina,int Paginas,IReadOnlyList<GpsHistoryPoint> Items);
public sealed record GpsPeriod(DateTime? De,DateTime? Ate)
{
    public static bool TryParse(string? de,string? ate,out GpsPeriod period)
    {
        bool Parse(string? value,out DateTime? parsed){parsed=null;if(string.IsNullOrWhiteSpace(value))return true;
            if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var result))return false;
            parsed=result.UtcDateTime;return true;}
        var a=Parse(de,out var start);var b=Parse(ate,out var end);period=new(start,end);
        return a&&b&&(!start.HasValue||!end.HasValue||start<=end);
    }
}
public sealed class GpsHistoryReader(MglDbContext db)
{
    public IQueryable<GpsHistoryIndex> Index(Guid tenant,int trip,bool includeSimulation=false,int? driverId=null,GpsPeriod? period=null)
    {
        var hot=db.PosicoesGps.AsNoTracking().Where(p=>p.TenantId==tenant&&p.EntregaId==trip&&p.OrdenadoEmUtc.HasValue)
            .Select(p=>new GpsHistoryIndex{Id=p.Id,EventoId=p.EventoId,TenantId=p.TenantId,EntregaId=p.EntregaId,MotoristaId=p.MotoristaId,VeiculoId=p.VeiculoId,
                TurnoId=p.TurnoId,CapturadoEmUtc=p.CapturadoEmUtc,RecebidoEmUtc=p.RecebidoEmUtc,Instante=p.OrdenadoEmUtc!.Value,
                PrecisaoMetros=p.PrecisaoMetros,Origem=p.Origem,ForaDeOrdem=p.ForaDeOrdem,ArquivoId=null});
        var legacy=db.PosicoesGps.AsNoTracking().Where(p=>p.TenantId==tenant&&p.EntregaId==trip&&!p.OrdenadoEmUtc.HasValue)
            .Select(p=>new GpsHistoryIndex{Id=p.Id,EventoId=p.EventoId,TenantId=p.TenantId,EntregaId=p.EntregaId,MotoristaId=p.MotoristaId,VeiculoId=p.VeiculoId,
                TurnoId=p.TurnoId,CapturadoEmUtc=p.CapturadoEmUtc,RecebidoEmUtc=p.RecebidoEmUtc,Instante=p.CapturadoEmUtc??p.RecebidoEmUtc,
                PrecisaoMetros=p.PrecisaoMetros,Origem=p.Origem,ForaDeOrdem=p.ForaDeOrdem,ArquivoId=null});
        var cold=db.GpsEventosArquivados.AsNoTracking().Where(p=>p.TenantId==tenant&&p.EntregaId==trip&&!db.PosicoesGps.Any(h=>h.EventoId==p.EventoId))
            .Select(p=>new GpsHistoryIndex{Id=p.Id,EventoId=p.EventoId,TenantId=p.TenantId,EntregaId=p.EntregaId,MotoristaId=p.MotoristaId,VeiculoId=p.VeiculoId,
                TurnoId=p.TurnoId,CapturadoEmUtc=p.CapturadoEmUtc,RecebidoEmUtc=p.RecebidoEmUtc,Instante=p.OrdenadoEmUtc,
                PrecisaoMetros=p.PrecisaoMetros,Origem=p.Origem,ForaDeOrdem=p.ForaDeOrdem,ArquivoId=p.ArquivoId});
        var query=hot.Concat(legacy).Concat(cold);
        if(!includeSimulation)query=query.Where(p=>p.Origem=="GPS");
        if(driverId.HasValue)query=query.Where(p=>p.MotoristaId==driverId.Value);
        if(period?.De is {} start)query=query.Where(p=>p.Instante>=start);
        if(period?.Ate is {} end)query=query.Where(p=>p.Instante<=end);
        return query;
    }
    public async Task<GpsHistoryPage> PageAsync(Guid tenant,int trip,int page=1,bool admin=false,int? driverId=null,GpsPeriod? period=null,int size=200,CancellationToken ct=default)
    {
        size=Math.Clamp(size,1,200);var query=Index(tenant,trip,admin,driverId,period);var total=await query.CountAsync(ct);
        var pages=Math.Max(1,(int)Math.Ceiling(total/(double)size));page=Math.Clamp(page,1,pages);
        var index=await query.OrderByDescending(p=>p.Instante).ThenByDescending(p=>p.Id).Skip((page-1)*size).Take(size).ToListAsync(ct);
        index.Reverse();var points=await MaterializeAsync(index,ct);
        var drivers=admin?await db.Usuarios.AsNoTracking().Where(u=>u.TenantId==tenant&&index.Select(p=>p.MotoristaId).Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.Nome,ct):[];
        var vehicles=admin?await db.Veiculos.AsNoTracking().Where(v=>v.TenantId==tenant&&index.Select(p=>p.VeiculoId).Contains(v.Id)).ToDictionaryAsync(v=>v.Id,ct):[];
        // Include the preceding observation outside this page/filter, but infer no cause.
        GpsHistoryIndex? previous=null;
        if(index.Count>0){var first=index[0];previous=await Index(tenant,trip,admin,driverId)
            .Where(p=>p.Instante<first.Instante||(p.Instante==first.Instante&&p.Id<first.Id)).OrderByDescending(p=>p.Instante).ThenByDescending(p=>p.Id).FirstOrDefaultAsync(ct);}
        var items=new List<GpsHistoryPoint>();
        foreach(var entry in index)
        {
            var point=points[entry.Id];double? gap=null;
            if(previous?.CapturadoEmUtc is {} before&&entry.CapturadoEmUtc is {} after&&entry.TurnoId.HasValue&&entry.TurnoId==previous.TurnoId
                &&entry.Origem=="GPS"&&previous.Origem=="GPS"&&(after-before).TotalSeconds>300)gap=(after-before).TotalSeconds;
            vehicles.TryGetValue(entry.VeiculoId,out var vehicle);
            items.Add(new(point,entry.ArquivoId.HasValue,gap,drivers.GetValueOrDefault(entry.MotoristaId),vehicle?.Placa,vehicle?.Modelo));previous=entry;
        }
        return new(total,page,pages,items);
    }
    public async Task<Dictionary<long,PosicaoGps>> MaterializeAsync(IReadOnlyList<GpsHistoryIndex> entries,CancellationToken ct)
    {
        var result=new Dictionary<long,PosicaoGps>();var hot=entries.Where(p=>!p.ArquivoId.HasValue).Select(p=>p.Id).ToArray();
        foreach(var row in await db.PosicoesGps.AsNoTracking().Where(p=>hot.Contains(p.Id)).ToListAsync(ct))result[row.Id]=row;
        // A controlled compaction can commit between index selection and payload
        // loading. Resolve missing hot rows through the permanent archive index.
        var missing=hot.Where(id=>!result.ContainsKey(id)).ToArray();
        if(missing.Length>0)
        {
            var moved=await db.GpsEventosArquivados.AsNoTracking().Where(p=>missing.Contains(p.Id)).Select(p=>new{p.Id,p.ArquivoId}).ToListAsync(ct);
            foreach(var entry in entries)if(moved.FirstOrDefault(p=>p.Id==entry.Id) is {} found)entry.ArquivoId=found.ArquivoId;
        }
        foreach(var group in entries.Where(p=>p.ArquivoId.HasValue).GroupBy(p=>p.ArquivoId!.Value))
        {
            var file=await db.GpsArquivos.AsNoTracking().SingleAsync(a=>a.Id==group.Key,ct);
            var wanted=group.ToDictionary(p=>p.Id);
            foreach(var row in GpsArchiveService.Verify(file))if(wanted.TryGetValue(row.Id,out var expected))
            {
                if(row.EventoId!=expected.EventoId||row.TenantId!=expected.TenantId||row.EntregaId!=expected.EntregaId
                    ||row.MotoristaId!=expected.MotoristaId||row.VeiculoId!=expected.VeiculoId||row.TurnoId!=expected.TurnoId
                    ||row.CapturadoEmUtc!=expected.CapturadoEmUtc||row.RecebidoEmUtc!=expected.RecebidoEmUtc
                    ||row.PrecisaoMetros!=expected.PrecisaoMetros||row.Origem!=expected.Origem
                    ||(row.OrdenadoEmUtc??row.CapturadoEmUtc??row.RecebidoEmUtc)!=expected.Instante||row.ForaDeOrdem!=expected.ForaDeOrdem)
                    throw new GpsArchiveIntegrityException("Índice e conteúdo GPS divergem.");
                result[row.Id]=row;
            }
        }
        if(result.Count!=entries.Count)throw new GpsArchiveIntegrityException("Há posições sem conteúdo verificável; consulta interrompida.");
        return result;
    }
}
