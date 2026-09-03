using System.Net;

namespace WattsUp.Middleware;

/// <summary>
/// The add-on has no auth of its own — Home Assistant's ingress proxy already authenticated the
/// user before forwarding the request, and it always connects from a fixed internal IP. Reject
/// anything else with 403, so the app is never reachable except through ingress. Loopback is
/// allowed in Development so <c>dotnet run</c> works without HA in the loop.
/// </summary>
public sealed class IngressRemoteIpFilterMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private static readonly IPAddress IngressAddress = IPAddress.Parse("172.30.32.2");

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;

        var allowed = remoteIp is not null &&
            (remoteIp.Equals(IngressAddress) || (environment.IsDevelopment() && IPAddress.IsLoopback(remoteIp)));

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: WattsUp is only reachable through Home Assistant ingress.");
            return;
        }

        await next(context);
    }
}
