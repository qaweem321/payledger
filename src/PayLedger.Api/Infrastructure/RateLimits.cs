using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PayLedger.Api.Auth;

namespace PayLedger.Api.Infrastructure;

public static class RateLimits
{
    public const string Signup = "signup";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var requestsPerMinute = configuration.GetValue("RateLimits:RequestsPerMinute", 300);
        var signupsPerHour = configuration.GetValue("RateLimits:SignupsPerHour", 10);
        return
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await Results.Problem(statusCode: 429, title: "Too many requests.", extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };

            // Every caller: RequestsPerMinute (default 300), per merchant when signed in, otherwise per IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.IsAuthenticated == true
                        ? "m:" + context.User.GetMerchantId()
                        : "ip:" + context.Connection.RemoteIpAddress,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1) }));

            // Signups create keys and settlement accounts: SignupsPerHour (default 10) per IP.
            options.AddPolicy(Signup, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    "ip:" + context.Connection.RemoteIpAddress,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = signupsPerHour, Window = TimeSpan.FromHours(1) }));
        });
    }
}
