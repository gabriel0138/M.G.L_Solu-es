namespace MglConnect.Web.Services.RouteTrust;

public enum ProviderFailure { None, Disabled, InvalidConfiguration, MissingVehicleData, MissingLocations, NoCoverage, InvalidResponse, RateLimited, HttpError, Timeout, Network }
public sealed record RouteLocation(double Latitude, double Longitude)
{
    public bool Valid => double.IsFinite(Latitude) && Latitude is >= -90 and <= 90
        && double.IsFinite(Longitude) && Longitude is >= -180 and <= 180;
}
// Explicit units at the boundary. No SDK/provider defaults for unknown physical facts.
public sealed record TruckPhysicalRequest(decimal HeightMetres, decimal WidthMetres, decimal LengthMetres,
    decimal GrossTonnes, decimal MaximumAxleTonnes, int Axles, bool Hazardous)
{
    public static TruckPhysicalRequest? From(OperationalTruckProfile profile)
    {
        if (profile.Validate().Count != 0 || profile.Missing().Count != 0 || profile.AxleLoads.Count == 0) return null;
        return new(profile.Height!.Value, profile.Width!.Value, profile.Length!.Value,
            profile.Gross!.Value / 1000m, profile.AxleLoads.Max(a => a.Value) / 1000m, profile.Axles!.Value, profile.Hazardous!.Value);
    }
}
public sealed record ProviderRouteRequest(RouteLocation Origin, RouteLocation Destination, TruckPhysicalRequest Vehicle);
public sealed record RoadReference(string Provider, string GraphVersion, string SegmentId, string WayId,
    bool? Forward, string TopologyHash, string SpatialCoverageHash);
public sealed record ProviderSegment(RoadReference Reference, int BeginShapeIndex, int EndShapeIndex);
public sealed record RoadRestrictionEvidence(RoadReference Reference, string Source, string SourceVersion,
    DateTimeOffset ValidFrom, DateTimeOffset ValidUntil);
public static class RoadEvidenceAssociation
{
    // Structural correspondence only. This does not establish legal completeness or independent homologation.
    public static RestrictionOutcome Check(RoadReference road, RoadRestrictionEvidence rule, DateTimeOffset at)
    {
        static bool Complete(RoadReference r) => new[] { r.Provider, r.GraphVersion, r.SegmentId, r.WayId, r.TopologyHash, r.SpatialCoverageHash }
            .All(s => !string.IsNullOrWhiteSpace(s)) && r.Forward.HasValue;
        return Complete(road) && Complete(rule.Reference) && road == rule.Reference
            && !string.IsNullOrWhiteSpace(rule.Source) && !string.IsNullOrWhiteSpace(rule.SourceVersion)
            && rule.ValidFrom <= at && at < rule.ValidUntil
            ? RestrictionOutcome.Compativel : RestrictionOutcome.Desconhecido;
    }
}
public sealed class ProviderRouteReply
{
    public string? SourceReference { get; set; }
    public string GeometrySource { get; set; } = "RoadNetwork";
    public double? DurationSeconds { get; set; }
    public List<RouteStep> Steps { get; set; } = [];
    public string Provider { get; set; } = "Valhalla experimental";
    public string Profile { get; set; } = "truck";
    public string? GraphVersion { get; set; }
    public string? ResponseSha256 { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public ProviderFailure Failure { get; set; }
    public string Detail { get; set; } = "";
    public int? RetryAfterSeconds { get; set; }
    public TruckPhysicalRequest? PhysicalRequest { get; set; }
    public List<ProviderSegment> Segments { get; set; } = [];
    public List<RouteCoordinate> Geometry { get; set; } = [];
    public decimal? DistanceKm { get; set; }
    public int? EstimatedMinutes { get; set; }
    public Dictionary<RestrictionKind, RestrictionOutcome> Coverage { get; set; } = Enum.GetValues<RestrictionKind>().ToDictionary(k => k, _ => RestrictionOutcome.Desconhecido);
    public TruckRoutePlan ToPlan() => new()
    {
        ProviderResponseSha256=ResponseSha256,ProviderGraphVersion=GraphVersion,GeometrySourceReference=SourceReference,ProviderName=Provider,ProviderProfile=Profile,GeometrySource=GeometrySource,ProviderFailure=Failure,ProviderDurationSeconds=DurationSeconds,PassosNavegacao=Steps,
        RotaDisponivel = Failure == ProviderFailure.None && Geometry.Count > 1, TrajetoCoordenadas = Geometry,
        PerfilVeiculo = Profile=="driving"?"Leve":"Pesado", DistanciaKm = DistanceKm ?? 0, TempoEstimadoMinutos = EstimatedMinutes ?? 0,
        AvisoRota = Provider+": restrições e correspondência viária não comprovadas; orientação pesada suspensa. "+Detail
    };
    public RouteEvidence Evidence() => new()
    {
        Provider = Provider, RoutingProfile = Profile, GeographicVersion = GraphVersion,
        SourceReference = "HTTP /route; SHA256 " + (ResponseSha256 ?? "indisponível") + "; " + Detail,
        // /route does not provide a verified restriction inventory, graph version or road correspondence.
        Rules = []
    };
}
public interface IExperimentalTruckRouter
{
    bool Enabled { get; }
    Task<ProviderRouteReply> RouteAsync(ProviderRouteRequest request, CancellationToken cancellationToken = default);
}
