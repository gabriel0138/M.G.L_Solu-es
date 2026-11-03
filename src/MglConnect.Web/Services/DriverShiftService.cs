using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services;

public sealed class DriverShiftService : IDriverShiftService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private readonly MglDbContext _context;
    private readonly TimeProvider _clock;

    public DriverShiftService(MglDbContext context, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<DriverShiftCodeIssueResult> IssueCodeAsync(
        int deliveryId,
        int adminUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var admin = await _context.Usuarios.AsNoTracking().AnyAsync(
            user => user.Id == adminUserId && user.Ativo
                && (user.Perfil == "Admin" || user.Perfil == "Administrador"),
            cancellationToken);
        if (!admin)
        {
            return new(false, null, null, "Usuário administrativo inválido.");
        }

        var delivery = await _context.Entregas.FirstOrDefaultAsync(
            item => item.Id == deliveryId,
            cancellationToken);
        if (delivery?.MotoristaId is not int driverId
            || delivery.VeiculoId is not int vehicleId
            || !delivery.AtribuicaoAtiva
            || delivery.StatusEntrega is "Entregue" or "Cancelada"
            || !await _context.Usuarios.AnyAsync(user => user.Id == driverId && user.Ativo && user.Perfil == "Motorista", cancellationToken)
            || !await _context.Veiculos.AnyAsync(vehicle => vehicle.Id == vehicleId && vehicle.Ativo, cancellationToken))
        {
            return new(false, null, null, "A entrega precisa ter motorista e veículo ativos atribuídos.");
        }

        if (await _context.TurnosMotorista.AnyAsync(
                shift => shift.EntregaId == deliveryId && shift.Ativo,
                cancellationToken))
        {
            return new(false, null, null, "A entrega já possui turno ativo.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var pendingCodes = await _context.CodigosTurnoMotorista
            .Where(item => item.EntregaId == deliveryId
                && item.MotoristaId == driverId
                && item.UsadoEmUtc == null
                && item.CanceladoEmUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var pending in pendingCodes)
        {
            pending.CanceladoEmUtc = now;
            pending.Versao++;
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var expiresAt = now.Add(CodeLifetime);
        _context.CodigosTurnoMotorista.Add(new CodigoTurnoMotorista
        {
            EntregaId = deliveryId,
            MotoristaId = driverId,
            CodigoHash = Hash(code),
            EmitidoPorUsuarioId = adminUserId,
            EmitidoEmUtc = now,
            ExpiraEmUtc = expiresAt
        });
        AddAudit(adminUserId, "CodigoTurnoEmitido", "Entrega", deliveryId.ToString(), "Código aleatório; segredo não registrado.");
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            return new(false, null, null, "Outra emissão ou alteração concorrente foi detectada. Atualize e tente novamente.");
        }
        return new(true, code, expiresAt, null);
    }

    public async Task<bool> CancelCodeAsync(
        int deliveryId,
        int adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var codes = await _context.CodigosTurnoMotorista
            .Where(item => item.EntregaId == deliveryId
                && item.UsadoEmUtc == null
                && item.CanceladoEmUtc == null)
            .ToListAsync(cancellationToken);
        if (codes.Count == 0)
        {
            return false;
        }

        foreach (var code in codes)
        {
            code.CanceladoEmUtc = now;
            code.Versao++;
        }
        AddAudit(adminUserId, "CodigoTurnoCancelado", "Entrega", deliveryId.ToString(), reason);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DriverShiftStartResult> StartAsync(
        int driverId,
        int deliveryId,
        string code,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var userIsActiveDriver = await _context.Usuarios.AnyAsync(
                user => user.Id == driverId && user.Ativo && user.Perfil == "Motorista",
                cancellationToken);
            if (!userIsActiveDriver)
            {
                return new(DriverShiftStartStatus.AssignmentUnavailable);
            }

            var delivery = await _context.Entregas.FirstOrDefaultAsync(
                item => item.Id == deliveryId && item.MotoristaId == driverId,
                cancellationToken);
            if (delivery == null
                || !delivery.AtribuicaoAtiva
                || delivery.VeiculoId == null
                || delivery.StatusEntrega is "Entregue" or "Cancelada"
                || !await _context.Veiculos.AnyAsync(vehicle => vehicle.Id == delivery.VeiculoId && vehicle.Ativo, cancellationToken))
            {
                return new(DriverShiftStartStatus.AssignmentUnavailable);
            }

            var existingShift = await _context.TurnosMotorista
                .FirstOrDefaultAsync(shift => shift.MotoristaId == driverId && shift.Ativo, cancellationToken);
            if (existingShift != null)
            {
                await CommitAsync(transaction, cancellationToken);
                return existingShift.EntregaId == deliveryId
                    ? new(DriverShiftStartStatus.AlreadyActive, existingShift.Id)
                    : new(DriverShiftStartStatus.AnotherShiftActive);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var issuedCode = await _context.CodigosTurnoMotorista
                .Where(item => item.EntregaId == deliveryId
                    && item.MotoristaId == driverId)
                .OrderByDescending(item => item.EmitidoEmUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (issuedCode == null)
            {
                AddAudit(driverId, "InicioTurnoRecusado", "Entrega", deliveryId.ToString(), "Código indisponível.");
                await _context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return new(DriverShiftStartStatus.InvalidCode);
            }

            if (issuedCode.CanceladoEmUtc.HasValue)
            {
                AddAudit(driverId, "InicioTurnoRecusado", "Entrega", deliveryId.ToString(), "Código cancelado.");
                await _context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return new(DriverShiftStartStatus.Cancelled);
            }
            if (issuedCode.UsadoEmUtc.HasValue)
            {
                AddAudit(driverId, "InicioTurnoRecusado", "Entrega", deliveryId.ToString(), "Código já utilizado.");
                await _context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return new(DriverShiftStartStatus.InvalidCode);
            }

            if (issuedCode.ExpiraEmUtc <= now)
            {
                issuedCode.CanceladoEmUtc = now;
                issuedCode.Versao++;
                AddAudit(driverId, "CodigoTurnoExpirado", "Entrega", deliveryId.ToString(), null);
                await _context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return new(DriverShiftStartStatus.Expired);
            }

            if (issuedCode.Tentativas >= MaxAttempts)
            {
                issuedCode.CanceladoEmUtc = now;
                issuedCode.Versao++;
                AddAudit(driverId, "CodigoTurnoBloqueado", "Entrega", deliveryId.ToString(), "Limite de tentativas atingido.");
                await _context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return new(DriverShiftStartStatus.AttemptsExceeded);
            }

            if (!Matches(code, issuedCode.CodigoHash))
            {
                issuedCode.Tentativas++;
                issuedCode.Versao++;
                if (issuedCode.Tentativas >= MaxAttempts)
                {
                    issuedCode.CanceladoEmUtc = now;
                }
                AddAudit(driverId, "CodigoTurnoInvalido", "Entrega", deliveryId.ToString(), $"Tentativa {issuedCode.Tentativas}.");
                await _context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return new(issuedCode.Tentativas >= MaxAttempts
                    ? DriverShiftStartStatus.AttemptsExceeded
                    : DriverShiftStartStatus.InvalidCode);
            }

            issuedCode.UsadoEmUtc = now;
            issuedCode.Versao++;
            delivery.StatusEntrega = "Em transporte";
            delivery.AtribuicaoAtiva = true;
            delivery.Versao++;
            var shift = new TurnoMotorista
            {
                EntregaId = deliveryId,
                MotoristaId = driverId,
                VeiculoId = delivery.VeiculoId.Value,
                CodigoTurnoId = issuedCode.Id,
                IniciadoEmUtc = now,
                Ativo = true
            };
            _context.TurnosMotorista.Add(shift);
            _context.EventosRastreio.Add(new EventoRastreio
            {
                EntregaId = deliveryId,
                DataHora = now,
                Titulo = "Turno iniciado pelo motorista",
                Descricao = "Início de operação autenticado e auditado.",
                Localidade = "Localização não informada no início do turno",
                StatusAssociado = "Em transporte",
                Icone = "bi-truck"
            });
            AddAudit(driverId, "TurnoIniciado", "Entrega", deliveryId.ToString(), $"Turno {shift.Id:N}.");
            await _context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return new(DriverShiftStartStatus.Started, shift.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return new(DriverShiftStartStatus.Conflict);
        }
        catch (DbUpdateException)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            var active = await _context.TurnosMotorista.AsNoTracking().FirstOrDefaultAsync(
                shift => shift.MotoristaId == driverId && shift.Ativo,
                cancellationToken);
            return active?.EntregaId == deliveryId
                ? new(DriverShiftStartStatus.AlreadyActive, active.Id)
                : new(DriverShiftStartStatus.Conflict);
        }
    }

    public Task<bool> HasActiveShiftAsync(
        int driverId,
        int deliveryId,
        CancellationToken cancellationToken = default) =>
        _context.TurnosMotorista.AsNoTracking().AnyAsync(
            shift => shift.MotoristaId == driverId
                && shift.EntregaId == deliveryId
                && shift.Ativo
                && _context.Usuarios.Any(user =>
                    user.Id == driverId && user.Ativo && user.Perfil == "Motorista"),
            cancellationToken);

    public async Task<bool> EndAsync(int driverId, CancellationToken cancellationToken = default)
    {
        var shift = await _context.TurnosMotorista.FirstOrDefaultAsync(
            item => item.MotoristaId == driverId && item.Ativo,
            cancellationToken);
        if (shift == null)
        {
            return false;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        shift.Ativo = false;
        shift.EncerradoEmUtc = now;
        shift.Versao++;
        AddAudit(driverId, "TurnoEncerrado", "Entrega", shift.EntregaId.ToString(), $"Turno {shift.Id:N}.");
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private void AddAudit(int actorId, string eventName, string entityType, string entityId, string? details) =>
        _context.AuditoriaOperacional.Add(new AuditoriaOperacional
        {
            AtorUsuarioId = actorId,
            Evento = eventName,
            TipoEntidade = entityType,
            IdentificadorEntidade = entityId,
            Detalhes = details,
            OcorridoEmUtc = _clock.GetUtcNow().UtcDateTime
        });

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    private static bool Matches(string? code, string hash)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
        {
            return false;
        }

        var supplied = Convert.FromHexString(Hash(code));
        var stored = Convert.FromHexString(hash);
        return supplied.Length == stored.Length
            && CryptographicOperations.FixedTimeEquals(supplied, stored);
    }

    private static Task CommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction == null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);
}
