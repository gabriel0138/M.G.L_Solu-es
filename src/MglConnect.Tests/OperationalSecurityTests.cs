using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public class OperationalSecurityTests
{
    [Fact]
    public async Task ShiftCodeIsHashedBoundSingleUseAndCanResumeAfterRetry()
    {
        await using var fixture = await CreateFixtureAsync();
        var shifts = new DriverShiftService(fixture.Context);

        var issued = await shifts.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        Assert.True(issued.Success);
        Assert.NotNull(issued.Code);
        var storedCode = await fixture.Context.CodigosTurnoMotorista.SingleAsync();
        Assert.NotEqual(issued.Code, storedCode.CodigoHash);
        Assert.Equal(fixture.Driver.Id, storedCode.MotoristaId);

        var started = await shifts.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, issued.Code!);
        Assert.Equal(DriverShiftStartStatus.Started, started.Status);
        Assert.Equal("Em transporte", fixture.Delivery.StatusEntrega);

        var retry = await shifts.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, issued.Code!);
        Assert.Equal(DriverShiftStartStatus.AlreadyActive, retry.Status);
        Assert.Equal(1, await fixture.Context.TurnosMotorista.CountAsync());

        Assert.True(await shifts.EndAsync(fixture.Driver.Id));
        var reused = await shifts.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, issued.Code!);
        Assert.Equal(DriverShiftStartStatus.InvalidCode, reused.Status);
        Assert.False(await shifts.HasActiveShiftAsync(fixture.Driver.Id, fixture.Delivery.Id));
    }

    [Fact]
    public async Task ShiftCodeRejectsWrongDriverAndLocksAfterRepeatedInvalidAttempts()
    {
        await using var fixture = await CreateFixtureAsync();
        var otherDriver = new Usuario
        {
            Nome = "Outro motorista",
            Login = "driver-other",
            SenhaHash = "hash",
            Perfil = "Motorista",
            Ativo = true
        };
        fixture.Context.Usuarios.Add(otherDriver);
        await fixture.Context.SaveChangesAsync();

        var service = new DriverShiftService(fixture.Context);
        var issued = await service.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        Assert.Equal(DriverShiftStartStatus.AssignmentUnavailable,
            (await service.StartAsync(otherDriver.Id, fixture.Delivery.Id, issued.Code!)).Status);

        for (var attempt = 1; attempt < 5; attempt++)
        {
            Assert.Equal(DriverShiftStartStatus.InvalidCode,
                (await service.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, "wrong-code")).Status);
        }
        Assert.Equal(DriverShiftStartStatus.AttemptsExceeded,
            (await service.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, "wrong-code")).Status);
        Assert.Equal(5, await fixture.Context.CodigosTurnoMotorista.Select(item => item.Tentativas).SingleAsync());
        Assert.Contains(await fixture.Context.AuditoriaOperacional.ToListAsync(),
            entry => entry.Evento == "CodigoTurnoBloqueado" || entry.Evento == "CodigoTurnoInvalido");
    }

    [Fact]
    public async Task ExpiredAndCancelledShiftCodesAreRejected()
    {
        await using var fixture = await CreateFixtureAsync();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var service = new DriverShiftService(fixture.Context, clock);

        var expired = await service.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(DriverShiftStartStatus.Expired,
            (await service.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, expired.Code!)).Status);

        var cancelled = await service.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        Assert.True(await service.CancelCodeAsync(fixture.Delivery.Id, fixture.Admin.Id, "Operação cancelou."));
        Assert.Equal(DriverShiftStartStatus.Cancelled,
            (await service.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, cancelled.Code!)).Status);
    }

    [Fact]
    public async Task ConcurrentShiftStartsCreateAtMostOneActiveShift()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mgl-shift-{Guid.NewGuid():N}.sqlite");
        try
        {
            var options = new DbContextOptionsBuilder<MglDbContext>()
                .UseSqlite($"Data Source={databasePath};Default Timeout=10")
                .Options;
            int deliveryId;
            int adminId;
            int driverId;
            string code;
            await using (var setup = new MglDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(setup);
                var ids = await SeedAsync(setup);
                deliveryId = ids.DeliveryId;
                adminId = ids.AdminId;
                driverId = ids.DriverId;
                var issued = await new DriverShiftService(setup).IssueCodeAsync(ids.DeliveryId, adminId);
                code = issued.Code!;
            }

            await using var firstContext = new MglDbContext(options);
            await using var secondContext = new MglDbContext(options);
            var firstService = new DriverShiftService(firstContext);
            var secondService = new DriverShiftService(secondContext);
            var results = await Task.WhenAll(
                firstService.StartAsync(driverId, deliveryId, code),
                secondService.StartAsync(driverId, deliveryId, code));

            await using var verify = new MglDbContext(options);
            Assert.Equal(1, await verify.TurnosMotorista.CountAsync(shift => shift.Ativo));
            Assert.All(results, result => Assert.Contains(result.Status, new[]
            {
                DriverShiftStartStatus.Started,
                DriverShiftStartStatus.AlreadyActive,
                DriverShiftStartStatus.Conflict
            }));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EmergencyAlertIsPersistedIdempotentAndAuditedByOperations()
    {
        await using var fixture = await CreateFixtureAsync();
        var shifts = new DriverShiftService(fixture.Context);
        var code = await shifts.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        Assert.Equal(DriverShiftStartStatus.Started,
            (await shifts.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, code.Code!)).Status);

        var service = new EmergencyAlertService(fixture.Context);
        var request = new EmergencyAlertRequest(Guid.NewGuid(), fixture.Delivery.Id, null, null, null, null);
        var created = await service.CreateAsync(fixture.Driver.Id, request);
        Assert.True(created.Success);
        Assert.Equal("Indisponível", created.Alert!.SituacaoLocalizacao);
        Assert.Null(created.Alert.Latitude);

        var retry = await service.CreateAsync(fixture.Driver.Id, request);
        Assert.True(retry.Success);
        Assert.True(retry.Duplicate);
        Assert.Equal(created.Alert.Id, retry.Alert!.Id);

        Assert.True(await service.AcknowledgeAsync(created.Alert.Id, fixture.Admin.Id));
        Assert.Equal("Reconhecido", created.Alert.Status);
        Assert.True(await service.UpdateStatusAsync(created.Alert.Id, fixture.Admin.Id, "EmAtendimento"));
        Assert.True(await service.UpdateStatusAsync(created.Alert.Id, fixture.Admin.Id, "Resolvido"));
        Assert.False(await service.UpdateStatusAsync(created.Alert.Id, fixture.Admin.Id, "Aberto"));
        Assert.Equal(4, await fixture.Context.AuditoriaOperacional.CountAsync(entry =>
            entry.TipoEntidade == "AlertaEmergencia"));
    }

    [Fact]
    public async Task EmergencyAlertRecordsStaleLocationAndRejectsWithoutActiveShift()
    {
        await using var fixture = await CreateFixtureAsync();
        var shifts = new DriverShiftService(fixture.Context);
        var code = await shifts.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        Assert.Equal(DriverShiftStartStatus.Started,
            (await shifts.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, code.Code!)).Status);

        var service = new EmergencyAlertService(fixture.Context);
        var staleRequest = new EmergencyAlertRequest(
            Guid.NewGuid(),
            fixture.Delivery.Id,
            -23.55,
            -46.63,
            15,
            DateTime.UtcNow.AddMinutes(-4));
        var alert = await service.CreateAsync(fixture.Driver.Id, staleRequest);
        Assert.True(alert.Success);
        Assert.Equal("Desatualizada", alert.Alert!.SituacaoLocalizacao);
        Assert.Equal(-23.55, alert.Alert.Latitude);

        await shifts.EndAsync(fixture.Driver.Id);
        var denied = await service.CreateAsync(fixture.Driver.Id,
            new EmergencyAlertRequest(Guid.NewGuid(), fixture.Delivery.Id, null, null, null, null));
        Assert.False(denied.Success);
        Assert.Equal(1, await fixture.Context.AlertasEmergencia.CountAsync());
    }

    [Fact]
    public async Task EmergencyAlertPersistenceFailureDoesNotReturnSuccess()
    {
        await using var fixture = await CreateFixtureAsync();
        var shifts = new DriverShiftService(fixture.Context);
        var code = await shifts.IssueCodeAsync(fixture.Delivery.Id, fixture.Admin.Id);
        await shifts.StartAsync(fixture.Driver.Id, fixture.Delivery.Id, code.Code!);

        await using var failingContext = new MglDbContext(
            new DbContextOptionsBuilder<MglDbContext>()
                .UseSqlite(fixture.Connection)
                .AddInterceptors(new FailSaveChangesInterceptor())
                .Options);
        var service = new EmergencyAlertService(failingContext);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(
            fixture.Driver.Id,
            new EmergencyAlertRequest(Guid.NewGuid(), fixture.Delivery.Id, null, null, null, null)));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>()
            .UseSqlite(connection)
            .Options);
        await context.Database.EnsureCreatedAsync();
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        var ids = await SeedAsync(context);
        var delivery = await context.Entregas.SingleAsync(item => item.Id == ids.DeliveryId);
        var admin = await context.Usuarios.SingleAsync(item => item.Id == ids.AdminId);
        var driver = await context.Usuarios.SingleAsync(item => item.Id == ids.DriverId);
        return new Fixture(connection, context, delivery, admin, driver);
    }

    private static async Task<SeedIds> SeedAsync(MglDbContext context)
    {
        var admin = new Usuario
        {
            Nome = "Admin",
            Login = $"admin-{Guid.NewGuid():N}",
            SenhaHash = "hash",
            Perfil = "Admin",
            Ativo = true
        };
        var driver = new Usuario
        {
            Nome = "Motorista",
            Login = $"driver-{Guid.NewGuid():N}",
            SenhaHash = "hash",
            Perfil = "Motorista",
            Ativo = true
        };
        var client = new Cliente
        {
            RazaoSocial = "Cliente",
            Cnpj = $"CNPJ-{Guid.NewGuid():N}"[..18],
            Telefone = "11999999999"
        };
        var vehicle = new Veiculo
        {
            Placa = $"T{Guid.NewGuid():N}"[..7].ToUpperInvariant(),
            Modelo = "Van de teste",
            Ativo = true
        };
        context.AddRange(admin, driver, client, vehicle);
        await context.SaveChangesAsync();
        var order = new Pedido
        {
            NumeroPedido = $"PED-{Guid.NewGuid():N}"[..18],
            ClienteId = client.Id,
            Descricao = "Pedido de teste",
            Status = "ComEntregaAtribuida"
        };
        context.Pedidos.Add(order);
        await context.SaveChangesAsync();
        var delivery = new Entrega
        {
            PedidoId = order.Id,
            MotoristaId = driver.Id,
            VeiculoId = vehicle.Id,
            CodigoRastreio = $"MGL-{Guid.NewGuid():N}"[..20],
            DestinatarioNome = "Destino de teste",
            EnderecoDestino = "Rua de teste",
            CidadeDestino = "Santos",
            EstadoDestino = "SP",
            CepDestino = "11000-000",
            StatusEntrega = "Pendente",
            AtribuicaoAtiva = true
        };
        context.Entregas.Add(delivery);
        await context.SaveChangesAsync();
        return new SeedIds(delivery.Id, admin.Id, driver.Id);
    }

    private sealed record SeedIds(int DeliveryId, int AdminId, int DriverId);

    private sealed class Fixture(
        SqliteConnection connection,
        MglDbContext context,
        Entrega delivery,
        Usuario admin,
        Usuario driver) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public MglDbContext Context { get; } = context;
        public Entrega Delivery { get; } = delivery;
        public Usuario Admin { get; } = admin;
        public Usuario Driver { get; } = driver;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private sealed class FailSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(new DbUpdateException("Simulated persistence failure."));
    }
}
