using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MglConnect.Web.Controllers;
using MglConnect.Web.Data;
using MglConnect.Web.Operations;
using Xunit;

namespace MglConnect.Tests;

public sealed class HostingInfrastructureTests
{
    [Fact]
    public async Task MinimalHealthIsAnonymousNoStoreAndDoesNotExposeAccountsOrConnectionDetails()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var client=f.Anonymous();
        foreach(var path in new[]{"/health/live","/health/ready"})
        {
            using var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
            var body=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Single(body.EnumerateObject());Assert.DoesNotContain("Sqlite",body.ToString());Assert.DoesNotContain("Tenant",body.ToString());
        }
        Assert.Equal(0,await ContainerHealthCommand.RunAsync(["--healthcheck",new Uri(f.Address,"/health/ready").ToString()]));
        using var privateHealth=await client.GetAsync("/operacional/saude");Assert.Equal(HttpStatusCode.Redirect,privateHealth.StatusCode);
    }
    [Fact]
    public async Task ReadinessFailureIs503WithoutCreatingDatabaseOrReturningSensitiveErrors()
    {
        var path=Path.Combine(Directory.CreateTempSubdirectory("mgl-8a-unavailable-").FullName,"absent.sqlite");
        await using var db=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite($"Data Source={path};Mode=ReadOnly").Options);
        var result=Assert.IsType<ObjectResult>(await new ContainerHealthController(db).Ready(default));Assert.Equal(503,result.StatusCode);Assert.False(File.Exists(path));
        Assert.Equal("{\"status\":\"unavailable\"}",JsonSerializer.Serialize(result.Value));
    }
    [Theory][InlineData("https://example.com/health/ready")][InlineData("http://example.com/health/ready")][InlineData("http://127.0.0.1/Account/Login")][InlineData("http://user:secret@127.0.0.1/health/ready")][InlineData("http://127.0.0.1/health/ready?token=secret")]
    public async Task HealthCommandCannotCallExternalHostsSensitivePathsOrCredentialUrls(string url)
    {Assert.Equal(1,await ContainerHealthCommand.RunAsync(["--healthcheck",url]));}
    [Fact]
    public void MountedSecretsAreExplicitAndTakePrecedenceWithoutNeedingEnvironmentPasswords()
    {
        var path=Directory.CreateTempSubdirectory("mgl-8a-secrets-").FullName;File.WriteAllText(Path.Combine(path,"DemoData__AdminPassword"),"synthetic-only-not-a-real-secret");
        var config=new ConfigurationManager();config.AddInMemoryCollection(new Dictionary<string,string?>{["Hosting:SecretsPath"]=path,["DemoData:AdminPassword"]="old-synthetic-value"});
        HostingConfiguration.AddSecretFiles(config);Assert.Equal("synthetic-only-not-a-real-secret",config["DemoData:AdminPassword"]);
        var native=new ConfigurationManager();HostingConfiguration.AddSecretFiles(native);Assert.Null(native["DemoData:AdminPassword"]);
    }
    [Theory][InlineData("relative/secrets")][InlineData("/missing/mgl-private-config")]
    public void ExplicitSecretDirectoryMustExistAndBeAbsolute(string path)
    {var config=new ConfigurationManager();config["Hosting:SecretsPath"]=path;Assert.Throws<InvalidOperationException>(()=>HostingConfiguration.AddSecretFiles(config));}
    [Theory][InlineData(null)][InlineData("*")][InlineData("proxy.example.com")][InlineData("10.0.0.0/8")]
    public void ProxyTrustRequiresExplicitIpAddresses(string? address)
    {
        var config=new ConfigurationManager();if(address!=null)config["ReverseProxy:KnownProxies:0"]=address;
        Assert.Throws<InvalidOperationException>(()=>HostingConfiguration.ProxyOptions(config));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task ForwardedSchemeIsAcceptedOnlyFromTheConfiguredProxy(bool trusted)
    {
        var config=new ConfigurationManager();config["ReverseProxy:KnownProxies:0"]="10.0.0.5";
        var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse(trusted?"10.0.0.5":"203.0.113.2");context.Request.Scheme="http";
        context.Request.Headers["X-Forwarded-For"]="198.51.100.1";context.Request.Headers["X-Forwarded-Proto"]="https";
        var middleware=new ForwardedHeadersMiddleware(_=>Task.CompletedTask,NullLoggerFactory.Instance,Options.Create(HostingConfiguration.ProxyOptions(config)));
        await middleware.Invoke(context);Assert.Equal(trusted?"https":"http",context.Request.Scheme);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task HttpHostingRequiresHttpsAndIssuesSecureCookiesOnlyWithTheTrustedForwardedScheme(bool trusted)
    {
        var settings=new Dictionary<string,string>{["ReverseProxy__Enabled"]="true",["ReverseProxy__KnownProxies__0"]=trusted?"127.0.0.1":"192.0.2.10",["Hosting__RequireHttps"]="true"};
        await using var f=await SecurityHttpFixture.CreateAsync(hostingConfiguration:settings);using var client=f.Anonymous();
        using(var health=await client.GetAsync("/health/ready"))Assert.Equal(HttpStatusCode.OK,health.StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For","198.51.100.1");client.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");
        using var response=await client.PostAsJsonAsync("/api/auth/login",new{Login="driver-a",Senha=SecurityHttpFixture.Password});
        Assert.Equal(trusted?HttpStatusCode.OK:HttpStatusCode.PermanentRedirect,response.StatusCode);
        if(trusted)Assert.All(response.Headers.GetValues("Set-Cookie"),cookie=>Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase));
    }
}
