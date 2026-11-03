using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
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
        foreach (var code in codes)
        {
            using var response = await client.GetAsync($"/api/rastreio/{code.ToLowerInvariant()}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
}
