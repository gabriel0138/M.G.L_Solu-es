using System.Net;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace MglConnect.Web.Services.RouteTrust;

public sealed class ValhallaExperimentalRouter(IHttpClientFactory clients, IConfiguration configuration, IMemoryCache? cache=null) : IExperimentalTruckRouter
{
    public bool Enabled => configuration.GetValue<bool>("Routing:Valhalla:Enabled");
    private static ProviderRouteReply Failed(ProviderFailure failure, string detail) => new() { Failure = failure, Detail = detail, ReceivedAtUtc = DateTime.UtcNow };
    public async Task<ProviderRouteReply> RouteAsync(ProviderRouteRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return Failed(ProviderFailure.Disabled, "Adaptador desativado por padrão.");
        if (!Uri.TryCreate(configuration["Routing:Valhalla:BaseUrl"], UriKind.Absolute, out var uri)
            || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return Failed(ProviderFailure.InvalidConfiguration, "Configure explicitamente uma instância autorizada HTTPS ou HTTP loopback.");
        if (!request.Origin.Valid || !request.Destination.Valid) return Failed(ProviderFailure.MissingLocations, "Coordenadas inválidas.");
        var v = request.Vehicle;
        if (v.HeightMetres <= 0 || v.WidthMetres <= 0 || v.LengthMetres <= 0 || v.GrossTonnes <= 0 || v.MaximumAxleTonnes <= 0
            || v.MaximumAxleTonnes > v.GrossTonnes || v.Axles is < 2 or > 20
            || v.HeightMetres > 10 || v.WidthMetres > 10 || v.LengthMetres > 50 || v.GrossTonnes > 100 || v.MaximumAxleTonnes > 40)
            return Failed(ProviderFailure.MissingVehicleData, "Perfil ausente ou fora do domínio técnico do adaptador; nenhum valor será substituído por defaults.");
        var backoffKey="valhalla-backoff-"+RouteFingerprint.Hash(uri.AbsoluteUri);
        if(cache!=null&&cache.TryGetValue<DateTimeOffset>(backoffKey,out var next)&&next>DateTimeOffset.UtcNow)
        {
            var waiting=Failed(ProviderFailure.RateLimited,"Aguardando janela de repetição indicada pelo fornecedor; nenhuma nova consulta foi enviada.");
            waiting.RetryAfterSeconds=(int)Math.Ceiling((next-DateTimeOffset.UtcNow).TotalSeconds);return waiting;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var payload = new
            {
                locations = new[] { new { lat = request.Origin.Latitude, lon = request.Origin.Longitude }, new { lat = request.Destination.Latitude, lon = request.Destination.Longitude } },
                costing = "truck", units = "kilometers", directions_options = new { units = "kilometers", language = "pt-BR" },
                costing_options = new { truck = new { height = v.HeightMetres, width = v.WidthMetres, length = v.LengthMetres,
                    weight = v.GrossTonnes, axle_load = v.MaximumAxleTonnes, axle_count = v.Axles, hazmat = v.Hazardous,
                    hgv_no_access_penalty = 43200 } }
            };
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(uri.AbsoluteUri.TrimEnd('/') + "/route")) { Content = JsonContent.Create(payload) };
            using var response = await clients.CreateClient("ValhallaExperimental").SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var failed = Failed(ProviderFailure.RateLimited, "Fornecedor limitou requisições; sem repetição automática ou fallback que libere orientação.");
                var retry = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow;
                failed.RetryAfterSeconds = retry.HasValue ? (int)Math.Clamp(retry.Value.TotalSeconds, 1, 3600) : null;
                var delay=TimeSpan.FromSeconds(failed.RetryAfterSeconds??30);
                cache?.Set(backoffKey,DateTimeOffset.UtcNow+delay,delay);
                return failed;
            }
            if (!response.IsSuccessStatusCode && response.StatusCode!=HttpStatusCode.BadRequest) return Failed(ProviderFailure.HttpError, "Resposta HTTP " + (int)response.StatusCode);
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) return Failed(ProviderFailure.InvalidResponse, "Resposta acima do limite de 2 MiB.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (bytes.Length + read > 2 * 1024 * 1024) return Failed(ProviderFailure.InvalidResponse, "Resposta acima do limite de 2 MiB.");
                bytes.Write(buffer, 0, read);
            }
            var reply = Parse(bytes.ToArray());
            if(!response.IsSuccessStatusCode&&reply.Failure!=ProviderFailure.NoCoverage)return Failed(ProviderFailure.HttpError,"Resposta HTTP "+(int)response.StatusCode);
            reply.PhysicalRequest = v; return reply;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failed(ProviderFailure.Timeout, "Tempo limite do fornecedor."); }
        catch (HttpRequestException) { return Failed(ProviderFailure.Network, "Comunicação com o fornecedor indisponível."); }
        catch (IOException) { return Failed(ProviderFailure.Network, "Resposta interrompida; nenhum resultado parcial foi aceito."); }
    }
    public static ProviderRouteReply Parse(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if(root.TryGetProperty("warnings",out var warnings)&&warnings.GetArrayLength()>0)
                return Failed(ProviderFailure.InvalidResponse,"Fornecedor retornou avisos; parâmetros/rota requerem verificação antes de aceitar a geometria.");
            if (root.TryGetProperty("error_code", out var code))
                return Failed(code.GetInt32() is 171 or 442 ? ProviderFailure.NoCoverage : ProviderFailure.InvalidResponse, "Fornecedor não retornou rota utilizável.");
            var trip = root.GetProperty("trip");
            if (trip.GetProperty("status").GetInt32() != 0 || trip.GetProperty("units").GetString() != "kilometers") throw new JsonException();
            if(trip.TryGetProperty("warnings",out var tripWarnings)&&tripWarnings.GetArrayLength()>0)throw new JsonException();
            var legs = trip.GetProperty("legs");
            if (legs.GetArrayLength() is < 1 or > 20) throw new JsonException();
            var points = new List<RouteCoordinate>();
            foreach (var leg in legs.EnumerateArray())
            {
                var shape = Decode(leg.GetProperty("shape").GetString() ?? "");
                if (shape.Count < 2 || points.Count + shape.Count > 20000) throw new JsonException();
                if (points.Count > 0)
                {
                    if (points[^1].Latitude != shape[0].Latitude || points[^1].Longitude != shape[0].Longitude) throw new JsonException();
                    shape.RemoveAt(0);
                }
                points.AddRange(shape);
            }
            if (points.All(p => p.Latitude == points[0].Latitude && p.Longitude == points[0].Longitude)) throw new JsonException();
            var summary = trip.GetProperty("summary"); var distance = summary.GetProperty("length").GetDecimal(); var seconds = summary.GetProperty("time").GetDouble();
            if (distance <= 0 || distance > 50000 || !double.IsFinite(seconds) || seconds < 0 || seconds > 60 * 60 * 24 * 30) throw new JsonException();
            return new() { Geometry = points, DistanceKm = distance, DurationSeconds = seconds, EstimatedMinutes = (int)Math.Ceiling(seconds / 60),
                ResponseSha256 = Convert.ToHexString(SHA256.HashData(body)), ReceivedAtUtc = DateTime.UtcNow,
                Detail = "Sucesso técnico. /route não comprova versão do grafo, IDs direcionais das vias nem cobertura de restrições. Todas as categorias permanecem desconhecidas." };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { return Failed(ProviderFailure.InvalidResponse, "Resposta incompleta ou malformada; sem geometria parcial."); }
    }
    private static List<RouteCoordinate> Decode(string encoded)
    {
        int index = 0; long lat = 0, lon = 0; var result = new List<RouteCoordinate>();
        long Next()
        {
            long value = 0; int shift = 0, b;
            do { if (index >= encoded.Length || shift > 30 || encoded[index] is < '?' or > '~') throw new JsonException(); b = encoded[index++] - 63; value |= (long)(b & 31) << shift; shift += 5; } while (b >= 32);
            return (value & 1) != 0 ? ~(value >> 1) : value >> 1;
        }
        while (index < encoded.Length)
        {
            lat += Next(); lon += Next(); var location = new RouteLocation(lat / 1e6, lon / 1e6);
            if (!location.Valid || result.Count >= 20000) throw new JsonException();
            result.Add(new() { Latitude = location.Latitude, Longitude = location.Longitude });
        }
        return result;
    }
}
