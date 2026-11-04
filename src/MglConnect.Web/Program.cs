using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Middleware;
using MglConnect.Web.Services;
using QuestPDF.Infrastructure;

if (args.FirstOrDefault() == "--healthcheck")
{
    Environment.ExitCode = await MglConnect.Web.Operations.ContainerHealthCommand.RunAsync(args);
    return;
}
if (args.FirstOrDefault() == "--operations")
{
    Environment.ExitCode = await MglConnect.Web.Operations.RecoveryCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
MglConnect.Web.Operations.HostingConfiguration.AddSecretFiles(builder.Configuration);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
var proxyEnabled = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
var proxyOptions = proxyEnabled ? MglConnect.Web.Operations.HostingConfiguration.ProxyOptions(builder.Configuration) : null;
var requireHttps = builder.Configuration.GetValue<bool>("Hosting:RequireHttps");
if (requireHttps) builder.Services.AddHttpsRedirection(options => { options.HttpsPort = 443; options.RedirectStatusCode = 308; });

QuestPDF.Settings.License = LicenseType.Community;

var postgresUrl = Environment.GetEnvironmentVariable("POSTGRES_URL")
               ?? Environment.GetEnvironmentVariable("DATABASE_URL")
               ?? Environment.GetEnvironmentVariable("POSTGRES_PRISMA_URL")
               ?? builder.Configuration["POSTGRES_URL"];
var dbProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=mglconnect.db";
var usesPostgres = !string.IsNullOrWhiteSpace(postgresUrl)
    || dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
    || dbProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase);
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VERCEL")) && !usesPostgres)
{
    throw new InvalidOperationException(
        "Vercel containers have ephemeral storage. Configure DATABASE_URL or POSTGRES_URL for PostgreSQL.");
}

var seedDemoData = builder.Configuration.GetValue<bool?>("DemoData:Enabled")
    ?? builder.Environment.IsDevelopment();
DemoSeedCredentials? demoSeedCredentials = null;
if (seedDemoData)
{
    string GetDemoPassword(string setting, string developmentDefault)
    {
        var configuredPassword = builder.Configuration[setting];
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            return configuredPassword;
        }

        if (builder.Environment.IsDevelopment())
        {
            return developmentDefault;
        }

        throw new InvalidOperationException(
            $"Set '{setting}' to a strong secret before enabling demo data outside Development.");
    }

    demoSeedCredentials = new DemoSeedCredentials(
        GetDemoPassword("DemoData:AdminPassword", "admin123"),
        GetDemoPassword("DemoData:DriverPassword", "motorista123"),
        GetDemoPassword("DemoData:ClientPassword", "cliente123"));
}

builder.Services.AddDbContext<MglDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(postgresUrl))
    {
        options.UseNpgsql(PostgresConnectionStringParser.Parse(postgresUrl));
    }
    else if (dbProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
             || dbProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(PostgresConnectionStringParser.Parse(connectionString));
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

var port = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(port, out var portNumber) && portNumber is > 0 and <= 65535)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{portNumber}");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("ValhallaExperimental").ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
builder.Services.AddScoped<MglConnect.Web.Services.RouteTrust.IExperimentalTruckRouter,MglConnect.Web.Services.RouteTrust.ValhallaExperimentalRouter>();
// Factory URL logs may contain geocoding addresses or provider API keys.
builder.Services.ConfigureHttpClientDefaults(client => client.RemoveAllLoggers());
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MglConnect");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
else if (usesPostgres)
{
    dataProtection.PersistKeysToDbContext<MglDbContext>();
}

