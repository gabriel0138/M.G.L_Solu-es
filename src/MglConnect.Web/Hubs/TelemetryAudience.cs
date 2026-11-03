using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;

namespace MglConnect.Web.Hubs;

internal static class TelemetryAudience
{
    internal static async Task<IReadOnlyList<string>> ResolveAsync(MglDbContext database, IDriverShiftService shifts,
        TelemetrySubscriptions subscriptions, string code, IGroupManager groups)
    {
            var delivery = await database.Entregas.AsNoTracking()
                .Where(e => e.CodigoRastreio == code)
                .Select(e => new { e.Id, e.TenantId, e.MotoristaId, e.AtribuicaoAtiva, e.StatusEntrega, ClienteId = (int?)e.Pedido!.ClienteId })
                .FirstOrDefaultAsync();
            if (delivery == null || !delivery.AtribuicaoAtiva
                || delivery.StatusEntrega is not ("Pendente" or "Em transporte")) return [];
            var authorized = new List<string>();
            foreach (var subscription in subscriptions.ForCode(code))
            {
                var user = await database.Usuarios.AsNoTracking().FirstOrDefaultAsync(u =>
                    u.Id == subscription.UserId && u.Ativo
                    && u.TenantId == subscription.TenantId && u.TenantId == delivery.TenantId);
                var profile = user?.Perfil == "Administrador" ? "Admin" : user?.Perfil;
                var allowed = profile == subscription.Role && (profile switch
                {
                    "Admin" => true,
                    "Motorista" => delivery.AtribuicaoAtiva && delivery.MotoristaId == user!.Id
                        && await shifts.HasActiveShiftAsync(user.Id, delivery.Id),
                    "Cliente" or "Intermediador" => user!.ClienteId == delivery.ClienteId
                        && await database.Clientes.AnyAsync(c => c.Id == user.ClienteId && c.Ativo
                            && c.TenantId == user.TenantId),
                    _ => false
                });
                if (allowed) authorized.Add(subscription.ConnectionId);
                else
                {
                    subscriptions.Remove(subscription.ConnectionId, code);
                    await groups.RemoveFromGroupAsync(subscription.ConnectionId, code);
                }
            }
            return authorized;
    }
}
