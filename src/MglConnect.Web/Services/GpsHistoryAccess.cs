using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
namespace MglConnect.Web.Services;
public sealed record GpsAccess(Usuario Actor,Entrega Delivery,bool Admin);
public sealed class GpsHistoryAccess(MglDbContext db)
{
    public async Task<GpsAccess?> ResolveAsync(ClaimsPrincipal user,int trip,CancellationToken ct=default)
    {
        if(!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier),out var id))return null;
        var actor=await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==id&&u.Ativo,ct);
        if(actor==null)return null;
        var delivery=await db.Entregas.AsNoTracking().Include(e=>e.Pedido).SingleOrDefaultAsync(e=>e.Id==trip&&e.TenantId==actor.TenantId,ct);
        if(delivery==null)return null;
        bool admin=actor.Perfil is "Admin" or "Administrador" && user.IsInRole("Admin");
        var allowed=admin||actor.Perfil switch{
            "Motorista"=>user.IsInRole("Motorista")&&delivery.MotoristaId==id,
            "Cliente" or "Intermediador"=>user.IsInRole(actor.Perfil)&&actor.ClienteId==delivery.Pedido?.ClienteId
                &&await db.Clientes.AnyAsync(c=>c.Id==actor.ClienteId&&c.Ativo&&c.TenantId==actor.TenantId,ct),_=>false};
        return allowed?new(actor,delivery,admin):null;
    }
}
