using System.Globalization;
using System.Threading.RateLimiting;
using AppInsightsLogs.Api.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace AppInsightsLogs.Api.Services;

public static class RateLimiting
{
    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, RateLimitOptions limits) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await Results.Problem(
                    title: "TooManyRequests",
                    detail: $"Too many requests. Wait {retryAfter} seconds and try again.",
                    statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context.HttpContext);
            };

            // Every /api call counts towards a generous per-user ceiling; static files are not limited.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/api")
                    ? RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => PerMinute(limits.ApiPerMinute))
                    : RateLimitPartition.GetNoLimiter("static"));

            options.AddPolicy(RateLimitPolicies.Query, context => RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context), _ => Sliding(limits.QueryPerMinute)));
            options.AddPolicy(RateLimitPolicies.Ai, context => RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context), _ => Sliding(limits.AiPerMinute)));
            options.AddPolicy(RateLimitPolicies.Overview, context => RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context), _ => Sliding(limits.OverviewPerMinute)));
        });

    /// <summary>The signed-in user's email; the client IP for anonymous requests.</summary>
    internal static string PartitionKey(HttpContext context) =>
        UserClaims.GetEmail(context.User) is { } email
            ? "user:" + email
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = Math.Max(1, permits),
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };

    private static SlidingWindowRateLimiterOptions Sliding(int permits) => new()
    {
        PermitLimit = Math.Max(1, permits),
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    };
}
