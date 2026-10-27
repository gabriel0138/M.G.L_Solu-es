using Xunit;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace MglConnect.Tests;

public class DeliveryStateMachineTests
{
    private readonly MglDbContext _context;

    public DeliveryStateMachineTests()
    {
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new MglDbContext(options);
        // Seed: criar uma entrega com status "Entregue"
        _context.Entregas.Add(new MglConnect.Web.Models.Entrega
        {
            Id = 1,
            CodigoRastreio = "MGL-00000001BR",
            StatusEntrega = "Entregue",
            DataEntregaRealizada = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    [Fact]
    public async Task AddCheckpoint_DeveLancarExcecaoAoTentarReverterDeEntregue()
    {
        // Arrange
        var service = new TrackingService(_context, null!);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddCheckpointAsync(1, "title", null, "cidade", "Em transporte", "bi-truck"));

        exception.Message.Should().Contain("Entregue", "Deve conter a palavra 'Entregue'");
        exception.Message.Should().Contain("nao pode", "Deve mencionar que não pode ser alterada");
    }

    [Fact]
    public async Task AddCheckpoint_NaoPodeAlterarEstadoOperacional()
    {
        var service = new TrackingService(_context, null!);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddCheckpointAsync(1, "Novo checkpoint", null, "São Paulo", "Em transporte", "bi-truck"));
        exception.Message.Should().Contain("nao pode ser alterada");
        _context.Entregas.First().StatusEntrega.Should().Be("Entregue");
    }

    [Fact]
    public async Task AddCheckpoint_NaoDeveFinalizarSemPOD()
    {
        // Arrange
        var entrega = _context.Entregas.First();
        entrega.StatusEntrega = "Em transporte";
        await _context.SaveChangesAsync();

        var service = new TrackingService(_context, null!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddCheckpointAsync(1, "Entrega concluída", null, "Destino", "Entregue", "bi-patch-check-fill"));
        _context.Entregas.First().StatusEntrega.Should().Be("Em transporte");
    }

    [Fact]
    public async Task FinalizarEntregaPOD_DeveMarcarComoEntregue()
    {
        var entrega = _context.Entregas.First();
        entrega.StatusEntrega = "Em transporte";
        await _context.SaveChangesAsync();

        var service = new TrackingService(_context, null!);
        var result = await service.FinalizarEntregaPODAsync(
            1, "Maria da Silva", "123", "assinatura", null, "Santos");

        // Assert
        result.Should().BeTrue();
        _context.Entregas.First().StatusEntrega.Should().Be("Entregue");
        _context.Entregas.First().DataEntregaRealizada.Should().NotBeNull();
        _context.Entregas.First().NomeRecebedor.Should().Be("Maria da Silva");
    }

    [Fact]
    public async Task FinalizarEntregaPOD_NaoDeveSubstituirComprovanteExistente()
    {
        var entrega = _context.Entregas.First();
        entrega.NomeRecebedor = "Recebedor original";
        await _context.SaveChangesAsync();

        var service = new TrackingService(_context, null!);
        var result = await service.FinalizarEntregaPODAsync(
            1, "Outro recebedor", "999", "outra assinatura", null, "Santos");

        result.Should().BeFalse();
        _context.Entregas.First().NomeRecebedor.Should().Be("Recebedor original");
    }
}