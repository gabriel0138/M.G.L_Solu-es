using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task AuthenticatedTenantSeesOnlyItsOwnRecords()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var context = CreateContext(databaseName, tenantA, true))
        {
            context.Clientes.Add(new Cliente { RazaoSocial = "A", Cnpj = "A-1", Telefone = "1" });
            context.SaveChanges();
        }

        await using (var context = CreateContext(databaseName, tenantB, true))
        {
            context.Clientes.Add(new Cliente { RazaoSocial = "B", Cnpj = "B-1", Telefone = "1" });
            context.SaveChanges();
            Assert.Equal("B", Assert.Single(await context.Clientes.ToListAsync()).RazaoSocial);
        }

        await using (var context = CreateContext(databaseName, tenantA, true))
        {
            Assert.Equal("A", Assert.Single(await context.Clientes.ToListAsync()).RazaoSocial);
        }
    }

    [Fact]
    public async Task TenantIsSetFromAuthenticatedContextInsteadOfPostedValue()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(databaseName, tenantId, true);

        var cliente = new Cliente
        {
            TenantId = Guid.NewGuid(),
            RazaoSocial = "Empresa",
            Cnpj = "CNPJ-1",
            Telefone = "1"
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        Assert.Equal(tenantId, cliente.TenantId);
    }

    [Fact]
    public async Task AuthenticatedUserCannotModifyAnotherTenantsRecord()
    {
        var databaseName = Guid.NewGuid().ToString();
        var otherTenant = Guid.NewGuid();
        var customerId = 0;
        await using (var seedContext = CreateContext(databaseName, otherTenant, true))
        {
            var cliente = new Cliente { RazaoSocial = "A", Cnpj = "A-1", Telefone = "1" };
            seedContext.Clientes.Add(cliente);
            await seedContext.SaveChangesAsync();
            customerId = cliente.Id;
        }

        await using var context = CreateContext(databaseName, Guid.NewGuid(), true);
        var clienteDeOutroTenant = await context.Clientes
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == customerId);
        clienteDeOutroTenant.RazaoSocial = "Alterado";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AuthenticatedUserCannotMoveAnotherTenantsRecordIntoItsTenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var customerId = 0;
        await using (var seedContext = CreateContext(databaseName, tenantB, true))
        {
            var cliente = new Cliente { RazaoSocial = "B", Cnpj = "B-1", Telefone = "1" };
            seedContext.Clientes.Add(cliente);
            await seedContext.SaveChangesAsync();
            customerId = cliente.Id;
        }

        await using var context = CreateContext(databaseName, tenantA, true);
        var clienteDeOutroTenant = await context.Clientes
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == customerId);
        clienteDeOutroTenant.TenantId = tenantA;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void AuthenticatedUserWithoutTenantClaimIsBoundToEmptyTenant()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "user") },
                "test"))
        };

        var provider = new HttpContextTenantProvider(new HttpContextAccessor { HttpContext = httpContext });

        Assert.True(provider.IsTenantBound);
        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    private static MglDbContext CreateContext(string databaseName, Guid tenantId, bool isTenantBound)
    {
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new MglDbContext(options, new StaticTenantProvider(tenantId, isTenantBound));
    }

    private sealed class StaticTenantProvider(Guid tenantId, bool isTenantBound) : ITenantProvider
    {
        public Guid TenantId { get; } = tenantId;
        public bool IsTenantBound { get; } = isTenantBound;
    }
}
