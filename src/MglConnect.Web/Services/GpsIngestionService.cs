using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services;

public sealed class GpsIngestionService(MglDbContext db, TimeProvider? clock = null, IOptions<GpsHistoryOptions>? options = null, ControlledRouteDemo? demo = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly GpsHistoryOptions _limits = options?.Value ?? new();
    public Task<GpsReceipt> AcceptAsync(int actorId, GpsEventInput input,
        bool legacy = false, CancellationToken cancellationToken = default)
        => AcceptCoreAsync(actorId,input,legacy,false,cancellationToken);

    // Only the gated server-owned circuit endpoint calls this; public GPS ingestion
    // retains its prohibition on driver-supplied simulated coordinates.
    internal Task<GpsReceipt> AcceptControlledDemoAsync(int actorId, GpsEventInput input, CancellationToken ct)
        => AcceptCoreAsync(actorId,input,false,demo?.Enabled==true && input.Simulado,ct);

    private async Task<GpsReceipt> AcceptCoreAsync(int actorId,GpsEventInput input,bool legacy,bool controlledDemo,CancellationToken cancellationToken)
    {
        GpsReceipt Reject(string reason) => new(input.EventoId, "rejeitado", reason);
        // PostgreSQL timestamps retain microseconds, while .NET supports 100ns.
        // Canonicalize BEFORE both duplicate checks and persistent fingerprints.
        bool microseconds=db.Database.ProviderName?.Contains("Npgsql",StringComparison.Ordinal)==true;
        DateTime StorageUtc(DateTime value)=>microseconds?new DateTime(value.Ticks-value.Ticks%10,DateTimeKind.Utc):value;
        var now = StorageUtc(_clock.GetUtcNow().UtcDateTime);
        var captured = StorageUtc(input.CapturadoEmUtc.UtcDateTime);
        if(microseconds)input=input with{CapturadoEmUtc=new DateTimeOffset(captured)};
        if (input.EventoId == Guid.Empty || input.EntregaId <= 0
            || !double.IsFinite(input.Latitude) || input.Latitude is < -90 or > 90
            || !double.IsFinite(input.Longitude) || input.Longitude is < -180 or > 180
            || (input.PrecisaoMetros is {} accuracy && (!double.IsFinite(accuracy) || accuracy is < 0 or > GpsQuality.MaximumAuditAccuracy))
            || (input.VelocidadeKmH is {} speed && (!double.IsFinite(speed) || speed is < 0 or > 200))
            || (input.Direcao is {} heading && (!double.IsFinite(heading) || heading is < 0 or >= 360)))
            return Reject("dados_invalidos");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var actor = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId && u.Ativo, cancellationToken);
        bool admin = legacy && actor?.Perfil is "Admin" or "Administrador";
        if (actor == null || (!admin && actor.Perfil != "Motorista")) return Reject("autorizacao_revogada");
        if (input.Simulado && !admin && !controlledDemo) return Reject("simulacao_nao_permitida");
        var existing = await db.PosicoesGps.AsNoTracking().SingleOrDefaultAsync(p => p.EventoId == input.EventoId, cancellationToken);
        if (existing != null)
        {
            if (existing.AtorUsuarioId != actorId || existing.TenantId != actor.TenantId || existing.EntregaId != input.EntregaId
                || existing.TurnoId != input.TurnoId || existing.Latitude != input.Latitude || existing.Longitude != input.Longitude
                || existing.PrecisaoMetros != input.PrecisaoMetros || existing.VelocidadeKmH != input.VelocidadeKmH
                || existing.Direcao != input.Direcao || existing.CapturadoEmUtc != (legacy ? (DateTime?)null : captured) || (existing.Origem == "Simulacao") != input.Simulado)
                return Reject("evento_conflitante");
            var code = await db.Entregas.Where(e => e.Id == existing.EntregaId && e.TenantId == actor.TenantId)
                .Select(e => e.CodigoRastreio).SingleAsync(cancellationToken);
            return Receipt(existing, code, "duplicado", false);
        }
        var archived=await db.GpsEventosArquivados.AsNoTracking().SingleOrDefaultAsync(p=>p.EventoId==input.EventoId,cancellationToken);
        if(archived!=null)
        {
            if(archived.AtorUsuarioId!=actorId||archived.TenantId!=actor.TenantId
                ||archived.ImpressaoRequisicao!=GpsFingerprint.Input(actorId,actor.TenantId,input,legacy))return Reject("evento_conflitante");
            var saved=await new GpsArchiveService(db,options,_clock).ReadEventAsync(archived,cancellationToken);
            var archivedCode=await db.Entregas.Where(e=>e.Id==archived.EntregaId&&e.TenantId==actor.TenantId).Select(e=>e.CodigoRastreio).SingleAsync(cancellationToken);
            return Receipt(saved,archivedCode,"duplicado",false);
        }
        if(await db.GpsEventosArquivados.IgnoreQueryFilters().AnyAsync(p=>p.EventoId==input.EventoId,cancellationToken)
            ||await db.PosicoesGps.IgnoreQueryFilters().AnyAsync(p=>p.EventoId==input.EventoId,cancellationToken))return Reject("evento_conflitante");
        if(captured<now.AddHours(-Math.Clamp(_limits.HorasMaximasSincronizacao,1,168))||captured>now.AddSeconds(30))return Reject("horario_invalido");
        var delivery=await db.Entregas.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==input.EntregaId&&e.TenantId==actor.TenantId
            &&e.MotoristaId!=null&&e.VeiculoId!=null,cancellationToken);
        if(delivery==null||(!admin&&delivery.MotoristaId!=actorId))return Reject("viagem_indisponivel");
        bool live=delivery.AtribuicaoAtiva&&delivery.StatusEntrega=="Em transporte";
        var shift=await db.TurnosMotorista.AsNoTracking().SingleOrDefaultAsync(t=>t.EntregaId==delivery.Id&&t.MotoristaId==delivery.MotoristaId
            &&t.TenantId==actor.TenantId&&(legacy?t.Ativo:t.Id==input.TurnoId),cancellationToken);
        bool historical=!legacy&&shift!=null&&!shift.Ativo&&shift.EncerradoEmUtc.HasValue
            &&captured<=GpsIngestionService.Utc(shift.EncerradoEmUtc.Value)
            &&captured>=GpsIngestionService.Utc(shift.IniciadoEmUtc).AddSeconds(-5);
        if(!historical&&!(admin&&input.Simulado)&&(!live||shift==null||!shift.Ativo||shift.VeiculoId!=delivery.VeiculoId
            ||captured<GpsIngestionService.Utc(shift.IniciadoEmUtc).AddSeconds(-5)))return Reject("turno_indisponivel");
        if(admin&&input.Simulado&&(!delivery.AtribuicaoAtiva||delivery.StatusEntrega is not ("Pendente" or "Em transporte")))return Reject("viagem_indisponivel");
        var vehicleId=historical?shift!.VeiculoId:delivery.VeiculoId!.Value;
        if(!await db.Usuarios.AnyAsync(u=>u.Id==delivery.MotoristaId&&u.Ativo&&u.Perfil=="Motorista"&&u.TenantId==actor.TenantId,cancellationToken)
            ||!await db.Veiculos.AnyAsync(v=>v.Id==vehicleId&&(historical||v.Ativo)&&v.TenantId==actor.TenantId,cancellationToken))return Reject("recurso_indisponivel");
        var window=now.AddHours(-24);
        var recent=db.PosicoesGps.Where(p=>p.TenantId==actor.TenantId&&p.RecebidoEmUtc>=window);
        var archivedRecent=db.GpsEventosArquivados.Where(p=>p.TenantId==actor.TenantId&&p.RecebidoEmUtc>=window
            &&!db.PosicoesGps.Any(h=>h.EventoId==p.EventoId));
        if(await recent.CountAsync(p=>p.EntregaId==delivery.Id,cancellationToken)+await archivedRecent.CountAsync(p=>p.EntregaId==delivery.Id,cancellationToken)>=Math.Clamp(_limits.MaximoPorViagem,1,20000)
            ||await recent.CountAsync(cancellationToken)+await archivedRecent.CountAsync(cancellationToken)>=Math.Clamp(_limits.MaximoPorOrganizacao,1,1000000))return new GpsReceipt(input.EventoId,"pendente","limite_diario");
        bool outOfOrder=!legacy&&(await db.PosicoesGps.AnyAsync(p=>p.EntregaId==delivery.Id&&p.CapturadoEmUtc>captured,cancellationToken)
            ||await db.GpsEventosArquivados.AnyAsync(p=>p.EntregaId==delivery.Id&&p.CapturadoEmUtc>captured,cancellationToken));
        var row = new PosicaoGps
        {
            EventoId = input.EventoId, TenantId = actor.TenantId, EntregaId = delivery.Id,
            MotoristaId = delivery.MotoristaId!.Value, VeiculoId = vehicleId,
            TurnoId = legacy ? shift?.Id : input.TurnoId, AtorUsuarioId = actorId,
            Latitude = input.Latitude, Longitude = input.Longitude, PrecisaoMetros = input.PrecisaoMetros,
            VelocidadeKmH = input.VelocidadeKmH, Direcao = input.Direcao,
            CapturadoEmUtc = legacy ? null : captured, OrdenadoEmUtc = legacy ? now : captured,
            ForaDeOrdem = legacy ? null : outOfOrder, RecebidoEmUtc = now, Origem = input.Simulado ? "Simulacao" : "GPS", CapturaInformada = !legacy
        };
        db.PosicoesGps.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        bool updated = false;
        if (!input.Simulado && live && !historical && (input.PrecisaoMetros == null || input.PrecisaoMetros <= 50))
        {
            var target = db.Entregas.Where(e => e.Id == delivery.Id && e.TenantId == actor.TenantId
                && e.MotoristaId == row.MotoristaId && e.VeiculoId == row.VeiculoId && e.AtribuicaoAtiva
                && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte")
                && (!(e.UltimoGpsCapturadoEmUtc ?? e.UltimoGpsRecebidoEmUtc).HasValue
                    || (e.UltimoGpsCapturadoEmUtc ?? e.UltimoGpsRecebidoEmUtc) < captured));
            if (db.Database.IsRelational())
                updated = await target.ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.UltimaLatitudeGps, (double?)row.Latitude).SetProperty(e => e.UltimaLongitudeGps, (double?)row.Longitude)
                    .SetProperty(e => e.UltimaPrecisaoGps, row.PrecisaoMetros).SetProperty(e => e.UltimaVelocidadeGps, row.VelocidadeKmH)
                    .SetProperty(e => e.UltimaDirecaoGps, row.Direcao).SetProperty(e => e.UltimoGpsCapturadoEmUtc, row.CapturadoEmUtc)
                    .SetProperty(e => e.UltimoGpsRecebidoEmUtc, (DateTime?)now), cancellationToken) > 0;
            else
            {
                var snapshot = await target.SingleOrDefaultAsync(cancellationToken);
                if (snapshot != null)
                {
                    snapshot.UltimaLatitudeGps = row.Latitude; snapshot.UltimaLongitudeGps = row.Longitude;
                    snapshot.UltimaPrecisaoGps = row.PrecisaoMetros; snapshot.UltimaVelocidadeGps = row.VelocidadeKmH; snapshot.UltimaDirecaoGps = row.Direcao;
                    snapshot.UltimoGpsCapturadoEmUtc = row.CapturadoEmUtc; snapshot.UltimoGpsRecebidoEmUtc = now;
                    await db.SaveChangesAsync(cancellationToken); updated = true;
                }
            }
        }
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Receipt(row, delivery.CodigoRastreio, "confirmado", updated);
    }
    public static GpsReceipt Receipt(PosicaoGps row, string code, string state, bool updated) => new(row.EventoId,
        state, Sequencia: row.Id, CapturadoEmUtc: row.CapturadoEmUtc.HasValue ? Utc(row.CapturadoEmUtc.Value) : null, RecebidoEmUtc: Utc(row.RecebidoEmUtc),
        Posicao: new(code, row.Latitude, row.Longitude, row.VelocidadeKmH, row.Direcao, row.PrecisaoMetros,
            row.Origem == "Simulacao", Utc(row.CapturadoEmUtc ?? row.RecebidoEmUtc), Utc(row.RecebidoEmUtc), row.EventoId, row.CapturaInformada), AtualizouPosicao: updated);
    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
