using System.Net;

namespace WattsUp.Middleware;

/// <summary>
/// The add-on has no auth of its own — Home Assistant's ingress proxy already authenticated the
/// user before forwarding the request, and it always connects from a fixed internal IP. Reject
/// anything else with 403, so the app is never reachable except through ingress. Loopback is
/// allowed in Development so <c>dotnet run</c> works without HA in the loop.
/// </summary>
public sealed class IngressRemoteIpFilterMiddleware(RequestDelegate next, IHostEnvironment environment, ILogger<IngressRemoteIpFilterMiddleware> logger)
{
    private static readonly IPAddress IngressAddress = IPAddress.Parse("172.30.32.2");

    public async Task InvokeAsync(HttpContext context)
    {
        // Kestrel listens dual-stack (ASPNETCORE_URLS=http://+:8099), so a genuine IPv4 peer often
        // shows up here as an IPv4-mapped IPv6 address (e.g. "::ffff:172.30.32.2") rather than
        // plain "172.30.32.2" — comparing without normalizing first would reject real ingress
        // traffic. Normalize before every check, including loopback.
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is { IsIPv4MappedToIPv6: true })
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var allowed = remoteIp is not null &&
            (remoteIp.Equals(IngressAddress) || (environment.IsDevelopment() && IPAddress.IsLoopback(remoteIp)));

        if (!allowed)
        {
            logger.LogWarning("Rejected request from non-ingress remote IP {RemoteIp}", remoteIp?.ToString() ?? "(none)");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: WattsUp is only reachable through Home Assistant ingress.");
            return;
        }

        await next(context);
    }
}
