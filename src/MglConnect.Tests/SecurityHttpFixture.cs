using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

// Real Kestrel, real cookie/bearer handlers and real SQLite. No replacement auth
// handler, TestServer, Docker, global environment changes or existing database.
internal sealed class SecurityHttpFixture : IAsyncDisposable
{
    private Process _server;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mgl-b01-{Guid.NewGuid():N}");
    public Guid TenantA { get; } = Guid.NewGuid();
    public Guid TenantB { get; } = Guid.NewGuid();
    public Uri Address { get; private set; } = null!;
    public Dictionary<string, Usuario> Users { get; } = new();
    public List<Cliente> Customers { get; } = new();
    public List<Entrega> Deliveries { get; } = new();
    public DbContextOptions<MglDbContext> Options { get; private set; }
    public string DatabaseFile => new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(_server.StartInfo.Environment["ConnectionStrings__DefaultConnection"]).DataSource;
    public string KeysPath => _server.StartInfo.Environment["DataProtection__KeysPath"]!;
    internal const string Password = "B01-isolated-fixture-only!";

    private SecurityHttpFixture(bool demoData = false, int? gpsRetentionDays = null)
    {
        Directory.CreateDirectory(_directory);
        Options = new DbContextOptionsBuilder<MglDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "fixture.db")};Foreign Keys=True").Options;
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source != null && !File.Exists(Path.Combine(source.FullName, "MglConnect.sln"))) source = source.Parent;
        if (source == null) throw new InvalidOperationException("Run integration tests from a checkout containing MglConnect.sln.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var webRoot = Path.Combine(source.FullName, "src", "MglConnect.Web");
        var assembly = Path.Combine(webRoot, "bin", configuration, "net10.0", "MglConnect.Web.dll");
        var dotnet = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var start = new ProcessStartInfo(Path.GetFullPath(dotnet))
        {
            WorkingDirectory = webRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(assembly);
        foreach (var variable in new[] { "POSTGRES_URL", "DATABASE_URL", "POSTGRES_PRISMA_URL", "PORT", "VERCEL" })
            start.Environment.Remove(variable);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        if (gpsRetentionDays.HasValue) start.Environment["GpsHistory__DiasAntesCompactar"] = gpsRetentionDays.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["DatabaseProvider"] = "Sqlite";
        start.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={Path.Combine(_directory, "fixture.db")};Foreign Keys=True";
        start.Environment["DemoData__Enabled"] = demoData ? "true" : "false";
        if (demoData)
        {
            start.Environment["DemoData__AdminPassword"] = Password;
            start.Environment["DemoData__DriverPassword"] = Password;
            start.Environment["DemoData__ClientPassword"] = Password;
        }
        start.Environment["DataProtection__KeysPath"] = Path.Combine(_directory, "keys");
        start.Environment["Geocoding__PhotonEnabled"] = "false";
        start.Environment["Routing__OsrmBaseUrl"] = "";
        start.Environment["GoogleMaps__ApiKey"] = "";
        start.Environment["GoogleMaps__GeocodingApiKey"] = "";
        start.Environment.Remove("GOOGLE_MAPS_API_KEY");
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        _server = new Process { StartInfo = start };
    }

    public static async Task<SecurityHttpFixture> CreateAsync(bool activeShifts = true, bool startServer = true, bool demoData = false, int? gpsRetentionDays = null, string? restorePackage = null, string? postgresConnection = null, bool seedDatabase = true, IReadOnlyDictionary<string,string>? hostingConfiguration = null)
    {
        var fixture = new SecurityHttpFixture(demoData, gpsRetentionDays);
        try
        {
            foreach(var key in fixture._server.StartInfo.Environment.Keys.Where(k=>k.StartsWith("Hosting__")||k.StartsWith("ReverseProxy__")||k=="ASPNETCORE_FORWARDEDHEADERS_ENABLED").ToArray())fixture._server.StartInfo.Environment.Remove(key);
            if(hostingConfiguration!=null)foreach(var setting in hostingConfiguration)fixture._server.StartInfo.Environment[setting.Key]=setting.Value;
            if (postgresConnection != null)
            {
                var pg=new Npgsql.NpgsqlConnectionStringBuilder(postgresConnection);
                if(pg.Database?.StartsWith("mgl_stage6_",StringComparison.Ordinal)!=true)throw new InvalidOperationException("Somente banco PostgreSQL novo da fixture é permitido.");
                fixture.Options=new DbContextOptionsBuilder<MglDbContext>().UseNpgsql(postgresConnection).Options;
                fixture._server.StartInfo.Environment["DatabaseProvider"]="Postgres";
                fixture._server.StartInfo.Environment["ConnectionStrings__DefaultConnection"]=postgresConnection;
            }
            if (restorePackage != null)
            {
                var restored=Path.Combine(fixture._directory,"restored");
                await MglConnect.Web.Operations.SqliteRecovery.RestoreAsync(restorePackage,restored);
                var cs=$"Data Source={Path.Combine(restored,MglConnect.Web.Operations.SqliteRecovery.DatabaseName)};Foreign Keys=True";
                fixture.Options=new DbContextOptionsBuilder<MglDbContext>().UseSqlite(cs).Options;
                fixture._server.StartInfo.Environment["ConnectionStrings__DefaultConnection"]=cs;
                fixture._server.StartInfo.Environment["DataProtection__KeysPath"]=Path.Combine(restored,"keys");
            }
            else if (!demoData && seedDatabase) await fixture.SeedAsync(activeShifts);
            if (!startServer) return fixture;
            var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture._server.OutputDataReceived += (_, e) =>
            {
                const string prefix = "Now listening on: ";
                var index = e.Data?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;
                if (index >= 0 && Uri.TryCreate(e.Data![(index + prefix.Length)..].Trim(), UriKind.Absolute, out var uri))
                    ready.TrySetResult(uri);
            };
            fixture._server.ErrorDataReceived += (_, _) => { }; // never echo process configuration/secrets
            fixture._server.Start();
            fixture._server.BeginOutputReadLine();
            fixture._server.BeginErrorReadLine();
            fixture.Address = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public long WorkingSetBytes() { _server.Refresh(); return _server.WorkingSet64; }

    public async Task RestartAsync()
    {
        _server.Kill(entireProcessTree: true);
        await _server.WaitForExitAsync();
        var start = _server.StartInfo;
        _server.Dispose();
        _server = new Process { StartInfo = start };
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        System.Diagnostics.DataReceivedEventHandler handler = (_, e) =>
        {
            const string prefix = "Now listening on: ";
            var index = e.Data?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;
            if (index >= 0 && Uri.TryCreate(e.Data![(index + prefix.Length)..].Trim(), UriKind.Absolute, out var uri)) ready.TrySetResult(uri);
        };
        _server.OutputDataReceived += handler;
        try
        {
            _server.Start(); _server.BeginOutputReadLine(); _server.BeginErrorReadLine();
            Address = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { _server.OutputDataReceived -= handler; }
    }

    public HttpClient Anonymous(CookieContainer? cookies = null) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false, CookieContainer = cookies ?? new CookieContainer()
    }) { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(15) };

    public async Task<Session> LoginAsync(string name)
    {
        var cookies = new CookieContainer();
        var client = Anonymous(cookies);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { login = name, senha = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Session(client, cookies, json.RootElement.GetProperty("token").GetString()!);
    }

    public static async Task<string> AntiForgeryAsync(HttpClient client, string page = "/Account/Login")
    {
        using var response = await client.GetAsync(page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected a real MVC antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public async Task ChangeAsync(Action<MglDbContext> change)
    {
        await using var database = new MglDbContext(Options);
        change(database);
        await database.SaveChangesAsync();
    }

    public string IssueTestToken(string login, DateTimeOffset expires)
    {
        var user = Users[login];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Perfil == "Administrador" ? "Admin" : user.Perfil),
            new Claim("TenantId", user.TenantId.ToString())
        }, MglConnectApiBearerDefaults.Scheme));
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_directory, "keys")),
            configuration => configuration.SetApplicationName("MglConnect"));
        return ApiBearerToken.Protect(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-2), ExpiresUtc = expires
        }, provider);
    }

    private async Task SeedAsync(bool activeShifts)
    {
        await using var db = new MglDbContext(Options);
        await db.Database.EnsureCreatedAsync();
        db.Empresas.AddRange(new Empresa { Id = TenantA, Nome = "B01 Org A", Slug = "b01-a" },
            new Empresa { Id = TenantB, Nome = "B01 Org B", Slug = "b01-b" });
        var hash = new SimplePasswordHasher().HashPassword(Password);
        for (var i = 0; i < 3; i++)
        {
            var tenant = i < 2 ? TenantA : TenantB;
            var customer = new Cliente { TenantId = tenant, RazaoSocial = $"B01 Customer {i}", Cnpj = $"B01-{i}", Telefone = "00000000" };
            Customers.Add(customer);
            db.Add(customer);
        }
        await db.SaveChangesAsync();
        AddUser("admin-a", "Admin", TenantA);
        AddUser("admin-b", "Administrador", TenantB);
        for (var i = 0; i < 3; i++)
        {
            var suffix = i == 0 ? "a" : i == 1 ? "a-other" : "b";
            var tenant = Customers[i].TenantId;
            AddUser($"client-{suffix}", "Cliente", tenant, Customers[i].Id);
            AddUser($"broker-{suffix}", "Intermediador", tenant, Customers[i].Id);
            AddUser($"driver-{suffix}", "Motorista", tenant);
        }
        await db.SaveChangesAsync();
        for (var i = 0; i < 3; i++)
        {
            var suffix = i == 0 ? "a" : i == 1 ? "a-other" : "b";
            var tenant = Customers[i].TenantId;
            var vehicle = new Veiculo { TenantId = tenant, Placa = $"B01{i:D4}", Modelo = "Fixture" };
            var order = new Pedido { TenantId = tenant, ClienteId = Customers[i].Id, NumeroPedido = $"B01-{i}", Descricao = $"Private B01 cargo {i}" };
            db.AddRange(vehicle, order);
            await db.SaveChangesAsync();
            var delivery = new Entrega
            {
                TenantId = tenant, PedidoId = order.Id, MotoristaId = Users[$"driver-{suffix}"].Id,
                VeiculoId = vehicle.Id, CodigoRastreio = $"MGL-B01-{i}BR", DestinatarioNome = "Fixture receiver",
                EnderecoDestino = "Fixture street", CidadeDestino = "Fixture city", EstadoDestino = "SP", CepDestino = "00000-000",
                AtribuicaoAtiva = true, StatusEntrega = activeShifts ? "Em transporte" : "Pendente"
            };
            db.Add(delivery);
            await db.SaveChangesAsync();
            Deliveries.Add(delivery);
            if (activeShifts)
            {
                var code = new CodigoTurnoMotorista
                {
                    TenantId = tenant, EntregaId = delivery.Id, MotoristaId = delivery.MotoristaId!.Value,
                    CodigoHash = new string('A', 64), EmitidoPorUsuarioId = Users[i < 2 ? "admin-a" : "admin-b"].Id,
                    EmitidoEmUtc = DateTime.UtcNow, ExpiraEmUtc = DateTime.UtcNow.AddMinutes(15), UsadoEmUtc = DateTime.UtcNow
                };
                db.Add(code);
                db.Add(new TurnoMotorista
                {
                    TenantId = tenant, EntregaId = delivery.Id, MotoristaId = delivery.MotoristaId.Value,
                    VeiculoId = vehicle.Id, CodigoTurnoId = code.Id, IniciadoEmUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }
        void AddUser(string login, string role, Guid tenant, int? customer = null)
        {
            var user = new Usuario { Login = login, Nome = login, Perfil = role, TenantId = tenant, ClienteId = customer, SenhaHash = hash };
            Users.Add(login, user);
            db.Add(user);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server.IdSafe() && !_server.HasExited)
        {
            _server.Kill(entireProcessTree: true); // only this fixture's owned process
            await _server.WaitForExitAsync();
        }
        _server.Dispose();
        // Keep clearly named temporary SQLite/key fixtures; never clean user data.
    }

    internal sealed record Session(HttpClient Client, CookieContainer Cookies, string Token) : IDisposable
    {
        public HttpRequestMessage Api(HttpMethod method, string path, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Bearer", Token);
            if (body != null) request.Content = JsonContent.Create(body);
            return request;
        }
        public void Dispose() => Client.Dispose();
    }
}

internal static class OwnedProcessExtensions
{
    public static bool IdSafe(this Process process)
    {
        try { return process.Id > 0; } catch (InvalidOperationException) { return false; }
    }
}

internal sealed class SecurityHubConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private SecurityHubConnection(WebSocket socket) => _socket = socket;
    private readonly Queue<JsonElement> _messages = new();
    private string _pending = "";
    private int _id;
    public static async Task<SecurityHubConnection> ConnectAsync(SecurityHttpFixture fixture,
        SecurityHttpFixture.Session session, string path = "/telemetryHub", bool useBearer = false)
    {
        using var request = useBearer ? session.Api(HttpMethod.Post, path + "/negotiate?negotiateVersion=1")
            : new HttpRequestMessage(HttpMethod.Post, path + "/negotiate?negotiateVersion=1");
        using var response = await session.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var socket = new ClientWebSocket();
        socket.Options.Cookies = session.Cookies;
        if (useBearer) socket.Options.SetRequestHeader("Authorization", "Bearer " + session.Token);
        var uri = new UriBuilder(fixture.Address) { Scheme = "ws", Path = path, Query = "id=" + Uri.EscapeDataString(json.RootElement.GetProperty("connectionToken").GetString()!) }.Uri;
        await socket.ConnectAsync(uri, CancellationToken.None);
        return await FromSocketAsync(socket);
    }
    public static async Task<SecurityHubConnection> FromSocketAsync(WebSocket socket)
    {
        var connection = new SecurityHubConnection(socket);
        await connection.SendAsync(new { protocol = "json", version = 1 });
        Assert.False((await connection.ReadAsync(TimeSpan.FromSeconds(5))).TryGetProperty("error", out _));
        return connection;
    }
    private Task SendAsync(object message) => _socket.SendAsync(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + "\u001e"), WebSocketMessageType.Text, true, CancellationToken.None);
    public async Task<JsonElement> InvokeAsync(string method, params object?[] arguments)
    {
        var id = (++_id).ToString();
        await SendAsync(new { type = 1, invocationId = id, target = method, arguments });
        while (true)
        {
            var message = await ReadAsync(TimeSpan.FromSeconds(5));
            if (message.TryGetProperty("invocationId", out var value) && value.GetString() == id) return message;
        }
    }
    public async Task<bool> ReceivesAsync(string target, TimeSpan timeout)
    {
        try
        {
            while (true)
            {
                var message = await ReadAsync(timeout);
                if (message.TryGetProperty("target", out var name) && name.GetString() == target) return true;
            }
        }
        catch (OperationCanceledException) { return false; }
    }
    public async Task<JsonElement> EventPayloadAsync(string target, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("Expected hub event was not received.");
            var message = await ReadAsync(remaining);
            if (message.TryGetProperty("target", out var name) && name.GetString() == target)
                return message.GetProperty("arguments")[0].Clone();
        }
    }
    public async Task<bool> ClosedByServerAsync(TimeSpan timeout)
    {
        try
        {
            while (true) await ReadAsync(timeout);
        }
        catch (InvalidOperationException error) when (error.Message == "Hub closed the test connection.") { return true; }
        catch (OperationCanceledException) { return false; } // a local timeout is NOT server revocation
    }
    private async Task<JsonElement> ReadAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (_messages.Count == 0)
        {
            var buffer = new byte[8192];
            var result = await _socket.ReceiveAsync(buffer, cancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Hub closed the test connection.");
            _pending += Encoding.UTF8.GetString(buffer, 0, result.Count);
            var frames = _pending.Split('\u001e');
            _pending = frames[^1];
            foreach (var frame in frames[..^1].Where(f => f.Length > 0))
            {
                using var json = JsonDocument.Parse(frame);
                _messages.Enqueue(json.RootElement.Clone());
            }
        }
        return _messages.Dequeue();
    }
    public ValueTask DisposeAsync() { _socket.Dispose(); return ValueTask.CompletedTask; }
}
