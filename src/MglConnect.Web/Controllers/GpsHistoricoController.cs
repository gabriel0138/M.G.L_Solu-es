using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;
namespace MglConnect.Web.Controllers;

[Authorize(Roles="Admin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class GpsHistoricoController(MglDbContext db,GpsHistoryReader reader,GpsHistoryAccess access,GpsArchiveService archives,IOptions<GpsHistoryOptions> options,ILogger<GpsHistoricoController> logger):Controller
{
    private GpsHistoryOptions Policy=>options.Value;
    private int ActorId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Index(int? entregaId,string? de,string? ate,CancellationToken ct,string? termo=null,int paginaArquivos=1)
    {
        if(!GpsPeriod.TryParse(de,ate,out _))ModelState.AddModelError("","Período inválido. Os horários são UTC; início deve preceder o fim.");
        var scope=entregaId.HasValue?await access.ResolveAsync(User,entregaId.Value,ct):null;
        if(entregaId.HasValue&&(scope==null||!scope.Admin))return NotFound();
        termo=termo?.Trim();if(termo?.Length>64)termo=termo[..64];
        var trips=await db.Entregas.AsNoTracking().Where(e=>termo==null||termo==""||e.CodigoRastreio.ToLower().Contains(termo.ToLower())||e.Id==entregaId).OrderByDescending(e=>e.Id==entregaId).ThenByDescending(e=>e.Id).Take(100)
            .Select(e=>new GpsTripOption(e.Id,e.CodigoRastreio,e.StatusEntrega)).ToListAsync(ct);
        var fileCount=entregaId.HasValue?await db.GpsArquivos.CountAsync(a=>a.EntregaId==entregaId,ct):0;
        var filePages=Math.Max(1,(int)Math.Ceiling(fileCount/20d));paginaArquivos=Math.Clamp(paginaArquivos,1,filePages);
        var files=entregaId.HasValue?await db.GpsArquivos.AsNoTracking().Where(a=>a.EntregaId==entregaId)
            .OrderByDescending(a=>a.CriadoEmUtc).ThenBy(a=>a.Id).Skip((paginaArquivos-1)*20).Take(20).Select(a=>new GpsArchiveSummary(a.Id,GpsIngestionService.Utc(a.CriadoEmUtc),a.Quantidade,a.Sha256,
                a.CompactadoEmUtc,a.CriadoPorUsuarioId,a.ConteudoGzip.Length)).ToListAsync(ct):[];
        int unarchived=entregaId.HasValue?await db.PosicoesGps.CountAsync(p=>p.EntregaId==entregaId&&!db.GpsEventosArquivados.Any(a=>a.EventoId==p.EventoId),ct):0;
        int recent=scope==null?0:await reader.Index(scope.Actor.TenantId,scope.Delivery.Id,true).CountAsync(p=>p.RecebidoEmUtc>=DateTime.UtcNow.AddHours(-24),ct);
        return View(new GpsHistoryConsoleViewModel{Termo=termo,PaginaArquivos=paginaArquivos,PaginasArquivos=filePages,
            BytesArquivados=await db.GpsArquivos.Select(a=>(long)a.ConteudoGzip.Length).SumAsync(ct),LimiteBytesArquivo=Math.Clamp(Policy.MaximoBytesArquivadosPorOrganizacao,1024,128L*1024*1024),EntregaId=entregaId,De=de,Ate=ate,StatusEntrega=scope?.Delivery.StatusEntrega,Viagens=trips,Arquivos=files,
            PosicoesOnlineSemArquivo=unarchived,RetencaoDias=Math.Clamp(Policy.DiasAntesCompactar,0,3650),LinhasCsv=Math.Clamp(Policy.LinhasPorExportacao,1,10000),
            CotaViagem24h=Math.Clamp(Policy.MaximoPorViagem,1,20000),Recebidas24h=recent});
    }
    [HttpGet]
    public async Task<IActionResult> Dados(int entregaId,string? de,string? ate,int pagina=1,CancellationToken ct=default)
    {
        var scope=await access.ResolveAsync(User,entregaId,ct);if(scope==null||!scope.Admin)return NotFound();
        if(!GpsPeriod.TryParse(de,ate,out var period))return BadRequest(new{erro="Período inválido."});
        GpsHistoryPage result;
        try{result=await reader.PageAsync(scope.Actor.TenantId,entregaId,pagina,admin:true,period:period,ct:ct);}
        catch(GpsArchiveIntegrityException){return Conflict(new{erro="Integridade do arquivo não confirmada."});}
        var items=result.Items.Select(e=>new{sequencia=e.Posicao.Id,eventoId=e.Posicao.EventoId,latitude=e.Posicao.Latitude,longitude=e.Posicao.Longitude,
            precisaoMetros=e.Posicao.PrecisaoMetros,capturadoEmUtc=e.Posicao.CapturadoEmUtc.HasValue?(DateTime?)GpsIngestionService.Utc(e.Posicao.CapturadoEmUtc.Value):null,
            recebidoEmUtc=GpsIngestionService.Utc(e.Posicao.RecebidoEmUtc),origem=e.Posicao.Origem,capturaInformada=e.Posicao.CapturaInformada,
            qualidade=GpsQuality.Classify(e.Posicao.PrecisaoMetros),adequadaMapa=GpsQuality.CanMap(e.Posicao.PrecisaoMetros),foraDeOrdem=e.Posicao.ForaDeOrdem,
            lacunaAnteriorSegundos=e.LacunaAnteriorSegundos,arquivado=e.Arquivado,motorista=e.Motorista,motoristaId=e.Posicao.MotoristaId,placa=e.Placa,veiculo=e.Veiculo});
        return Json(new{entregaId,total=result.Total,pagina=result.Pagina,paginas=result.Paginas,items,ultimaPosicao=scope.Delivery.UltimaLatitudeGps.HasValue && scope.Delivery.UltimaLongitudeGps.HasValue ? (object)new { latitude=scope.Delivery.UltimaLatitudeGps.Value, longitude=scope.Delivery.UltimaLongitudeGps.Value } : null});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Arquivar(int entregaId,CancellationToken ct)
    {
        var scope=await access.ResolveAsync(User,entregaId,ct);if(scope==null||!scope.Admin)return NotFound();
        try{var result=await archives.ArchiveNextAsync(ActorId,entregaId,ct);TempData[result.Success?"MensagemSucesso":"MensagemErro"]=result.Message;logger.LogInformation(new EventId(6101,"GpsArchiveOperation"),"Operação de arquivo GPS: Success={Success}; Quantity={Quantity}; CorrelationId={CorrelationId}",result.Success,result.Quantidade,HttpContext.Items["Mgl.CorrelationId"]);}
        catch(Exception ex) when(ex is DbException or DbUpdateException or GpsArchiveIntegrityException){logger.LogWarning(new EventId(6102,"GpsArchiveFailure"),"Arquivo GPS não confirmado: ErrorType={ErrorType}; CorrelationId={CorrelationId}",ex.GetType().Name,HttpContext.Items["Mgl.CorrelationId"]);TempData["MensagemErro"]="Arquivo não confirmado; nenhuma cópia online foi removida. Confira a integridade e tente novamente.";}
        return RedirectToAction(nameof(Index),new{entregaId});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Compactar(Guid id,bool confirmar,CancellationToken ct)
    {
        var file=await db.GpsArquivos.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id,ct);if(file==null)return NotFound();
        try{var result=await archives.CompactAsync(ActorId,id,confirmar,ct);TempData[result.Success?"MensagemSucesso":"MensagemErro"]=result.Message;logger.LogInformation(new EventId(6101,"GpsArchiveOperation"),"Operação de arquivo GPS: Success={Success}; Quantity={Quantity}; CorrelationId={CorrelationId}",result.Success,result.Quantidade,HttpContext.Items["Mgl.CorrelationId"]);}
        catch(Exception ex) when(ex is DbException or DbUpdateException or GpsArchiveIntegrityException){logger.LogWarning(new EventId(6102,"GpsArchiveFailure"),"Arquivo GPS não confirmado: ErrorType={ErrorType}; CorrelationId={CorrelationId}",ex.GetType().Name,HttpContext.Items["Mgl.CorrelationId"]);TempData["MensagemErro"]="Compactação recusada por conflito ou integridade; a cópia online foi preservada.";}
        return RedirectToAction(nameof(Index),new{entregaId=file.EntregaId});
    }
    [HttpGet]
    public async Task<IActionResult> Arquivo(Guid id,CancellationToken ct)
    {
        var file=await db.GpsArquivos.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id,ct);if(file==null)return NotFound();
        if((await access.ResolveAsync(User,file.EntregaId,ct))?.Admin!=true)return NotFound();
        try{GpsArchiveService.Verify(file);}catch(GpsArchiveIntegrityException){return Conflict("Integridade do arquivo não confirmada.");}
        archives.Audit(ActorId,file.TenantId,"GpsArquivoBaixado",id.ToString(),$"SHA256 {file.Sha256}");await db.SaveChangesAsync(ct);
        Response.Headers["X-Content-SHA256"]=file.Sha256;
        return File(file.ConteudoGzip,"application/gzip",$"gps-{file.EntregaId}-{file.Id:N}.json.gz");
    }
    [HttpGet,EnableRateLimiting("gps-export")]
    public async Task<IActionResult> Exportar(int entregaId,string? de,string? ate,int pagina=1,CancellationToken ct=default)
    {
        var scope=await access.ResolveAsync(User,entregaId,ct);if(scope==null||!scope.Admin)return NotFound();
        if(!GpsPeriod.TryParse(de,ate,out var period))return BadRequest("Período inválido.");
        // Exclude later receipts so concurrent offline ingestion cannot shift CSV offsets.
        var snapshot=DateTime.UtcNow;
        var query=reader.Index(scope.Actor.TenantId,entregaId,true,period:period).Where(p=>p.RecebidoEmUtc<=snapshot);var total=await query.CountAsync(ct);
        var size=Math.Clamp(Policy.LinhasPorExportacao,1,10000);var pages=Math.Max(1,(int)Math.Ceiling(total/(double)size));
        if(pagina<1||pagina>pages)return BadRequest("Página de exportação inválida.");
        var fileIds=await query.OrderBy(p=>p.Instante).ThenBy(p=>p.Id).Skip((pagina-1)*size).Take(size)
            .Where(p=>p.ArquivoId.HasValue).Select(p=>p.ArquivoId!.Value).Distinct().ToListAsync(ct);
        foreach(var fileId in fileIds)
        {
            var file=await db.GpsArquivos.AsNoTracking().SingleAsync(a=>a.Id==fileId,ct);
            try{GpsArchiveService.Verify(file);}catch(GpsArchiveIntegrityException){return Conflict("Arquivo não verificável; exportação recusada.");}
        }
        archives.Audit(ActorId,scope.Actor.TenantId,"GpsCsvIniciado",entregaId.ToString(),$"Página {pagina}/{pages}; UTC {period.De:O} a {period.Ate:O}; máximo {size}");await db.SaveChangesAsync(ct);
        Response.ContentType="text/csv; charset=utf-8";Response.Headers.ContentDisposition=$"attachment; filename=\"gps-{entregaId}-pagina-{pagina}.csv\"";
        Response.Headers["X-Gps-Total"]=total.ToString();Response.Headers["X-Gps-Pagina"]=pagina.ToString();Response.Headers["X-Gps-Paginas"]=pages.ToString();
        int sent;
        try
        {
            sent=await GpsCsvExport.WriteAsync(Response,db,reader,query,scope.Actor.TenantId,(pagina-1)*size,size,ct);
            if(sent!=Math.Min(size,total-(pagina-1)*size))throw new GpsArchiveIntegrityException("Exportação incompleta.");
        }
        catch(GpsArchiveIntegrityException)
        {
            // Never finish a partially streamed download as a successful CSV.
            if(Response.HasStarted){HttpContext.Abort();return new EmptyResult();}
            Response.Clear();return Conflict("Integridade não confirmada; exportação interrompida.");
        }
        archives.Audit(ActorId,scope.Actor.TenantId,"GpsCsvExportado",entregaId.ToString(),$"Página {pagina}; {sent} linhas enviadas");await db.SaveChangesAsync(ct);
        return new EmptyResult();
    }
}
