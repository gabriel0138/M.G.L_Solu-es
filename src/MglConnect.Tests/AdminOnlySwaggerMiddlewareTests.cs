using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MglConnect.Web.Middleware;
using Xunit;

namespace MglConnect.Tests;

public class AdminOnlySwaggerMiddlewareTests
{
    [Fact]
    public async Task SwaggerIsNotServedToAnonymousUsers()
    {
        var context = CreateContext("/swagger/v1/swagger.json", null);
        var nextWasCalled = false;

        await InvokeAsync(context, () => nextWasCalled = true);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextWasCalled);
    }

    [Fact]
    public async Task SwaggerIsNotServedToNonAdministrators()
    {
        var principal = CreatePrincipal("Cliente");
        var context = CreateContext("/swagger", principal);
        var nextWasCalled = false;

        await InvokeAsync(context, () => nextWasCalled = true);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextWasCalled);
    }

    [Fact]
    public async Task AdministratorCanOpenSwagger()
    {
        var principal = CreatePrincipal("Admin");
        var context = CreateContext("/swagger", principal);
        var nextWasCalled = false;

        await InvokeAsync(context, () => nextWasCalled = true);

        Assert.True(nextWasCalled);
    }

    [Fact]
    public async Task NonSwaggerPathsAreNotBlocked()
    {
        var context = CreateContext("/api/entregas", null);
        var nextWasCalled = false;

        await InvokeAsync(context, () => nextWasCalled = true);

        Assert.True(nextWasCalled);
    }

    private static async Task InvokeAsync(HttpContext context, Action next)
    {
        var middleware = new AdminOnlySwaggerMiddleware(_ =>
        {
            next();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
    }

    private static DefaultHttpContext CreateContext(
        string path,
        ClaimsPrincipal? user)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (user is not null)
        {
            context.User = user;
        }

        return context;
    }

    private static ClaimsPrincipal CreatePrincipal(string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "test"),
            new Claim(ClaimTypes.Role, role)
        }, "test"));
}
