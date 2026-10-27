namespace MglConnect.Web.Models;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
