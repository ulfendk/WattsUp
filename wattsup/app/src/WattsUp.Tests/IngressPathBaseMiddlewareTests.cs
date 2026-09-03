using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using WattsUp.Middleware;

namespace WattsUp.Tests;

/// <summary>
/// Exercises the two ingress middlewares directly against a <see cref="DefaultHttpContext"/> —
/// the standard, host-free way to unit test ASP.NET Core middleware — rather than through a full
/// <c>WebApplicationFactory</c>, so these tests stay fast and don't depend on network access or a
/// real SQLite file the app's other hosted services would otherwise need.
/// </summary>
public class IngressPathBaseMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithIngressPathHeader_SetsPathBaseAndTrimsPath()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Ingress-Path"] = "/api/hassio_ingress/testtoken";
        context.Request.Path = "/api/hassio_ingress/testtoken/settings";

        var middleware = new IngressPathBaseMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal("/api/hassio_ingress/testtoken", context.Request.PathBase.Value);
        Assert.Equal("/settings", context.Request.Path.Value);
    }

    [Fact]
    public async Task InvokeAsync_WithIngressPathHeaderAtRoot_TrimsToSlash()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Ingress-Path"] = "/api/hassio_ingress/testtoken";
        context.Request.Path = "/api/hassio_ingress/testtoken";

        var middleware = new IngressPathBaseMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal("/", context.Request.Path.Value);
    }

    [Fact]
    public async Task InvokeAsync_NoIngressPathHeader_LeavesPathBaseEmpty()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/settings";

        var middleware = new IngressPathBaseMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal("", context.Request.PathBase.Value);
        Assert.Equal("/settings", context.Request.Path.Value);
    }

    [Fact]
    public async Task InvokeAsync_RequestFromIngressAddress_IsAllowedThrough()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.30.32.2");
        var nextCalled = false;

        var middleware = new IngressRemoteIpFilterMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, FakeEnvironment(isDevelopment: false));
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RequestFromOtherAddressInProduction_Returns403()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
        var nextCalled = false;

        var middleware = new IngressRemoteIpFilterMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, FakeEnvironment(isDevelopment: false));
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_LoopbackInDevelopment_IsAllowedThrough()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var nextCalled = false;

        var middleware = new IngressRemoteIpFilterMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, FakeEnvironment(isDevelopment: true));
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_LoopbackInProduction_Returns403()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var nextCalled = false;

        var middleware = new IngressRemoteIpFilterMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, FakeEnvironment(isDevelopment: false));
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static IHostEnvironment FakeEnvironment(bool isDevelopment)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.SetupGet(e => e.EnvironmentName).Returns(isDevelopment ? Environments.Development : Environments.Production);
        return mock.Object;
    }
}
