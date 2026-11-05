using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MglConnect.Web.Services.RouteTrust;

namespace MglConnect.Web.Services;

// Generic driving adapter extracted from TruckRoutingService; never certifies HGV restrictions.
public sealed class OsrmRoadRouter(IHttpClientFactory clients,IConfiguration configuration,IMemoryCache? cache=null,IWebHostEnvironment? environment=null)
{
    public bool HistoricalCaptureEnabled => environment?.IsDevelopment()==true && configuration.GetValue<bool>("DemoData:Enabled") && !string.IsNullOrWhiteSpace(configuration["Routing:HistoricalGeometryPath"]);
    private static readonly SemaphoreSlim PublicGate=new(1,1);
    private static DateTimeOffset nextPublicRequest;
    private static ProviderRouteReply Failed(ProviderFailure code)=>new(){Provider="OSRM",Profile="driving",GeometrySource="Unavailable",Failure=code,ReceivedAtUtc=DateTime.UtcNow};
    public static bool TryLocations(string? text,out List<RouteLocation> points)
    {
        points=[];if(string.IsNullOrWhiteSpace(text)||text.Length>1000)return false;
        foreach(var pair in text.Split(';')){
            var parts=pair.Trim().Split(',');
            if(parts.Length!=2||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var lon)
                ||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var lat)||!new RouteLocation(lat,lon).Valid)return false;
            points.Add(new(lat,lon));
        }
        return points.Count is >=2 and <=10 && points.Distinct().Count()>1;
    }
    public async Task<ProviderRouteReply> RouteAsync(IReadOnlyList<RouteLocation> points,CancellationToken ct=default)
    {
        if(points.Count is <2 or >10||points.Any(p=>!p.Valid)||points.Distinct().Count()<2)return Failed(ProviderFailure.MissingLocations);
        var capture=configuration["Routing:HistoricalGeometryPath"];
        if(!string.IsNullOrEmpty(capture))return await ReadHistoricalAsync(capture,points,ct);
        if(!Uri.TryCreate(configuration["Routing:OsrmBaseUrl"],UriKind.Absolute,out var uri))return Failed(ProviderFailure.Disabled);
        if(!(uri.Scheme=="https"||uri.Scheme=="http"&&uri.IsLoopback&&environment?.IsDevelopment()==true)
            ||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return Failed(ProviderFailure.InvalidConfiguration);
        var backoff="osrm-backoff-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        if(cache?.TryGetValue<DateTimeOffset>(backoff,out var until)==true&&until>DateTimeOffset.UtcNow)return Failed(ProviderFailure.RateLimited);
        bool publicDemo=uri.Host is "router.project-osrm.org" or "routing.openstreetmap.de";
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>("Routing:TimeoutSeconds")??15,1,30)));
        bool locked=false;
        try {
            if(publicDemo){await PublicGate.WaitAsync(deadline.Token);locked=true;var delay=nextPublicRequest-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,deadline.Token);nextPublicRequest=DateTimeOffset.UtcNow.AddSeconds(1);}
            var radiuses=string.Join(';',points.Select(_=>"100"));
            var coordinates=string.Join(';',points.Select(p=>FormattableString.Invariant($"{p.Longitude:F6},{p.Latitude:F6}")));
            using var request=new HttpRequestMessage(HttpMethod.Get,uri.AbsoluteUri.TrimEnd('/')+"/route/v1/driving/"+coordinates+"?overview=full&geometries=geojson&steps=true&alternatives=true&radiuses="+radiuses);
            request.Headers.UserAgent.ParseAdd("MglConnect-RoutePrototype/3.0");
            using var response=await clients.CreateClient("OsrmRoad").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(response.StatusCode==HttpStatusCode.TooManyRequests){var requestedWait=response.Headers.RetryAfter?.Delta??response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow??TimeSpan.FromSeconds(30);var wait=TimeSpan.FromSeconds(Math.Clamp(requestedWait.TotalSeconds,1,3600));cache?.Set(backoff,DateTimeOffset.UtcNow+wait,wait);return Failed(ProviderFailure.RateLimited);}
            if(!response.IsSuccessStatusCode && response.StatusCode!=HttpStatusCode.BadRequest)return Failed(ProviderFailure.HttpError);
            if(response.Content.Headers.ContentLength>2*1024*1024)return Failed(ProviderFailure.InvalidResponse);
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
            while((read=await stream.ReadAsync(buffer,deadline.Token))>0){if(bytes.Length+read>2*1024*1024)return Failed(ProviderFailure.InvalidResponse);bytes.Write(buffer,0,read);}
            var result=Parse(bytes.ToArray());if(!response.IsSuccessStatusCode && result.Failure!=ProviderFailure.NoCoverage)return Failed(ProviderFailure.HttpError);result.SourceReference=uri.GetLeftPart(UriPartial.Authority)+"/route/v1/driving";if(result.Failure==ProviderFailure.None)result.GeometrySource=uri.IsLoopback && !configuration.GetValue<bool>("Routing:LocalRoadEngine")?"LocalProviderFixture":"RoadNetwork";
            result.EngineVersion=configuration["Routing:OsrmEngineVersion"];
            result.DatasetReference=configuration["Routing:DatasetReference"];
            result.GraphVersion??=configuration["Routing:DatasetVersion"];
            if(result.Failure==ProviderFailure.None && (result.Waypoints.Count>0&&result.Waypoints.Count!=points.Count
                || ControlledRouteDemo.Distance((points[0].Latitude,points[0].Longitude),(result.Geometry[0].Latitude,result.Geometry[0].Longitude))>110
                || ControlledRouteDemo.Distance((points[^1].Latitude,points[^1].Longitude),(result.Geometry[^1].Latitude,result.Geometry[^1].Longitude))>110))return Failed(ProviderFailure.InvalidResponse);
            // A response does not disclose the OSM graph revision or restriction coverage.
            return result;
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return Failed(ProviderFailure.Timeout);}
        catch(HttpRequestException){return Failed(ProviderFailure.Network);}
        catch(IOException){return Failed(ProviderFailure.Network);}
        finally{if(locked)PublicGate.Release();}
    }
    public static ProviderRouteReply Parse(byte[] bytes)
    {
        try {
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=32});var root=document.RootElement;
            var code=root.GetProperty("code").GetString();if(code is "NoRoute" or "NoSegment")return Failed(ProviderFailure.NoCoverage);if(code!="Ok")return Failed(ProviderFailure.InvalidResponse);
            var routes=root.GetProperty("routes");if(routes.GetArrayLength()==0)return Failed(ProviderFailure.NoCoverage);
            var route=routes[0];var distance=route.GetProperty("distance").GetDouble();var seconds=route.GetProperty("duration").GetDouble();
            if(!double.IsFinite(distance)||distance<=0||distance>50000000||!double.IsFinite(seconds)||seconds<=0||seconds>2592000)throw new JsonException();
            var shape=route.GetProperty("geometry");if(shape.GetProperty("type").GetString()!="LineString")throw new JsonException();
            var points=ReadGeometry(shape.GetProperty("coordinates"));
            var result=new ProviderRouteReply{Provider="OSRM",Profile="driving",GraphVersion=root.TryGetProperty("data_version",out var version)?version.GetString():null,Geometry=points,DistanceKm=(decimal)distance/1000,
                DurationSeconds=seconds,EstimatedMinutes=(int)Math.Ceiling(seconds/60),ReceivedAtUtc=DateTime.UtcNow,ResponseSha256=Convert.ToHexString(SHA256.HashData(bytes)),GeometrySource="RoadNetwork"};
            if(route.TryGetProperty("legs",out var legs))foreach(var leg in legs.EnumerateArray())if(leg.TryGetProperty("steps",out var steps))foreach(var step in steps.EnumerateArray()){
                var maneuver=step.GetProperty("maneuver");var location=maneuver.GetProperty("location");var point=new RouteLocation(location[1].GetDouble(),location[0].GetDouble());if(!point.Valid)throw new JsonException();
                result.Steps.Add(new(){Latitude=point.Latitude,Longitude=point.Longitude,NomeVia=step.TryGetProperty("name",out var name)?name.GetString()??"Via sem nome":"Via sem nome",
                    DistanciaMetros=step.TryGetProperty("distance",out var stepDistance)?NonNegative(stepDistance):0,
                    DuracaoSegundos=step.TryGetProperty("duration",out var stepDuration)?NonNegative(stepDuration):0,
                    ManobraModificador=maneuver.TryGetProperty("modifier",out var modifier)?modifier.GetString()??"straight":"straight",
                    ManobraTipo=maneuver.GetProperty("type").GetString()??"turn",Instrucao="Consulte a manobra do percurso genérico",Ordem=result.Steps.Count});
                if(result.Steps.Count>20000)throw new JsonException();
            }
            if(root.TryGetProperty("waypoints",out var waypoints))foreach(var waypoint in waypoints.EnumerateArray()) {
                if(result.Waypoints.Count>=10)throw new JsonException();
                var location=waypoint.GetProperty("location");if(location.GetArrayLength()!=2)throw new JsonException();
                var p=new RouteLocation(location[1].GetDouble(),location[0].GetDouble());if(!p.Valid)throw new JsonException();
                result.Waypoints.Add(new(){Latitude=p.Latitude,Longitude=p.Longitude,Nome=waypoint.TryGetProperty("name",out var n)?n.GetString():null});
            }
            if(routes.GetArrayLength()>4)throw new JsonException();
            foreach(var alternative in routes.EnumerateArray().Skip(1)) {
                var d=NonNegative(alternative.GetProperty("distance"));var t=NonNegative(alternative.GetProperty("duration"));
                if(d<=0||d>50000000||t<=0||t>2592000||alternative.GetProperty("geometry").GetProperty("type").GetString()!="LineString")throw new JsonException();
                result.Alternatives.Add(new(ReadGeometry(alternative.GetProperty("geometry").GetProperty("coordinates")),(decimal)d/1000,t));
            }
            return result;
        }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException or OverflowException){return Failed(ProviderFailure.InvalidResponse);}
    }
    private static double NonNegative(JsonElement value) {
        var number=value.GetDouble();if(!double.IsFinite(number)||number<0)throw new JsonException();return number;
    }
    private static List<RouteCoordinate> ReadGeometry(JsonElement coordinates)
    {
        if(coordinates.GetArrayLength() is <2 or >20000)throw new JsonException();
        var result=new List<RouteCoordinate>();
        foreach(var pair in coordinates.EnumerateArray()){
            if(pair.GetArrayLength()!=2)throw new JsonException();var p=new RouteLocation(pair[1].GetDouble(),pair[0].GetDouble());if(!p.Valid)throw new JsonException();
            result.Add(new(){Latitude=p.Latitude,Longitude=p.Longitude});
        }
        if(result.All(p=>p.Latitude==result[0].Latitude&&p.Longitude==result[0].Longitude))throw new JsonException();return result;
    }
    private async Task<ProviderRouteReply> ReadHistoricalAsync(string path,IReadOnlyList<RouteLocation> requested,CancellationToken ct)
    {
        if(environment?.IsDevelopment()!=true||!configuration.GetValue<bool>("DemoData:Enabled")||!Path.IsPathFullyQualified(path))return Failed(ProviderFailure.InvalidConfiguration);
        try {
            if(new FileInfo(path).Length>2*1024*1024)return Failed(ProviderFailure.InvalidResponse);
            var bytes=await File.ReadAllBytesAsync(path,ct);using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
            // Existing historical fixture contains synthetic restriction evidence. Read
            // ONLY its captured road geometry/distance; never reuse those restrictions.
            if(root.GetProperty("Provider").GetString()?.StartsWith("OSRM — geometria gravada")!=true)return Failed(ProviderFailure.InvalidResponse);
            if(!Uri.TryCreate(root.GetProperty("SourceReference").GetString(),UriKind.Absolute,out var source)||source.Scheme!="https"||source.Host!="router.project-osrm.org")return Failed(ProviderFailure.InvalidResponse);
            var plan=root.GetProperty("RecordedPlan");var geometry=new List<RouteCoordinate>();
            foreach(var point in plan.GetProperty("TrajetoCoordenadas").EnumerateArray())geometry.Add(new(){Latitude=point.GetProperty("Latitude").GetDouble(),Longitude=point.GetProperty("Longitude").GetDouble()});
            var shape=JsonSerializer.SerializeToElement(geometry.Select(p=>new[]{p.Longitude,p.Latitude}));geometry=ReadGeometry(shape);
            if(requested.Count!=2||ControlledRouteDemo.Distance((requested[0].Latitude,requested[0].Longitude),(geometry[0].Latitude,geometry[0].Longitude))>100
                ||ControlledRouteDemo.Distance((requested[^1].Latitude,requested[^1].Longitude),(geometry[^1].Latitude,geometry[^1].Longitude))>100)return Failed(ProviderFailure.NoCoverage);
            if(plan.GetProperty("DistanciaKm").GetDecimal() is <=0 or >50000)return Failed(ProviderFailure.InvalidResponse);
            return new(){SourceReference=source.AbsoluteUri,Provider="OSRM · captura histórica",Profile="driving",GeometrySource="HistoricalRoadCapture",Geometry=geometry,DistanceKm=plan.GetProperty("DistanciaKm").GetDecimal(),
                DurationSeconds=null,EstimatedMinutes=null,ReceivedAtUtc=DateTime.UtcNow,ResponseSha256=Convert.ToHexString(SHA256.HashData(bytes)),Detail="Geometria histórica de marcos públicos. Duração original e versão do grafo não preservadas; sem consulta ao vivo ou restrições verificadas."};
        }catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException){return Failed(ProviderFailure.InvalidResponse);}
    }
}
