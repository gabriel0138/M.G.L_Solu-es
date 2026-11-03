using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MglConnect.Web.Operations;

public static class HostingConfiguration
{
    public static void AddSecretFiles(ConfigurationManager configuration)
    {
        var path = configuration["Hosting:SecretsPath"];
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
                throw new InvalidOperationException("Hosting:SecretsPath deve apontar para um diretório absoluto existente.");
            // Explicit mounted directory only; __ maps to configuration sections. Values are never logged.
            configuration.AddKeyPerFile(path, optional: false, reloadOnChange: false);
        }
    }

    public static ForwardedHeadersOptions ProxyOptions(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1, RequireHeaderSymmetry = true
        };
        options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
        var addresses = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        if (addresses.Length == 0) throw new InvalidOperationException("ReverseProxy habilitado exige KnownProxies explícitos.");
        foreach (var address in addresses)
        {
            if (!IPAddress.TryParse(address, out var ip)) throw new InvalidOperationException("KnownProxies aceita somente endereços IP explícitos.");
            options.KnownProxies.Add(ip);
        }
        return options;
    }
}
