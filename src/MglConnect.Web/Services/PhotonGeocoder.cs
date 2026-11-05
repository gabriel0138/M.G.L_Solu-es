using System.Net;
using System.Text.Json;
using MglConnect.Web.Services.RouteTrust;

namespace MglConnect.Web.Services;

public sealed record GeocodingSuggestion(string Label,double Latitude,double Longitude,string Precision="Aproximada · confirme no mapa");
public sealed record GeocodingReply(string State,IReadOnlyList<GeocodingSuggestion> Suggestions);

// Shared Photon adapter. A search result is a candidate, never an exact address claim.
public sealed class PhotonGeocoder(IHttpClientFactory clients,IConfiguration configuration,IWebHostEnvironment? environment=null)
{
    private static readonly SemaphoreSlim PublicGate=new(1,1);
    private static DateTimeOffset nextRequest;
    private static DateTimeOffset retryAfter;
    public async Task<GeocodingReply> SearchAsync(string? query,CancellationToken ct=default)
    {
        GeocodingReply Failed(string state)=>new(state,[]);
        if(query==null||query.Trim().Length is <3 or >240)return Failed("InvalidQuery");
        if(!configuration.GetValue<bool>("Geocoding:PhotonEnabled"))return Failed("Disabled");
        if(!Uri.TryCreate(configuration["Geocoding:PhotonBaseUrl"]??"https://photon.komoot.io",UriKind.Absolute,out var uri)
            ||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0
            ||!(uri.Scheme=="https"||uri.Scheme=="http"&&uri.IsLoopback&&environment?.IsDevelopment()==true))return Failed("InvalidConfiguration");
        var publicService=uri.Host.Equals("photon.komoot.io",StringComparison.OrdinalIgnoreCase);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>("Geocoding:TimeoutSeconds")??8,1,15)));
        bool locked=false;
        try {
            if(publicService){await PublicGate.WaitAsync(timeout.Token);locked=true;if(retryAfter>DateTimeOffset.UtcNow)return Failed("RateLimited");var delay=nextRequest-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,timeout.Token);nextRequest=DateTimeOffset.UtcNow.AddSeconds(1);}
            var url=uri.AbsoluteUri.TrimEnd('/')+"/api/?q="+Uri.EscapeDataString(query.Trim())+"&limit=5&bbox=-75,-34,-34,6";
            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("MglConnect-GeographyPrototype/5.0");
            using var response=await clients.CreateClient("Photon").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(response.StatusCode==HttpStatusCode.TooManyRequests){if(publicService)retryAfter=DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds??30,1,3600));return Failed("RateLimited");}
            if(!response.IsSuccessStatusCode)return Failed("Unavailable");
            if(response.Content.Headers.ContentLength>1024*1024)return Failed("InvalidResponse");
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
            while((count=await stream.ReadAsync(chunk,timeout.Token))>0){if(buffer.Length+count>1024*1024)return Failed("InvalidResponse");buffer.Write(chunk,0,count);}
            return Parse(buffer.ToArray());
        } catch(OperationCanceledException) when(!ct.IsCancellationRequested){return Failed("Timeout");}
        catch(HttpRequestException){return Failed("Unavailable");}
        catch(IOException){return Failed("Unavailable");}
        finally{if(locked)PublicGate.Release();}
    }
    public static GeocodingReply Parse(byte[] bytes)
    {
        try {
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=24});var features=doc.RootElement.GetProperty("features");
            if(features.GetArrayLength()>100)return new("InvalidResponse",[]);
            var list=new List<GeocodingSuggestion>();
            foreach(var f in features.EnumerateArray().Take(5)) {
                var geo=f.GetProperty("geometry");var coords=geo.GetProperty("coordinates");if(geo.GetProperty("type").GetString()!="Point"||coords.GetArrayLength()!=2)continue;
                var p=new RouteLocation(coords[1].GetDouble(),coords[0].GetDouble());if(!p.Valid||p.Latitude is <-34 or >6||p.Longitude is <-75 or >-34)continue;
                var props=f.GetProperty("properties");
                var label=string.Join(", ",new[]{"name","street","housenumber","district","city","state","postcode"}.Select(k=>props.TryGetProperty(k,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct());
                if(label.Length is <1 or >600)continue;
                list.Add(new(label,p.Latitude,p.Longitude));
            }
            return new(list.Count>0?"Available":"Empty",list);
        }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or IndexOutOfRangeException){return new("InvalidResponse",[]);}
    }
}
