using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;

namespace MglConnect.Web.Middleware;

public sealed class ActiveAccountMiddleware
{
    private readonly RequestDelegate _next;

    public ActiveAccountMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, MglDbContext database)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = int.TryParse(userIdClaim, out var userId)
                ? await database.Usuarios.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId)
                : null;
            if (user == null || !user.Ativo || !HasExpectedRole(context.User, user.Perfil))
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/telemetryHub")
                    || context.Request.Path.StartsWithSegments("/trackingHub"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                context.Response.Redirect("/Account/Login");
                return;
            }
        }

        await _next(context);
    }

    private static bool HasExpectedRole(ClaimsPrincipal principal, string profile)
    {
        var expected = profile.Equals("Administrador", StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : profile;
        return principal.IsInRole(expected)
            || (expected == "Admin" && principal.IsInRole("Administrador"));
    }
}
