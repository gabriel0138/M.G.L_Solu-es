using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers;

[Authorize(Roles = "Motorista")]
[Route("Motorista/Offline")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DriverOfflineController(IDataProtectionProvider protection) : Controller
{
    [HttpGet("Contexto")]
    public async Task<IActionResult> Contexto()
    {
        // A fresh cookie-authenticated context is required before replay. Never
        // persist bearer credentials in IndexedDB or the service-worker cache.
        var authenticated = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authenticated.Succeeded || authenticated.Principal == null
            || !authenticated.Principal.IsInRole("Motorista")
            || authenticated.Principal.FindFirstValue(ClaimTypes.NameIdentifier) != User.FindFirstValue(ClaimTypes.NameIdentifier)
            || authenticated.Principal.FindFirstValue("TenantId") != User.FindFirstValue("TenantId"))
            return Unauthorized(new { erro = "Sessão offline inválida." });
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(5);
        if (authenticated.Properties?.ExpiresUtc is { } cookieExpiry && cookieExpiry < expires) expires = cookieExpiry;
        if (expires <= now) return Unauthorized();
        var token = ApiBearerToken.Protect(authenticated.Principal,
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = expires }, protection);
        return Json(new
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            tenantId = User.FindFirstValue("TenantId"), role = "Motorista", token, expiresAt = expires
        });
    }
}
