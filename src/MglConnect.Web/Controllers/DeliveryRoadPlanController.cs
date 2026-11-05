using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers;

[Authorize(Roles="Admin,Motorista")]
[Route("Entregas/{id:int}/Planejamento")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DeliveryRoadPlanController(GpsHistoryAccess access,DeliveryRoadPlanning plans) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Read(int id,CancellationToken ct)
    {
        var scope=await access.ResolveAsync(User,id,ct);
        if(scope==null||(!scope.Admin&&scope.Actor.Perfil!="Motorista"))return NotFound();
        var plan=await plans.ReadAsync(id,ct);
        return plan==null?NotFound():Json(new {plan});
    }
}
