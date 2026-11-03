using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using Xunit;

namespace MglConnect.Tests;

public class DemoConsistencyTests
{
    [Fact]
    public async Task FreshDemoHasAnAssignedTripAwaitingShiftAndAConsistentCompletedOrder()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(demoData: true);
        await using var db = new MglDbContext(fixture.Options);
        var available = await db.Entregas.Include(e => e.Pedido).SingleAsync(e => e.CodigoRastreio == "MGL-BR100201");
        Assert.Equal("Pendente", available.StatusEntrega);
        Assert.True(available.AtribuicaoAtiva);
        Assert.Equal("ComEntregaAtribuida", available.Pedido!.Status);
        Assert.False(await db.TurnosMotorista.AnyAsync());
        Assert.All(await db.EventosRastreio.Where(e => e.EntregaId == available.Id).ToListAsync(), e => Assert.Equal("Pendente", e.StatusAssociado));
        var completed = await db.Entregas.Include(e => e.Pedido).SingleAsync(e => e.CodigoRastreio == "MGL-BR200302");
        Assert.Equal("Entregue", completed.StatusEntrega);
        Assert.Equal("Concluido", completed.Pedido!.Status);
        Assert.False(completed.AtribuicaoAtiva);
        using var driver = await fixture.LoginAsync("carlos.motorista");
        using var trips = await driver.Client.GetAsync("/Entregas");
        Assert.Equal(HttpStatusCode.OK, trips.StatusCode);
        var html = await trips.Content.ReadAsStringAsync();
        Assert.Contains("MGL-8E19", html);
        Assert.Contains("/Entregas/IniciarTurno", html);
        Assert.Contains("name=\"codigo\"", html);
        Assert.DoesNotContain("Nenhuma viagem atribuída", html);
    }
}
