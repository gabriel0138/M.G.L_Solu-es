using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class PublicTrackingUiTests
{
    [Fact]
    public async Task SearchFromAResultPageDoesNotRetainThePreviousCodeInItsFormAction()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var client = fixture.Anonymous();
        var previous = fixture.Deliveries[0].CodigoRastreio;
        var next = fixture.Deliveries[1].CodigoRastreio;
        var html = await PageAsync(client, $"/Rastreio/{previous}");
        var form = Regex.Match(html, "<form\\b[^>]*role=\"search\"[^>]*>").Value;
        Assert.Contains("action=\"/Rastreio\"", form);
        Assert.DoesNotContain(previous, form);
        var changed = await PageAsync(client, $"/Rastreio?codigo={Uri.EscapeDataString(next)}");
        Assert.Contains($"data-tracking-code=\"{next}\"", changed);
        Assert.DoesNotContain($"data-tracking-code=\"{previous}\"", changed);
        var missing = await PageAsync(client, "/Rastreio/MGL-NOT-FOUND");
        Assert.Matches("<a\\b[^>]*href=\"/Rastreio\"[^>]*>.*?Fazer nova consulta", missing);
    }

    [Fact]
    public async Task PublicStagesFollowTheActualStatusRatherThanTheNumberOfEvents()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var delivery = fixture.Deliveries[0];
        await fixture.ChangeAsync(db =>
        {
            for (var index = 0; index < 3; index++)
                db.EventosRastreio.Add(new EventoRastreio
                {
                    EntregaId = delivery.Id, DataHora = new DateTime(2026, 1, index + 1, 12, 0, 0, DateTimeKind.Utc),
                    StatusAssociado = "Pendente", Titulo = "Legacy private event", Localidade = "Private warehouse"
                });
        });
        using var client = fixture.Anonymous();
        foreach (var (status, title, tone, stage) in new[]
        {
            ("Pendente", "Preparando sua entrega", "pending", 1),
            ("Em transporte", "A caminho do destino", "transit", 2),
            ("Entregue", "Entrega concluída", "delivered", 3),
            ("Cancelada", "Entrega cancelada", "cancelled", 0),
            ("EstadoDesconhecido", "Atualização operacional", "neutral", 0)
        })
        {
            await fixture.ChangeAsync(db => db.Entregas.Single(row => row.Id == delivery.Id).StatusEntrega = status);
            var html = await PageAsync(client, $"/Rastreio/{delivery.CodigoRastreio}");

            Assert.Equal(title, Text(html, "publicTrackingHeadline", "h2"));
            Assert.Contains($"data-status=\"{tone}\"", OpeningTag(html, "publicDeliveryStatus"));
            Assert.Equal(stage == 0, Regex.IsMatch(OpeningTag(html, "publicTrackingStages"), "\\bhidden(?:\\s|=|>)"));
            for (var item = 1; item <= 3; item++)
            {
                var expected = stage == item ? "current" : stage > item ? "completed" : "upcoming";
                Assert.Matches($"<li\\b[^>]*data-stage=\"{item}\"[^>]*data-stage-state=\"{expected}\"", html);
            }
            Assert.DoesNotContain("Bipado", html);
            Assert.DoesNotContain("POD Arquivado", html);
            Assert.DoesNotContain("Legacy private event", html);
            Assert.DoesNotContain("Private warehouse", html);
        }
    }

    [Fact]
    public async Task PublicTimelineHasAnEmptyStateAndKeepsExistingRealtimeIdentifiers()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        using var client = fixture.Anonymous();
        var html = await PageAsync(client, $"/Rastreio/{fixture.Deliveries[0].CodigoRastreio}");

        Assert.Contains("id=\"publicTimelineEmpty\"", html);
        Assert.Contains("id=\"publicTimeline\"", html);
        Assert.Contains("id=\"trackingConnectionStatus\"", html);
        Assert.Contains("ReceiveCheckpointUpdate", html);
        Assert.Contains("JoinTrackingGroup", html);
        Assert.Contains("/trackingHub", html);
        Assert.Contains("/css/command-tracking.css", html);
        Assert.Contains("Ainda não informada", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("POD Arquivado", html);
        Assert.DoesNotContain("Bipado", html);
        Assert.DoesNotContain("Corredor Rodoviário", html);
    }

    [Fact]
    public async Task PublicMarkupEncodesAllowedTextAndNeverRestoresPrivateDeliveryFields()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        const string firstName = "<script>alert(1)</script>";
        const string city = "<img src=x onerror=alert(1)>";
        await fixture.ChangeAsync(db =>
        {
            var row = db.Entregas.Single(item => item.Id == delivery.Id);
            row.DestinatarioNome = firstName + " SegredoSobrenome";
            row.CidadeDestino = city;
            row.EnderecoDestino = "SegredoEndereco";
            row.DestinatarioDocumento = "SegredoDocumento";
            row.NomeRecebedor = "SegredoRecebedor";
            row.DocumentoRecebedor = "SegredoDocumentoRecebedor";
            row.AssinaturaDigitalBase64 = "data:image/png;base64,SegredoAssinatura";
            row.FotoComprovantePath = "/private/SegredoFoto.png";
        });
        using var client = fixture.Anonymous();
        var html = await PageAsync(client, $"/Rastreio/{delivery.CodigoRastreio}");

        Assert.DoesNotContain(firstName, html);
        Assert.DoesNotContain(city, html);
        Assert.Contains(firstName, WebUtility.HtmlDecode(html));
        Assert.Contains(city, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Segredo", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private B01 cargo", html);
        Assert.DoesNotContain("B010000", html);
        Assert.DoesNotContain(fixture.TenantA.ToString(), html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/pod/pdf", html);
        Assert.Contains("/Account/Login", html);
    }

    private static async Task<string> PageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string OpeningTag(string html, string id)
    {
        var match = Regex.Match(html, $"<[a-z]+\\b[^>]*id=\"{Regex.Escape(id)}\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Expected element {id}.");
        return match.Value;
    }

    private static string Text(string html, string id, string tag)
    {
        var match = Regex.Match(html, $"<{tag}\\b[^>]*id=\"{Regex.Escape(id)}\"[^>]*>(.*?)</{tag}>", RegexOptions.Singleline);
        Assert.True(match.Success, $"Expected text element {id}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
    }
}
