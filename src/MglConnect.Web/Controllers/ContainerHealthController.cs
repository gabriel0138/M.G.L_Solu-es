using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;

namespace MglConnect.Web.Controllers;

[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerHealthController(MglDbContext db) : ControllerBase
{
    [HttpGet("health/live")]
    public IActionResult Live() => Ok(new { status = "live" });

    [HttpGet("health/ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(3));
        bool available;
        try { available = await db.Database.CanConnectAsync(limit.Token); }
        catch (Exception ex) when (ex is System.Data.Common.DbException or OperationCanceledException or InvalidOperationException) { available = false; }
        // Connectivity only, not routing availability, schema certification or physical GPS.
        return StatusCode(available ? 200 : 503, new { status = available ? "ready" : "unavailable" });
    }
}
