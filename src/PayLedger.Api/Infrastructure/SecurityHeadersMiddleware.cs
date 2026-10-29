namespace PayLedger.Api.Infrastructure;

public static class SecurityHeaders
{
    /// <summary>API responses hold financial data: never cache them, never render them as a page.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers.XFrameOptions = "DENY";
                if (context.Request.Path.StartsWithSegments("/v1"))
                {
                    headers.CacheControl = "no-store";
                    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                }

                return Task.CompletedTask;
            });
            await next();
        });
}
