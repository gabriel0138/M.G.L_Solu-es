using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
namespace MglConnect.Web.Services;

public static class GpsCsvExport
{
    public static string Text(string? value)
    {
        value??="";var trimmed=value.TrimStart();
        if(value.Any(c=>c is '\t' or '\r' or '\n')||trimmed.Length>0&&"=+-@".Contains(trimmed[0]))value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    private static string Date(DateTime? value)=>value.HasValue?GpsIngestionService.Utc(value.Value).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",CultureInfo.InvariantCulture):"";
    public static async Task<int> WriteAsync(HttpResponse response,MglDbContext db,GpsHistoryReader reader,IQueryable<GpsHistoryIndex> query,Guid tenant,int offset,int limit,CancellationToken ct)
    {
        await response.Body.WriteAsync(Encoding.UTF8.GetPreamble(),ct);
        await response.WriteAsync("EventoId;Sequencia;EntregaId;MotoristaId;Motorista_cadastro_atual;VeiculoId;Placa_cadastro_atual;Origem;Qualidade;Latitude;Longitude;Precisao_m;Capturado_UTC;Recebido_UTC;Fora_de_ordem;Arquivado\r\n",ct);
        int sent=0;
        while(sent<limit)
        {
            var index=await query.OrderBy(p=>p.Instante).ThenBy(p=>p.Id).Skip(offset+sent).Take(Math.Min(200,limit-sent)).ToListAsync(ct);
            if(index.Count==0)break;var rows=await reader.MaterializeAsync(index,ct);
            var driverIds=index.Select(i=>i.MotoristaId).Distinct().ToArray();var vehicleIds=index.Select(i=>i.VeiculoId).Distinct().ToArray();
            var names=await db.Usuarios.AsNoTracking().Where(u=>u.TenantId==tenant&&driverIds.Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.Nome,ct);
            var plates=await db.Veiculos.AsNoTracking().Where(v=>v.TenantId==tenant&&vehicleIds.Contains(v.Id)).ToDictionaryAsync(v=>v.Id,v=>v.Placa,ct);
            foreach(var entry in index)
            {
                var row=rows[entry.Id];var fields=new[]{Text(row.EventoId.ToString()),row.Id.ToString(CultureInfo.InvariantCulture),row.EntregaId.ToString(CultureInfo.InvariantCulture),
                    row.MotoristaId.ToString(CultureInfo.InvariantCulture),Text(names.GetValueOrDefault(row.MotoristaId)),row.VeiculoId.ToString(CultureInfo.InvariantCulture),
                    Text(plates.GetValueOrDefault(row.VeiculoId)),Text(row.Origem),Text(GpsQuality.Classify(row.PrecisaoMetros)),
                    row.Latitude.ToString("R",CultureInfo.InvariantCulture),row.Longitude.ToString("R",CultureInfo.InvariantCulture),row.PrecisaoMetros?.ToString("R",CultureInfo.InvariantCulture)??"",
                    Date(row.CapturadoEmUtc),Date(row.RecebidoEmUtc),row.ForaDeOrdem?.ToString().ToLowerInvariant()??"nao_classificada",entry.ArquivoId.HasValue?"true":"false"};
                await response.WriteAsync(string.Join(';',fields)+"\r\n",ct);sent++;
            }
            await response.Body.FlushAsync(ct);
        }
        return sent;
    }
}
