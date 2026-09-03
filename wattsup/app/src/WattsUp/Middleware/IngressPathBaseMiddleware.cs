using Microsoft.Extensions.Primitives;

namespace WattsUp.Middleware;

/// <summary>
/// HA ingress sends <c>X-Ingress-Path</c> (a per-installation path like
/// <c>/api/hassio_ingress/&lt;token&gt;</c>) on every request — it's known only at request time, so
/// the static <c>app.UsePathBase(...)</c> can't be used (that needs a startup-time constant).
/// Setting <see cref="HttpRequest.PathBase"/> here makes static assets, <c>blazor.web.js</c>, and —
/// critically — the SignalR negotiate URL all resolve under the ingress prefix, and self-corrects if
/// HA regenerates the token without a redeploy. Plain <c>dotnet run</c> sends no such header, so
/// PathBase stays empty and the app behaves like a normal top-level app.
/// </summary>
public sealed class IngressPathBaseMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Ingress-Path", out var raw) && !StringValues.IsNullOrEmpty(raw))
        {
            var pathBase = new PathString(raw.ToString());
            context.Request.PathBase = pathBase;
            if (context.Request.Path.StartsWithSegments(pathBase, out var remainder))
            {
                context.Request.Path = remainder.Value == "" ? "/" : remainder;
            }
        }

        return next(context);
    }
}