builder.Services.AddScoped<IPasswordHasher, SimplePasswordHasher>();
builder.Services.AddScoped<ITrackingService, TrackingService>();
builder.Services.AddSingleton<PublicTrackingRateLimiter>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDriverShiftService, DriverShiftService>();
builder.Services.AddScoped<IEmergencyAlertService, EmergencyAlertService>();
builder.Services.AddSingleton<IRouteDeviationEngine, RouteDeviationEngine>();
builder.Services.AddSingleton<TelemetrySubscriptions>();
builder.Services.AddScoped<DeliveryPositionRecorder>();
builder.Services.Configure<GpsHistoryOptions>(builder.Configuration.GetSection("GpsHistory"));
builder.Services.AddScoped<GpsIngestionService>();
builder.Services.AddScoped<GpsArchiveService>();
builder.Services.AddScoped<GpsHistoryReader>();
builder.Services.AddScoped<GpsHistoryAccess>();
builder.Services.AddScoped<GpsTelemetryPublisher>();
builder.Services.AddSingleton<MglConnect.Web.Services.RouteTrust.RestrictionEngine>();
builder.Services.AddScoped<MglConnect.Web.Services.RouteTrust.IRouteEvidenceSource,MglConnect.Web.Services.RouteTrust.ConfiguredRouteEvidenceSource>();
builder.Services.AddScoped<MglConnect.Web.Services.RouteTrust.RouteTrustService>();
builder.Services.AddScoped<ControlledRouteDemo>();
builder.Services.AddScoped<ControlledDemoNavigation>();
builder.Services.AddScoped<ITruckRoutingService, TruckRoutingService>();
builder.Services.AddScoped<IFuelCostService, FuelCostService>();
builder.Services.AddScoped<ITrafficMatchService, TrafficMatchService>();
builder.Services.AddScoped<IAnttFreteService, AnttFreteService>();
builder.Services.AddScoped<ICo2EsgService, Co2EsgService>();
builder.Services.AddScoped<IPodPdfService, PodPdfService>();
builder.Services.AddScoped<ITenantProvider, HttpContextTenantProvider>();

builder.Services.AddSignalR();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = MglConnectApiBearerDefaults.SmartScheme;
        options.DefaultChallengeScheme = MglConnectApiBearerDefaults.SmartScheme;
        options.DefaultForbidScheme = MglConnectApiBearerDefaults.SmartScheme;
    })
    .AddPolicyScheme(MglConnectApiBearerDefaults.SmartScheme, "Cookie ou bearer token", options =>
    {
        options.ForwardDefaultSelector = MglConnectApiBearerDefaults.SelectScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = MglConnectApiBearerDefaults.CookieName;
        options.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiBearerAuthenticationHandler>(
        MglConnectApiBearerDefaults.Scheme, _ => { });

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "M.G.L Connect Logistics API",
        Version = "v1",
        Description = "API REST corporativa para aplicativo móvel, comprovantes digitais e rastreamento em tempo real.",
        Contact = new OpenApiContact
        {
            Name = "M.G.L Logística Integrada S.A.",
            Email = "operacoes@example.invalid"
        }
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "protected ticket",
        In = ParameterLocation.Header,
        Description = "Informe o token retornado por POST /api/auth/login."
    });
});

builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("route-assess",context=>RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier)??"anonymous",_=>new FixedWindowRateLimiterOptions{PermitLimit=8,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
    options.AddPolicy("gps-export", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 4, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
var app = builder.Build();
if (proxyOptions != null) app.UseForwardedHeaders(proxyOptions);
if (requireHttps) app.UseWhen(context => context.Request.Path != "/health/live" && context.Request.Path != "/health/ready", branch => branch.UseHttpsRedirection());

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MglDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DbInitializer.InitializeAsync(context, hasher, demoSeedCredentials);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseStatusCodePagesWithReExecute("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<OperationalDiagnosticsMiddleware>();
app.UseMiddleware<PublicTrackingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ActiveAccountMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<AdminOnlySwaggerMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "M.G.L Connect API v1");
    options.RoutePrefix = "swagger";
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<TrackingHub>("/trackingHub");
app.MapHub<TelemetryHub>("/telemetryHub", options => options.CloseOnAuthenticationExpiration = true);

app.Run();
