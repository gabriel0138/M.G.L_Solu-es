using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
namespace MglConnect.Web.Controllers;

[Authorize(Roles="Admin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OperationalHealthController(MglDbContext db):Controller
{
    [HttpGet("operacional/saude")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Connection health only: does not certify schema, providers, routing or devices.
        var connected=await db.Database.CanConnectAsync(ct);
        return StatusCode(connected?200:503,new{conexaoBanco=connected?"disponivel":"indisponivel",verificadoEmUtc=DateTime.UtcNow});
    }
}
