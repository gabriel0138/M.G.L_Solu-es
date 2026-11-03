using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace MglConnect.Tests;

public class CommandCenterUiTests
{
    [Fact]
    public async Task AdministratorShellExposesWorkingOperationalNavigationAndThemeControl()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var html = await GetPageAsync(admin.Client, "/Dashboard");

        Assert.Contains("id=\"commandSidebar\"", html);
        Assert.Contains("Command Center", html);
        Assert.Contains("data-theme-toggle", html);
        var links = NavigationTargets(html);
        foreach (var target in new[] { "/Dashboard", "/Pedidos", "/Entregas", "/Clientes", "/Entregas/AlertasEmergencia", "/Roteamento" })
        {
            Assert.Contains(target, links);
            using var destination = await admin.Client.GetAsync(target);
            Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
        }
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task CustomerAndBrokerShellExposesOnlyTheirPortalDestinations(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var customer = await fixture.LoginAsync(login);
        var html = await GetPageAsync(customer.Client, "/Cliente");

        Assert.Contains("id=\"commandSidebar\"", html);
        Assert.Contains("data-theme-toggle", html);
        var links = NavigationTargets(html);
        foreach (var target in new[] { "/Cliente/Pedidos", "/Cliente/Pedido/Novo" })
        {
            Assert.Contains(target, links);
            using var destination = await customer.Client.GetAsync(target);
            Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
        }
        foreach (var target in new[] { "/Dashboard", "/Clientes", "/Pedidos", "/Pedidos/Create" })
            Assert.DoesNotContain(target, links);
    }

    [Fact]
    public async Task DriverKeepsMobileLayoutAndOfflineQueueWithoutAdministrativeSidebar()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var driver = await fixture.LoginAsync("driver-a");
        var html = await GetPageAsync(driver.Client, "/Entregas");

        Assert.DoesNotContain("id=\"commandSidebar\"", html);
        Assert.Contains("driver-experience", html);
        Assert.Contains("id=\"driverOfflineQueueState\"", html);
        Assert.Contains($"data-offline-user=\"{fixture.Users["driver-a"].Id}\"", html);
        Assert.Contains($"data-offline-tenant=\"{fixture.TenantA}\"", html);
        Assert.Contains("/js/driver-pod-queue.js", html);
    }

    [Fact]
    public async Task ProductionLoginPreservesAccessibleInputsAndAntiforgeryWithoutDemoCredentials()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var anonymous = fixture.Anonymous();
        var html = await GetPageAsync(anonymous, "/Account/Login");

        Assert.DoesNotContain("id=\"commandSidebar\"", html);
        Assert.Contains("data-toggle-password=\"Senha\"", html);
        Assert.Contains("aria-controls=\"Senha\"", html);
        Assert.Equal("username", Attribute(Input(html, "Login"), "autocomplete"));
        var password = Input(html, "Senha");
        Assert.Equal("password", Attribute(password, "type"));
        Assert.Equal("current-password", Attribute(password, "autocomplete"));
        Assert.Equal("hidden", Attribute(Input(html, "ReturnUrl"), "type"));
        Assert.False(string.IsNullOrWhiteSpace(Attribute(Input(html, "__RequestVerificationToken"), "value")));

        foreach (var developmentOnly in new[] { "cc-demo-access", "admin123", "motorista123", "cliente123", "carlos.motorista", "cliente.demo" })
            Assert.DoesNotContain(developmentOnly, html);
    }

    [Theory]
    [InlineData("/css/command-center.css", "text/css")]
    [InlineData("/js/command-center.js", "text/javascript")]
    public async Task CommandCenterAssetsAreServedWithTheirContentTypes(string path, string contentType)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var anonymous = fixture.Anonymous();
        using var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));
    }

    private static async Task<string> GetPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    private static HashSet<string> NavigationTargets(string html) => Regex
        .Matches(html, "<a\\b[^>]*>", RegexOptions.IgnoreCase)
        .Cast<Match>()
        .Select(match => Attribute(match.Value, "href"))
        .Where(target => target is not null)
        .Select(target => target!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Input(string html, string name) => Assert.Single(Regex
        .Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase)
        .Cast<Match>()
        .Select(match => match.Value), tag => Attribute(tag, "name") == name);

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"""(?:^|\s){Regex.Escape(name)}\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }
}
