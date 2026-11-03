using System.Net;

namespace MglConnect.Web.Operations;

public static class ContainerHealthCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        // No application boot, keys, initialization or database writes in the probe process.
        var value = args.Length == 2 ? args[1] : "http://127.0.0.1:8080/health/ready";
        if (args.Length > 2 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "http" || !IPAddress.TryParse(uri.Host, out var ip) || !IPAddress.IsLoopback(ip)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/health/ready") return 1;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };
        try { using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead); return response.StatusCode == HttpStatusCode.OK ? 0 : 1; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { return 1; }
    }
}
