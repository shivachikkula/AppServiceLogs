using System.Diagnostics.Metrics;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Custom metrics for the viewer itself, exported with OpenTelemetry (to Azure Monitor when configured) so alerts
/// can watch for spikes in rejected queries, denied access or rate limiting.
/// </summary>
public static class AppMetrics
{
    public const string MeterName = "OsseLogViewer";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> QueriesRun =
        Meter.CreateCounter<long>("osse.queries", description: "User-written KQL queries, by outcome (succeeded, failed, rejected).");

    public static readonly Counter<long> AiRequests =
        Meter.CreateCounter<long>("osse.ai_requests", description: "AI query assistant requests, by outcome.");

    public static readonly Counter<long> AccessDenied =
        Meter.CreateCounter<long>("osse.access_denied", description: "Requests for an application the user is not assigned to.");

    public static readonly Counter<long> RateLimited =
        Meter.CreateCounter<long>("osse.rate_limited", description: "Requests rejected by a rate limit.");
}
