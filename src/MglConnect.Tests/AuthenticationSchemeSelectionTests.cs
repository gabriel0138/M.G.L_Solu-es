using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public class AuthenticationSchemeSelectionTests
{
    [Fact]
    public void SelectScheme_UsesCookieForDriverTrafficEtaRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/entregas/1/trafego-eta";
        context.Request.Headers.Cookie = $"{MglConnectApiBearerDefaults.CookieName}=ticket";

        var scheme = MglConnectApiBearerDefaults.SelectScheme(context);

        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, scheme);
    }

    [Fact]
    public void SelectScheme_UsesBearerForOtherApiRequestsEvenWithCookie()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/entregas/1";
        context.Request.Headers.Cookie = $"{MglConnectApiBearerDefaults.CookieName}=ticket";

        var scheme = MglConnectApiBearerDefaults.SelectScheme(context);

        Assert.Equal(MglConnectApiBearerDefaults.Scheme, scheme);
    }

    [Fact]
    public void SelectScheme_PrefersExplicitBearerHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/entregas/1/trafego-eta";
        context.Request.Headers.Authorization = "Bearer access-token";
        context.Request.Headers.Cookie = $"{MglConnectApiBearerDefaults.CookieName}=ticket";

        var scheme = MglConnectApiBearerDefaults.SelectScheme(context);

        Assert.Equal(MglConnectApiBearerDefaults.Scheme, scheme);
    }
}
