using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MglConnect.Tests;

public class B01HttpAuthorizationTests
{
    [Theory]
    [InlineData("admin-a", true, true, false)]
    [InlineData("admin-b", false, false, true)]
    [InlineData("client-a", true, false, false)]
    [InlineData("broker-a", true, false, false)]
    [InlineData("client-a-other", false, true, false)]
    [InlineData("broker-b", false, false, true)]
    [InlineData("driver-a", true, false, false)]
    [InlineData("driver-a-other", false, true, false)]
    public async Task ApiResourceMatrixHonorsOrganizationCustomerAndAssignment(string login, bool first, bool second, bool third)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync(login);
        var permissions = new[] { first, second, third };
        for (var i = 0; i < 3; i++)
        {
            using var response = await session.Client.SendAsync(session.Api(HttpMethod.Get, $"/api/entregas/{fixture.Deliveries[i].Id}"));
            Assert.Equal(permissions[i] ? HttpStatusCode.OK
                : fixture.Users[login].TenantId == fixture.Deliveries[i].TenantId ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, response.StatusCode);
        }
        if (login.StartsWith("client", StringComparison.Ordinal) || login.StartsWith("broker", StringComparison.Ordinal))
        {
            for (var i = 0; i < 3; i++)
                using (var response = await session.Client.GetAsync($"/Cliente/Pedido/{fixture.Deliveries[i].PedidoId}"))
                    Assert.Equal(permissions[i] ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task AdminCannotOverwriteForeignCustomerByForgingPostedTenant()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var customer = fixture.Customers[2];
        var form = CustomerForm(customer.Id, await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Clientes/Create"));
        form["TenantId"] = fixture.TenantA.ToString();
        using var response = await admin.Client.PostAsync($"/Clientes/Edit/{customer.Id}", new FormUrlEncodedContent(form));
        await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
        var persisted = await db.Clientes.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(fixture.TenantB, persisted.TenantId);
        Assert.Equal(customer.RazaoSocial, persisted.RazaoSocial);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LegitimateCustomerEditPreservesServerOwnedTenantAndCreationDate()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var customer = fixture.Customers[0];
        var form = CustomerForm(customer.Id, await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Clientes/Create"));
        form["DataCadastro"] = "2001-01-01";
        using var response = await admin.Client.PostAsync($"/Clientes/Edit/{customer.Id}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
        var persisted = await db.Clientes.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(fixture.TenantA, persisted.TenantId);
        Assert.Equal("Authorized edit", persisted.RazaoSocial);
        Assert.Equal(customer.DataCadastro, persisted.DataCadastro);
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task InactiveCustomerCannotReadDeliveryViaApiOrSubscribeToTelemetry(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync(login);
        await fixture.ChangeAsync(db => db.Clientes.Single(c => c.Id == fixture.Customers[0].Id).Ativo = false);
        using var response = await session.Client.SendAsync(session.Api(HttpMethod.Get, $"/api/entregas/{fixture.Deliveries[0].Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var hub = await SecurityHubConnection.ConnectAsync(fixture, session);
        Assert.True((await hub.InvokeAsync("JoinLiveTracking", fixture.Deliveries[0].CodigoRastreio)).TryGetProperty("error", out _));
    }

    [Fact]
    public async Task AdminOrderCreationIgnoresPostedEntityGraphAndServerFields()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Pedidos/Create");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["ClienteId"] = fixture.Customers[0].Id.ToString(),
            ["Descricao"] = "Authorized new order", ["PesoKg"] = "10", ["TipoCarga"] = "Carga Geral Seca",
            ["TenantId"] = fixture.TenantB.ToString(), ["Status"] = "Concluido",
            ["Entrega.CodigoRastreio"] = "MGL-B01-INJECTEDBR", ["Entrega.DestinatarioNome"] = "Injected graph",
            ["Entrega.EnderecoDestino"] = "Test", ["Entrega.CidadeDestino"] = "Test", ["Entrega.EstadoDestino"] = "SP",
            ["Entrega.CepDestino"] = "00000-000", ["Entrega.VeiculoId"] = fixture.Deliveries[2].VeiculoId.ToString()!,
            ["Entrega.MotoristaId"] = fixture.Users["driver-b"].Id.ToString(), ["Entrega.AtribuicaoAtiva"] = "false"
        };
        using var response = await admin.Client.PostAsync("/Pedidos/Create", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
        var order = await db.Pedidos.SingleAsync(p => p.Descricao == "Authorized new order");
        Assert.Equal(fixture.TenantA, order.TenantId);
        Assert.Equal("Registrado", order.Status);
        Assert.False(await db.Entregas.AnyAsync(e => e.PedidoId == order.Id));
    }

    [Fact]
    public async Task AuthenticationAndRevocationAreEnforcedByRealHttpPipeline()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var anonymous = fixture.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/entregas/1")).StatusCode);
        var login = await anonymous.GetAsync("/Pedidos");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("http://127.0.0.1", login.Headers.Location!.AbsoluteUri);
        Assert.Contains("/Account/Login", login.Headers.Location.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { login = "admin-a", senha = "wrong" })).StatusCode);
        using var admin = await fixture.LoginAsync("admin-a");
        await fixture.ChangeAsync(db => db.Usuarios.Single(u => u.Id == fixture.Users["admin-a"].Id).Ativo = false);
        using var response = await admin.Client.SendAsync(admin.Api(HttpMethod.Get, "/api/entregas/1"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var negotiation = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, "/telemetryHub/negotiate?negotiateVersion=1"));
        Assert.Equal(HttpStatusCode.Unauthorized, negotiation.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { login = "admin-a", senha = "B01-isolated-fixture-only!" })).StatusCode);
    }

    [Fact]
    public async Task DriverNeedsOwnAssignmentAndActiveShiftForOperationalHttpAccess()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        using var driver = await fixture.LoginAsync("driver-a");
        using var denied = await driver.Client.SendAsync(driver.Api(HttpMethod.Get, $"/api/entregas/{fixture.Deliveries[0].Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var list = await driver.Client.SendAsync(driver.Api(HttpMethod.Get, $"/api/entregas/motorista/{fixture.Users["driver-a"].Id}"));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Private B01 cargo", listJson);
        Assert.DoesNotContain("destinatarioNome", listJson);
        using var other = await driver.Client.SendAsync(driver.Api(HttpMethod.Get, $"/api/entregas/motorista/{fixture.Users["driver-a-other"].Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        var token = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas");
        using var pod = await driver.Client.PostAsync("/Entregas/FinalizarEntregaPOD", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["entregaId"] = fixture.Deliveries[1].Id.ToString(), ["nomeRecebedor"] = "Fixture"
        }));
        Assert.True(pod.StatusCode == HttpStatusCode.Forbidden || pod.StatusCode == HttpStatusCode.Redirect);
        if (pod.StatusCode == HttpStatusCode.Redirect) Assert.Contains("AccessDenied", pod.Headers.Location!.ToString());
    }

