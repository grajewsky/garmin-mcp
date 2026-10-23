namespace GarminMcp.Hosting;

/// <summary>
/// Ochrona przed DNS rebinding: żądanie do endpointu MCP z nagłówkiem <c>Origin</c> spoza loopback
/// (<c>http://localhost[:port]</c>, <c>http://127.0.0.1[:port]</c>) dostaje 403. Brak <c>Origin</c> (klient nie-przeglądarkowy) przechodzi.
/// </summary>
public static class LoopbackOrigin
{
    public static bool IsAllowed(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Host is "localhost" or "127.0.0.1"
        && uri.PathAndQuery == "/"
        && string.IsNullOrEmpty(uri.UserInfo);

    public static IApplicationBuilder UseLoopbackOriginCheck(this IApplicationBuilder app, string pathPrefix) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments(pathPrefix)
                && ctx.Request.Headers.Origin.Count > 0
                && !IsAllowed(ctx.Request.Headers.Origin.ToString()))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(ctx);
        });
}
