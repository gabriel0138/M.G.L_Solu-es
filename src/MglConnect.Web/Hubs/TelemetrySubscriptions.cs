using System.Collections.Concurrent;
using System.Security.Claims;

namespace MglConnect.Web.Hubs;

// Connection-local authorization snapshot, never an authority for current access.
// A recipient is checked against the database again before each private broadcast.
public sealed class TelemetrySubscriptions
{
    public sealed record Subscription(string ConnectionId, int UserId, Guid TenantId, string Role);
    private readonly ConcurrentDictionary<(string Connection, string Code), Subscription> _items = new();

    public void Add(string code, string connection, ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue("TenantId"), out var tenantId))
            throw new Microsoft.AspNetCore.SignalR.HubException("Identidade de rastreamento inválida.");
        var role = new[] { "Admin", "Motorista", "Cliente", "Intermediador" }.FirstOrDefault(principal.IsInRole);
        if (role == null) throw new Microsoft.AspNetCore.SignalR.HubException("Perfil de rastreamento inválido.");
        _items[(connection, code)] = new(connection, userId, tenantId, role);
    }

    public IReadOnlyList<Subscription> ForCode(string code) => _items
        .Where(item => item.Key.Code == code).Select(item => item.Value).ToArray();

    public void Remove(string connection, string code) => _items.TryRemove((connection, code), out _);
    public void Disconnect(string connection)
    {
        foreach (var key in _items.Keys.Where(key => key.Connection == connection)) _items.TryRemove(key, out _);
    }
}