    [Fact]
    public async Task HubSubscriptionsAndPublicationRespectRolesAndResourceIds()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var client = await fixture.LoginAsync("client-a");
        await using var customerHub = await SecurityHubConnection.ConnectAsync(fixture, client);
        Assert.False((await customerHub.InvokeAsync("JoinLiveTracking", fixture.Deliveries[0].CodigoRastreio)).TryGetProperty("error", out _));
        foreach (var other in fixture.Deliveries.Skip(1))
            Assert.True((await customerHub.InvokeAsync("JoinLiveTracking", other.CodigoRastreio)).TryGetProperty("error", out _));
        Assert.True((await customerHub.InvokeAsync("SendVehicleTelemetry", fixture.Deliveries[0].CodigoRastreio, -23d, -46d, 40d, 90d, 10d, false)).TryGetProperty("error", out _));
        using var driver = await fixture.LoginAsync("driver-a");
        await using var driverHub = await SecurityHubConnection.ConnectAsync(fixture, driver);
        Assert.False((await driverHub.InvokeAsync("JoinLiveTracking", fixture.Deliveries[0].CodigoRastreio)).TryGetProperty("error", out _));
        Assert.False((await driverHub.InvokeAsync("SendVehicleTelemetry", fixture.Deliveries[0].CodigoRastreio, -23d, -46d, 40d, 90d, 10d, false)).TryGetProperty("error", out _));
        Assert.True((await driverHub.InvokeAsync("SendVehicleTelemetry", fixture.Deliveries[1].CodigoRastreio, -23d, -46d, 40d, 90d, 10d, false)).TryGetProperty("error", out _));
        Assert.True((await driverHub.InvokeAsync("SimulateDeviationTrigger", fixture.Deliveries[0].CodigoRastreio, 0.001d, 0.001d)).TryGetProperty("error", out _));
        using var admin = await fixture.LoginAsync("admin-a");
        await using var adminHub = await SecurityHubConnection.ConnectAsync(fixture, admin);
        Assert.False((await adminHub.InvokeAsync("JoinLiveTracking", fixture.Deliveries[0].CodigoRastreio)).TryGetProperty("error", out _));
        Assert.True((await adminHub.InvokeAsync("JoinLiveTracking", fixture.Deliveries[2].CodigoRastreio)).TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("role")]
    [InlineData("customer")]
    [InlineData("tenant")]
    [InlineData("shift")]
    [InlineData("assignment")]
    [InlineData("customer-active")]
    public async Task ExistingTelemetrySubscriptionLosesAccessWhenAuthorizationChanges(string reason)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var login = reason is "shift" or "assignment" ? "driver-a" : "client-a";
        using var recipient = await fixture.LoginAsync(login);
        using var publisher = await fixture.LoginAsync("admin-a");
        await using var observer = await SecurityHubConnection.ConnectAsync(fixture, recipient);
        await using var sender = await SecurityHubConnection.ConnectAsync(fixture, publisher);
        var code = fixture.Deliveries[0].CodigoRastreio;
        Assert.False((await observer.InvokeAsync("JoinLiveTracking", code)).TryGetProperty("error", out _));
        Assert.False((await sender.InvokeAsync("SendVehicleTelemetry", code, -23d, -46d, 40d, 90d, 10d, false)).TryGetProperty("error", out _));
        Assert.True(await observer.ReceivesAsync("ReceiveVehiclePosition", TimeSpan.FromSeconds(3)));
        await fixture.ChangeAsync(db =>
        {
            var user = db.Usuarios.Single(u => u.Id == fixture.Users[login].Id);
            switch (reason)
            {
                case "account": user.Ativo = false; break;
                case "role": user.Perfil = "Motorista"; break;
                case "customer": user.ClienteId = fixture.Customers[1].Id; break;
                case "tenant": user.TenantId = fixture.TenantB; break;
                case "shift": db.TurnosMotorista.Single(t => t.MotoristaId == user.Id).Ativo = false; break;
                case "assignment": db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id).MotoristaId = null; break;
                case "customer-active": db.Clientes.Single(c => c.Id == fixture.Customers[0].Id).Ativo = false; break;
            }
        });
        var afterRevocation = await sender.InvokeAsync("SendVehicleTelemetry", code, -23d, -46d, 40d, 90d, 10d, false);
        // GPS history requires an actual assigned driver. Even an administrator
        // cannot create history after that link or its active shift is removed.
        Assert.Equal(reason is "assignment" or "shift", afterRevocation.TryGetProperty("error", out _));
        if (reason is "assignment" or "shift")
        {
            await using var verify = new MglConnect.Web.Data.MglDbContext(fixture.Options);
            Assert.Equal(1, await verify.PosicoesGps.CountAsync());
        }
        Assert.False(await observer.ReceivesAsync("ReceiveVehiclePosition", TimeSpan.FromMilliseconds(750)));
    }

    [Fact]
    public async Task PodReportsAndMutationsEnforceResourceOwnershipThroughHttp()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        using var client = await fixture.LoginAsync("client-a");
        using var broker = await fixture.LoginAsync("broker-a");
        var id = fixture.Deliveries[0].Id;
        using var forbidden = await client.Client.SendAsync(client.Api(HttpMethod.Post, $"/api/entregas/{id}/pod", new { nomeRecebedor = "Fixture" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var confirmed = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, $"/api/entregas/{id}/pod", new { nomeRecebedor = "Fixture" }));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        foreach (var actor in new[] { client, broker })
        {
            using var pdf = await actor.Client.SendAsync(actor.Api(HttpMethod.Get, $"/api/entregas/{id}/pod-pdf"));
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        }
        using var otherClient = await fixture.LoginAsync("client-a-other");
        using var otherPdf = await otherClient.Client.SendAsync(otherClient.Api(HttpMethod.Get, $"/api/entregas/{id}/pod-pdf"));
        Assert.Equal(HttpStatusCode.Forbidden, otherPdf.StatusCode);
        using var foreignAdmin = await fixture.LoginAsync("admin-b");
        using var foreignPdf = await foreignAdmin.Client.SendAsync(foreignAdmin.Api(HttpMethod.Get, $"/api/entregas/{id}/pod-pdf"));
        Assert.Equal(HttpStatusCode.NotFound, foreignPdf.StatusCode);
        using var esg = await admin.Client.SendAsync(admin.Api(HttpMethod.Get, $"/api/entregas/{id}/esg"));
        Assert.Equal(HttpStatusCode.OK, esg.StatusCode);
        using var forbiddenReport = await client.Client.SendAsync(client.Api(HttpMethod.Get, $"/api/entregas/{id}/esg"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReport.StatusCode);
        using var foreignReport = await foreignAdmin.Client.SendAsync(foreignAdmin.Api(HttpMethod.Get, $"/api/entregas/{id}/esg"));
        Assert.Equal(HttpStatusCode.NotFound, foreignReport.StatusCode);
    }

    [Fact]
    public async Task BrokerCookieLoginAndHomeRedirectToAuthorizedPortal()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var browser = fixture.Anonymous();
        var token = await SecurityHttpFixture.AntiForgeryAsync(browser);
        using var response = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Login"] = "broker-a", ["Senha"] = "B01-isolated-fixture-only!"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Cliente", response.Headers.Location!.ToString());
        using var home = await browser.GetAsync("/");
        Assert.Equal("/Cliente", home.Headers.Location!.ToString());
    }

    [Fact]
    public async Task DriverCannotModifyAdministrativeResourcesOrBypassAntiforgery()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var driver = await fixture.LoginAsync("driver-a");
        foreach (var path in new[] { "/Clientes", "/Pedidos", "/Dashboard", "/Roteamento", "/Entregas/AlertasEmergencia" })
        {
            using var response = await driver.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        }
        using var admin = await fixture.LoginAsync("admin-a");
        using var noCsrf = await admin.Client.PostAsync($"/Clientes/Edit/{fixture.Customers[0].Id}", new FormUrlEncodedContent(CustomerForm(fixture.Customers[0].Id, "invalid")));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
        Assert.Equal(fixture.Customers[0].RazaoSocial, (await db.Clientes.SingleAsync(c => c.Id == fixture.Customers[0].Id)).RazaoSocial);
    }

    [Fact]
    public async Task CustomerCreateAcceptsOnlyItsFormFieldsAndRejectsNestedOrders()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var form = CustomerForm(9999, await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Clientes/Create"));
        form["TenantId"] = fixture.TenantB.ToString();
        form["Pedidos[0].NumeroPedido"] = "INJECTED";
        form["Pedidos[0].Descricao"] = "Unauthorized nested order";
        form["Pedidos[0].ClienteId"] = fixture.Customers[2].Id.ToString();
        using var response = await admin.Client.PostAsync("/Clientes/Create", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
        var created = await db.Clientes.SingleAsync(c => c.RazaoSocial == "Authorized edit");
        Assert.Equal(fixture.TenantA, created.TenantId);
        Assert.NotEqual(9999, created.Id);
        Assert.Equal(3, await db.Pedidos.CountAsync());
    }

    [Fact]
    public async Task HttpShiftCodeFlowRequiresOwnAssignmentExpiresAndIsSingleUse()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        using var admin = await fixture.LoginAsync("admin-a");
        using var driver = await fixture.LoginAsync("driver-a");
        using var other = await fixture.LoginAsync("driver-a-other");
        var id = fixture.Deliveries[0].Id;
        var adminToken = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{id}");
        async Task<string> IssueAsync()
        {
            using var issue = await admin.Client.PostAsync("/Entregas/EmitirCodigoTurno", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = adminToken, ["entregaId"] = id.ToString()
            }));
            Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
            Assert.Contains("no-store", issue.Headers.CacheControl!.ToString());
            var match = System.Text.RegularExpressions.Regex.Match(await issue.Content.ReadAsStringAsync(), "aria-label=\"Código temporário\">([^<]+)</output>");
            Assert.True(match.Success);
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }
        async Task StartAsync(SecurityHttpFixture.Session actor, string code)
        {
            var token = await SecurityHttpFixture.AntiForgeryAsync(actor.Client, "/Entregas");
            using var response = await actor.Client.PostAsync("/Entregas/IniciarTurno", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["entregaId"] = id.ToString(), ["codigo"] = code
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        var expired = await IssueAsync();
        await fixture.ChangeAsync(db => db.CodigosTurnoMotorista.Single(c => c.EntregaId == id).ExpiraEmUtc = DateTime.UtcNow.AddMinutes(-1));
        await StartAsync(driver, expired);
        await using (var db = new MglConnect.Web.Data.MglDbContext(fixture.Options)) Assert.False(await db.TurnosMotorista.AnyAsync());
        var valid = await IssueAsync();
        await StartAsync(other, valid);
        await StartAsync(driver, "incorrect");
        await using (var db = new MglConnect.Web.Data.MglDbContext(fixture.Options)) Assert.False(await db.TurnosMotorista.AnyAsync());
        await StartAsync(driver, valid);
        await StartAsync(driver, valid); // response retry, at most one active shift
        await using (var db = new MglConnect.Web.Data.MglDbContext(fixture.Options))
        {
            Assert.Equal(1, await db.TurnosMotorista.CountAsync());
            Assert.True(await db.TurnosMotorista.AllAsync(t => t.MotoristaId == fixture.Users["driver-a"].Id));
        }
        var endToken = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas");
        using var ended = await driver.Client.PostAsync("/Entregas/EncerrarTurno", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = endToken }));
        Assert.Equal(HttpStatusCode.Redirect, ended.StatusCode);
        await StartAsync(driver, valid); // consumed code must not create a second shift
        await using (var db = new MglConnect.Web.Data.MglDbContext(fixture.Options)) Assert.False(await db.TurnosMotorista.AnyAsync(t => t.Ativo));
    }

    [Fact]
    public async Task ExpiredAndMalformedBearerDoNotFallBackToValidCookie()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync("admin-a");
        foreach (var token in new[] { "not-a-protected-ticket", fixture.IssueTestToken("admin-a", DateTimeOffset.UtcNow.AddMinutes(-1)) })
        {
            var expired = session with { Token = token };
            using var response = await session.Client.SendAsync(expired.Api(HttpMethod.Get, "/api/entregas/1"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var valid = await session.Client.SendAsync(session.Api(HttpMethod.Get, "/api/entregas/1"));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task TelemetryConnectionCannotOutliveItsAuthenticationTicket()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync("client-a");
        var expires = DateTimeOffset.UtcNow.AddSeconds(4);
        var shortSession = session with { Token = fixture.IssueTestToken("client-a", expires) };
        await using var hub = await SecurityHubConnection.ConnectAsync(fixture, shortSession, useBearer: true);
        Assert.False((await hub.InvokeAsync("JoinLiveTracking", fixture.Deliveries[0].CodigoRastreio)).TryGetProperty("error", out _));
        var delay = expires - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        // Dispatcher expiration is scheduled, not a sub-second timer guarantee.
        // Require a real server close frame, never a client timeout/abort.
        Assert.True(await hub.ClosedByServerAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AdministrativeCommandsDoNotMutateForeignOrganizationResources()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        using var client = await fixture.LoginAsync("client-a");
        var other = fixture.Deliveries[2];
        foreach (var path in new[] { $"/Clientes/Details/{fixture.Customers[2].Id}", $"/Pedidos/Details/{other.PedidoId}", $"/Entregas/Details/{other.Id}" })
            using (var response = await admin.Client.GetAsync(path)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Clientes/Create");
        foreach (var action in new[] { "AtribuirMotorista", "AtribuirVeiculo" })
        {
            using var response = await admin.Client.PostAsync("/Entregas/" + action, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["entregaId"] = other.Id.ToString(),
                ["motoristaId"] = fixture.Users["driver-a"].Id.ToString(), ["veiculoId"] = fixture.Deliveries[0].VeiculoId.ToString()!
            }));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var foreignCancel = await admin.Client.SendAsync(admin.Api(HttpMethod.Put, $"/api/entregas/{other.Id}/status", new { status = "Cancelada" }));
        Assert.Equal(HttpStatusCode.NotFound, foreignCancel.StatusCode);
        using var customerCancel = await client.Client.SendAsync(client.Api(HttpMethod.Put, $"/api/entregas/{fixture.Deliveries[0].Id}/status", new { status = "Cancelada" }));
        Assert.Equal(HttpStatusCode.Forbidden, customerCancel.StatusCode);
        using var legitimate = await admin.Client.SendAsync(admin.Api(HttpMethod.Put, $"/api/entregas/{fixture.Deliveries[1].Id}/status", new { status = "Cancelada" }));
        Assert.Equal(HttpStatusCode.OK, legitimate.StatusCode);
        await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
        var preserved = await db.Entregas.SingleAsync(e => e.Id == other.Id);
        Assert.Equal(other.MotoristaId, preserved.MotoristaId);
        Assert.Equal(other.VeiculoId, preserved.VeiculoId);
        Assert.Equal("Em transporte", preserved.StatusEntrega);
        Assert.Equal("Cancelada", (await db.Entregas.SingleAsync(e => e.Id == fixture.Deliveries[1].Id)).StatusEntrega);
    }

    [Fact]
    public async Task EmergencyCreationAndAcknowledgementHonorDriverRoleAndOrganization()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var driver = await fixture.LoginAsync("driver-a");
        using var otherDriver = await fixture.LoginAsync("driver-a-other");
        using var foreignAdmin = await fixture.LoginAsync("admin-b");
        using var admin = await fixture.LoginAsync("admin-a");
        var key = Guid.NewGuid();
        async Task<HttpResponseMessage> CreateAsync(SecurityHttpFixture.Session actor)
        {
            var token = await SecurityHttpFixture.AntiForgeryAsync(actor.Client, "/Entregas");
            return await actor.Client.PostAsync("/Entregas/CriarAlertaEmergencia", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["chaveIdempotencia"] = key.ToString(), ["entregaId"] = fixture.Deliveries[0].Id.ToString()
            }));
        }
        using var invalidAssignment = await CreateAsync(otherDriver);
        Assert.Equal(HttpStatusCode.Conflict, invalidAssignment.StatusCode);
        using var created = await CreateAsync(driver);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("alertaId").GetGuid();
        using var retry = await CreateAsync(driver);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var reusedByOther = await CreateAsync(otherDriver);
        Assert.Equal(HttpStatusCode.Conflict, reusedByOther.StatusCode);
        foreach (var actor in new[] { foreignAdmin, admin })
        {
            var token = await SecurityHttpFixture.AntiForgeryAsync(actor.Client, "/Entregas/AlertasEmergencia");
            using var response = await actor.Client.PostAsync("/Entregas/ReconhecerAlertaEmergencia", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["id"] = id.ToString()
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            await using var db = new MglConnect.Web.Data.MglDbContext(fixture.Options);
            var alert = await db.AlertasEmergencia.SingleAsync();
            Assert.Equal(actor == foreignAdmin ? "Aberto" : "Reconhecido", alert.Status);
            Assert.Equal(fixture.TenantA, alert.TenantId);
            if (actor == admin) Assert.Equal(fixture.Users["admin-a"].Id, alert.ReconhecidoPorUsuarioId);
        }
    }

    private static Dictionary<string, string> CustomerForm(int id, string token) => new()
    {
        ["Id"] = id.ToString(), ["RazaoSocial"] = "Authorized edit", ["Cnpj"] = "B01-edit", ["Telefone"] = "00000000",
        ["Ativo"] = "true", ["__RequestVerificationToken"] = token
    };
}
