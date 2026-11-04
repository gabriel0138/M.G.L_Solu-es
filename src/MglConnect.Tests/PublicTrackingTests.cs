using System.Net;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class PublicTrackingTests
{
    [Fact]
    public async Task AnonymousApiAndPageExposeOnlyOperationalDataIncludingLegacyEvents()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        await using (var db = new MglDbContext(fixture.Options))
        {
            var row = await db.Entregas.Include(e => e.Pedido).SingleAsync(e => e.Id == delivery.Id);
            row.DestinatarioNome = "Mariana SegredoDestinatario";
            row.NomeRecebedor = "Carlos SegredoRecebedor";
            row.DestinatarioDocumento = "123.456.789-09";
            row.DocumentoRecebedor = "98.765.432/0001-10";
            row.EnderecoDestino = "Rua SegredoResidencial";
            row.NumeroDestino = "98765";
            row.ComplementoDestino = "ApartamentoSecreto";
            row.BairroDestino = "Centro";
            row.AssinaturaDigitalBase64 = "data:image/png;base64,SEGREDOASSINATURA";
            row.FotoComprovantePath = "/privado/segredo-foto.png";
            row.StatusEntrega = "Entregue";
            row.Pedido!.OrigemCidade = "Campinas";
            row.Pedido.OrigemEstado = "SP";
            db.EventosRastreio.Add(new EventoRastreio
            {
                EntregaId = row.Id, Titulo = "SegredoTitulo", Descricao = "Carlos SegredoRecebedor (Doc: 98.765.432/0001-10)",
                Localidade = "Rua SegredoResidencial, 98765", StatusAssociado = "Entregue", Icone = "SegredoIcone"
            });
            await db.SaveChangesAsync();
        }
        using var client = fixture.Anonymous();
        foreach (var path in new[] { $"/api/rastreio/{delivery.CodigoRastreio}", $"/Rastreio/{delivery.CodigoRastreio}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            foreach (var secret in new[] { "Segredo", "123.456.789-09", "98.765.432/0001-10", "98765", "ApartamentoSecreto", "data:image", "segredo-foto", "Fixture street" })
                Assert.DoesNotContain(secret, content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Mariana", content);
            Assert.Contains("Campinas", content);
        }
        using var api = await client.GetAsync($"/api/rastreio/{delivery.CodigoRastreio}");
        using var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        var fields = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(fields, f => f.Contains("documento", StringComparison.OrdinalIgnoreCase) || f.Contains("assinatura", StringComparison.OrdinalIgnoreCase) || f.Contains("pedido", StringComparison.OrdinalIgnoreCase) || f.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Entregue", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Mariana", json.RootElement.GetProperty("destinatario").GetString());
        Assert.Single(json.RootElement.GetProperty("eventos").EnumerateArray());
        using var pdf = await client.GetAsync($"/api/entregas/{delivery.Id}/pod/pdf");
        Assert.NotEqual("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ExistingDecimalGuidAndPrefixedCodesRemainQueryableAndInvalidCodesAreGeneric()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var codes = new[] { "12345678", Guid.NewGuid().ToString(), "MGL-12345678BR" };
        await using (var db = new MglDbContext(fixture.Options))
        {
            for (var i = 0; i < codes.Length; i++)
                (await db.Entregas.FindAsync(fixture.Deliveries[i].Id))!.CodigoRastreio = codes[i];
            await db.SaveChangesAsync();
        }
        using var client = fixture.Anonymous();
        for (var index = 0; index < codes.Length; index++)
        {
            using var response = await client.GetAsync($"/api/rastreio/{codes[index].ToLowerInvariant()}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (index is 0 or 2)
            {
                Assert.True(json.RootElement.GetProperty("informacoesPublicasReduzidas").GetBoolean());
                Assert.Equal("Destinatário", json.RootElement.GetProperty("destinatario").GetString());
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("cidadeDestino").ValueKind);
                Assert.Empty(json.RootElement.GetProperty("eventos").EnumerateArray());
            }
            else
            {
                Assert.False(json.RootElement.GetProperty("informacoesPublicasReduzidas").GetBoolean());
                Assert.NotEqual("Destinatário", json.RootElement.GetProperty("destinatario").GetString());
            }
        }
        string? generic = null;
        foreach (var code in new[] { "99999999", "invalid'--", new string('A', 100), "%3Cscript%3E" })
        {
            using var response = await client.GetAsync($"/api/rastreio/{code}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            generic ??= body;
            Assert.Equal(generic, body);
            Assert.DoesNotContain("Exception", body);
            Assert.DoesNotContain("Sql", body);
        }
    }

    [Fact]
    public async Task AnonymousQueriesAreRateLimitedAcrossPageAndApi()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var client = fixture.Anonymous();
        var limited = false;
        for (var i = 0; i < 35; i++)
        {
            using var response = await client.GetAsync(i % 2 == 0 ? "/api/rastreio/99999999" : "/Rastreio/99999999");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = true; break; }
        }
        Assert.True(limited);
    }

    [Theory]
    [InlineData("MGL-12345678BR")]
    [InlineData("12345678")]
    public async Task WeakLegacyCodesKeepStatusButDoNotExposePersonalDataOrRealtime(string code)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        await fixture.ChangeAsync(db =>
        {
            var row = db.Entregas.Single(e => e.Id == delivery.Id);
            row.CodigoRastreio = code;
            row.DestinatarioNome = "Synthetic private recipient";
            row.CidadeDestino = "Synthetic private city";
            row.DataCriacao = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            row.DataPrevisaoEntrega = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);
            db.EventosRastreio.Add(new EventoRastreio
            {
                EntregaId = row.Id, DataHora = DateTime.UtcNow, StatusAssociado = "Em transporte",
                Titulo = "Synthetic private event", Descricao = "Synthetic private description",
                Localidade = "Synthetic private location"
            });
        });

        using var client = fixture.Anonymous();
        using var response = await client.GetAsync($"/api/rastreio/{code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var privateValue in new[]
        {
            "Synthetic private recipient", "Synthetic private city", "Synthetic private event",
            "Synthetic private description", "Synthetic private location", "2026-01-02", "2026-01-03"
        })
            Assert.DoesNotContain(privateValue, body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Destinatário", json.RootElement.GetProperty("destinatario").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("dataPostagem").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("eventos").EnumerateArray());

        using var page = await client.GetAsync($"/Rastreio/{code}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("exibem apenas o status", html, StringComparison.Ordinal);
        Assert.Contains("Atualizações em tempo real indisponíveis", html, StringComparison.Ordinal);
        foreach (var privateValue in new[]
        {
            "Synthetic private recipient", "Synthetic private city", "Synthetic private event",
            "Synthetic private description", "Synthetic private location", "2026-01-02", "2026-01-03"
        })
            Assert.DoesNotContain(privateValue, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyProjectionQueriesOnlyTheStatusColumn()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        const string legacyCode = "MGL-12345678BR";
        await fixture.ChangeAsync(db =>
        {
            var delivery = db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id);
            delivery.CodigoRastreio = legacyCode;
            db.EventosRastreio.Add(new EventoRastreio
            {
                EntregaId = delivery.Id, StatusAssociado = "Em transporte",
                Titulo = "Synthetic private event", Descricao = "Synthetic private description",
                Localidade = "Synthetic private location"
            });
        });

        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseSqlite($"Data Source={fixture.DatabaseFile};Foreign Keys=True")
            .LogTo(commands.Add, LogLevel.Information)
            .Options;
        await using var context = new MglDbContext(options);
        var service = new TrackingService(context, Mock.Of<IHubContext<TrackingHub>>());
        var details = await service.GetTrackingDetailsAsync(legacyCode);

        Assert.NotNull(details);
        Assert.True(details.InformacoesPublicasReduzidas);
        Assert.Equal("Em transporte", details.StatusEntrega);
        Assert.Empty(details.EventosRastreio);
        var executedSql = string.Join('\n', commands);
        Assert.DoesNotContain("EventosRastreio", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DestinatarioNome", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CidadeDestino", executedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyTrackingUsesTheSamePerAddressBudgetAcrossPageAndApi()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(hostingConfiguration: new Dictionary<string, string>
        {
            ["PublicTracking:PermitLimit"] = "1",
            ["ReverseProxy__Enabled"] = "true",
            ["ReverseProxy__KnownProxies__0"] = "127.0.0.1"
        });
        await fixture.ChangeAsync(db =>
            db.Entregas.Single(e => e.Id == fixture.Deliveries[1].Id).CodigoRastreio = "12345678");
        using var client = fixture.Anonymous();
        using var first = await GetFromAsync("192.0.2.1", "/api/rastreio/99999999");
        using var repeated = await GetFromAsync("192.0.2.1", "/Rastreio?codigo=88888888");
        using var legitimate = await GetFromAsync("192.0.2.2", "/api/rastreio/12345678");

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeated.StatusCode);
        Assert.True(repeated.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, legitimate.StatusCode);
        using var json = JsonDocument.Parse(await legitimate.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("informacoesPublicasReduzidas").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("eventos").EnumerateArray());

        async Task<HttpResponseMessage> GetFromAsync(string address, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Forwarded-For", address);
            request.Headers.Add("X-Forwarded-Proto", "http");
            return await client.SendAsync(request);
        }
    }

    [Fact]
    public async Task AdminCanRotateOneLegacyCodeAndTheOldCodeStopsWorking()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        const string oldCode = "MGL-12345678BR";
        await fixture.ChangeAsync(db => db.Entregas.Single(e => e.Id == delivery.Id).CodigoRastreio = oldCode);
        using var driver = await fixture.LoginAsync("driver-a");
        var driverToken = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas");
        using var deniedRotation = await driver.Client.PostAsync("/Entregas/RotacionarCodigoRastreio", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = driverToken,
            ["id"] = delivery.Id.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, deniedRotation.StatusCode);
        await using (var afterDeniedRotation = new MglDbContext(fixture.Options))
            Assert.Equal(oldCode, (await afterDeniedRotation.Entregas.SingleAsync(e => e.Id == delivery.Id)).CodigoRastreio);

        using var admin = await fixture.LoginAsync("admin-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{delivery.Id}");
        using var missingToken = await admin.Client.PostAsync("/Entregas/RotacionarCodigoRastreio",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = delivery.Id.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var rotation = await admin.Client.PostAsync("/Entregas/RotacionarCodigoRastreio", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = delivery.Id.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, rotation.StatusCode);

        await using var db = new MglDbContext(fixture.Options);
        var rotated = await db.Entregas.SingleAsync(e => e.Id == delivery.Id);
        Assert.Matches("^[A-F0-9]{20}$", rotated.CodigoRastreio);
        Assert.NotEqual(oldCode, rotated.CodigoRastreio);
        var audit = await db.AuditoriaOperacional.SingleAsync(e => e.TipoEntidade == "Entrega" && e.IdentificadorEntidade == delivery.Id.ToString());
        Assert.Equal("CodigoRastreioRotacionado", audit.Evento);
        Assert.DoesNotContain(oldCode, audit.Detalhes ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(rotated.CodigoRastreio, audit.Detalhes ?? string.Empty, StringComparison.Ordinal);

        using var client = fixture.Anonymous();
        using var revoked = await client.GetAsync($"/api/rastreio/{oldCode}");
        using var current = await client.GetAsync($"/api/rastreio/{rotated.CodigoRastreio}");
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task AdminCannotRotateTrackingCodeInAnotherTenant()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var foreignDelivery = fixture.Deliveries[2];
        var originalCode = foreignDelivery.CodigoRastreio;
        using var admin = await fixture.LoginAsync("admin-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Entregas");
        using var response = await admin.Client.PostAsync("/Entregas/RotacionarCodigoRastreio",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["id"] = foreignDelivery.Id.ToString()
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = new MglDbContext(fixture.Options);
        Assert.Equal(originalCode, (await db.Entregas.SingleAsync(e => e.Id == foreignDelivery.Id)).CodigoRastreio);
    }
}
