using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace MglConnect.Web.Services;

public static class MglConnectApiBearerDefaults
{
    public const string Scheme = "MglBearer";
    public const string SmartScheme = "MglSmart";
    public const string CookieName = ".AspNetCore.Cookies";
    internal const string ProtectionPurpose = "MglConnect.ApiBearer.v1";

    public static string SelectScheme(HttpContext context)
    {
        if (context.Request.Headers.Authorization.ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Scheme;
        }

        if (context.Request.Method == HttpMethods.Get
            && (context.Request.Path.Value?.EndsWith("/trafego-eta", StringComparison.OrdinalIgnoreCase) == true
                || context.Request.Path.StartsWithSegments("/api/gps/historico"))
            && context.Request.Cookies.ContainsKey(CookieName))
        {
            return CookieAuthenticationDefaults.AuthenticationScheme;
        }

        return context.Request.Path.StartsWithSegments("/api")
            ? Scheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    }
}

public static class ApiBearerToken
{
    public static string Protect(
        ClaimsPrincipal principal,
        AuthenticationProperties properties,
        IDataProtectionProvider provider)
    {
        var protector = CreateProtector(provider);
        var format = new TicketDataFormat(protector);
        var ticket = new AuthenticationTicket(
            principal,
            properties,
            MglConnectApiBearerDefaults.Scheme);

        return format.Protect(ticket);
    }

    internal static IDataProtector CreateProtector(IDataProtectionProvider provider) =>
        provider.CreateProtector(MglConnectApiBearerDefaults.ProtectionPurpose);
}

public sealed class ApiBearerAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ApiBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDataProtectionProvider dataProtectionProvider)
        : base(options, logger, encoder)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (token.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Bearer token ausente."));
        }

        AuthenticationTicket? ticket;
        try
        {
            var format = new TicketDataFormat(ApiBearerToken.CreateProtector(_dataProtectionProvider));
            ticket = format.Unprotect(token);
        }
        catch (CryptographicException exception)
        {
            Logger.LogWarning(exception, "Token bearer inválido ou protegido por outra chave.");
            return Task.FromResult(AuthenticateResult.Fail("Bearer token inválido."));
        }

        if (ticket == null
            || ticket.Properties.ExpiresUtc is not DateTimeOffset expires
            || expires <= TimeProvider.GetUtcNow())
        {
            return Task.FromResult(AuthenticateResult.Fail("Bearer token expirado ou inválido."));
        }

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
