namespace MglConnect.Web.Models;

// Immutable request identity; each attempt (including failure) is retained separately
// from GPS history and from the assessment that can authorize heavy navigation.
public sealed class DeliveryRoadPlan : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int EntregaId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }
    public string BindingHash { get; set; } = "";
    public string LocationsJson { get; set; } = "";
    public string PlanJson { get; set; } = "";
}
