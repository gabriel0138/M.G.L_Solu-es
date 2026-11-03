using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class PublicTrackingRealtimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousCheckpointAndPodEventsUsePublicProjectionWhilePrivateHistoryIsPreserved(bool pod)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var id = fixture.Deliveries[0].Id;
        var code = Guid.NewGuid().ToString(); // legacy lowercase GUID, including the live group
        await fixture.ChangeAsync(db => db.Entregas.Single(e => e.Id == id).CodigoRastreio = code);
        var cookies = new CookieContainer();
        using var anonymous = new SecurityHttpFixture.Session(fixture.Anonymous(cookies), cookies, "");
        await using var observer = await SecurityHubConnection.ConnectAsync(fixture, anonymous, "/trackingHub");
        Assert.False((await observer.InvokeAsync("JoinTrackingGroup", code)).TryGetProperty("error", out _));
        using var admin = await fixture.LoginAsync("admin-a");
        object body = pod
            ? new { nomeRecebedor = "SegredoRecebedor", documentoRecebedor = "123.456.789-09", assinaturaBase64 = "data:image/png;base64,Segredo", localidade = "SegredoEndereco" }
            : new { titulo = "SegredoTitulo", descricao = "SegredoDocumento", localidade = "SegredoEndereco", status = "Em transporte", icone = "SegredoIcone" };
        using var changed = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, $"/api/entregas/{id}/{(pod ? "pod" : "checkpoint")}", body));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var payload = await observer.EventPayloadAsync("ReceiveCheckpointUpdate", TimeSpan.FromSeconds(5));
        var text = payload.GetRawText();
        Assert.DoesNotContain("Segredo", text);
        Assert.DoesNotContain("123.456.789-09", text);
        Assert.DoesNotContain("data:image", text);
        Assert.False(payload.TryGetProperty("nomeRecebedor", out _));
        Assert.Equal(code.ToUpperInvariant(), payload.GetProperty("codigoRastreio").GetString());
        Assert.Equal(pod ? "Entregue" : "Em transporte", payload.GetProperty("status").GetString());

        using var response = await anonymous.Client.GetAsync($"/api/rastreio/{code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checkpoint = Assert.Single(json.RootElement.GetProperty("eventos").EnumerateArray());
        foreach (var field in new[] { "titulo", "descricao", "localidade", "icone" })
            Assert.Equal(checkpoint.GetProperty(field).GetString(), payload.GetProperty(field).GetString());
        Assert.Equal(checkpoint.GetProperty("dataHora").GetDateTime(), payload.GetProperty("dataHora").GetDateTime());
        using var page = await anonymous.Client.GetAsync($"/Rastreio/{code}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("Segredo", await page.Content.ReadAsStringAsync());

        await using var persisted = new MglDbContext(fixture.Options);
        var historical = await persisted.EventosRastreio.SingleAsync(e => e.EntregaId == id);
        Assert.Contains("Segredo", historical.Descricao!);
        Assert.Equal("SegredoEndereco", historical.Localidade);
        if (pod)
        {
            var delivery = await persisted.Entregas.SingleAsync(e => e.Id == id);
            Assert.Equal("SegredoRecebedor", delivery.NomeRecebedor);
            Assert.Equal("123.456.789-09", delivery.DocumentoRecebedor);
            Assert.Equal("data:image/png;base64,Segredo", delivery.AssinaturaDigitalBase64);
            using var denied = await anonymous.Client.GetAsync($"/api/entregas/{id}/pod-pdf");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var authorized = await admin.Client.SendAsync(admin.Api(HttpMethod.Get, $"/api/entregas/{id}/pod-pdf"));
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
            Assert.Equal("application/pdf", authorized.Content.Headers.ContentType?.MediaType);
        }
        else
        {
            using var privateDetails = await admin.Client.SendAsync(admin.Api(HttpMethod.Get, $"/api/entregas/{id}"));
            Assert.Equal(HttpStatusCode.OK, privateDetails.StatusCode);
            Assert.Contains("SegredoDocumento", await privateDetails.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task PublicHubInvocationsAndHttpQueriesShareTheSameBudget()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var cookies = new CookieContainer();
        using var anonymous = new SecurityHttpFixture.Session(fixture.Anonymous(cookies), cookies, "");
        await using var hub = await SecurityHubConnection.ConnectAsync(fixture, anonymous, "/trackingHub");
        for (var i = 0; i < 10; i++)
        {
            using var response = await anonymous.Client.GetAsync("/api/rastreio/99999999");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        var limited = false;
        for (var i = 0; i < 35; i++)
        {
            var result = await hub.InvokeAsync("JoinTrackingGroup", "99999999");
            Assert.True(result.TryGetProperty("error", out var error));
            if (error.GetString()!.Contains("Muitas consultas", StringComparison.Ordinal)) { limited = true; break; }
            Assert.Contains("Rastreamento não localizado", error.GetString());
        }
        Assert.True(limited);
        using var blocked = await anonymous.Client.GetAsync("/Rastreio/99999999");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var spoofed = new HttpRequestMessage(HttpMethod.Get, "/api/rastreio/99999999");
        spoofed.Headers.Add("X-Forwarded-For", "192.0.2.123");
        using var stillBlocked = await anonymous.Client.SendAsync(spoofed);
        Assert.Equal(HttpStatusCode.TooManyRequests, stillBlocked.StatusCode);
        // Exhausting anonymous tracking must not disable the private operation.
        using var admin = await fixture.LoginAsync("admin-a");
        using var privateDetails = await admin.Client.SendAsync(admin.Api(HttpMethod.Get, $"/api/entregas/{fixture.Deliveries[0].Id}"));
        Assert.Equal(HttpStatusCode.OK, privateDetails.StatusCode);
    }

    [Fact]
    public async Task NewCodesFitExistingSchemaAndRemainQueryable()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await using var db = new MglDbContext(fixture.Options);
        var service = new TrackingService(db, Mock.Of<IHubContext<TrackingHub>>());
        var code = service.GenerateTrackingCode();
        var delivery = await db.Entregas.SingleAsync(e => e.Id == fixture.Deliveries[0].Id);
        delivery.CodigoRastreio = code;
        Assert.True(Validator.TryValidateProperty(code,
            new ValidationContext(delivery) { MemberName = nameof(Entrega.CodigoRastreio) }, new List<ValidationResult>()));
        await db.SaveChangesAsync();
        using var client = fixture.Anonymous();
        using var response = await client.GetAsync($"/api/rastreio/{code.ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
