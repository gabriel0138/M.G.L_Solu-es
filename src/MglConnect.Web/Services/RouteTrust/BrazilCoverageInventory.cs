using System.Text.Json;

namespace MglConnect.Web.Services.RouteTrust;

public sealed record InventoryCount(RestrictionKind Kind, int TaggedWays, int UnknownWays);
public sealed record CoverageInventory(string Territory, string Source, string CapturedAtUtc, string ResponseSha256,
    int Ways, int Bridges, int Tunnels, IReadOnlyList<InventoryCount> Counts);
public static class BrazilCoverageInventory
{
    public static CoverageInventory Read()
    {
        using var stream = typeof(BrazilCoverageInventory).Assembly.GetManifestResourceStream("MglConnect.BrazilPilotOsm")!;
        using var json = JsonDocument.Parse(stream);
        return Analyze(json.RootElement);
    }
    public static CoverageInventory Analyze(JsonElement data)
    {
        var ways = data.GetProperty("ways").EnumerateArray().ToArray();
        string[] Keys(RestrictionKind kind) => kind switch
        {
            RestrictionKind.Height => ["maxheight", "maxheight:physical"], RestrictionKind.Width => ["maxwidth"],
            RestrictionKind.Length => ["maxlength"], RestrictionKind.GrossWeight => ["maxweight"],
            RestrictionKind.AxleLoad => ["maxaxleload"], RestrictionKind.AxleCount => ["maxaxles"],
            RestrictionKind.Category => ["hgv", "goods"], RestrictionKind.Access => ["access", "motor_vehicle", "vehicle", "hgv"],
            RestrictionKind.Hazardous => ["hazmat"], _ => []
        };
        var motorKeys = Enum.GetValues<RestrictionKind>().SelectMany(Keys).ToHashSet(StringComparer.Ordinal);
        bool Has(JsonElement way, RestrictionKind kind) => way.GetProperty("tags").EnumerateObject()
            .Any(t => !string.IsNullOrWhiteSpace(t.Value.GetString()) && (kind == RestrictionKind.Time
                ? t.Name.EndsWith(":conditional", StringComparison.Ordinal) && motorKeys.Contains(t.Name[..^12])
                : Keys(kind).Any(k => t.Name == k || t.Name == k + ":conditional")));
        return new(data.GetProperty("territory").GetString()!, data.GetProperty("source").GetString()!, data.GetProperty("capturedAtUtc").GetString()!, data.GetProperty("responseSha256").GetString()!,
            ways.Length, ways.Count(w => w.GetProperty("tags").TryGetProperty("bridge", out var b) && b.GetString() != "no"),
            ways.Count(w => w.GetProperty("tags").TryGetProperty("tunnel", out var t) && t.GetString() != "no"),
            Enum.GetValues<RestrictionKind>().Select(k => new InventoryCount(k, ways.Count(w => Has(w, k)), ways.Count(w => !Has(w, k)))).ToArray());
    }
}
