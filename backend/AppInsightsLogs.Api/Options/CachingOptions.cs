namespace AppInsightsLogs.Api.Options;

/// <summary>
/// Short server-side caches, so many viewers of the same application share one set of Application Insights queries.
/// "Refresh" in the UI bypasses them. 0 disables a cache.
/// </summary>
public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    public int OverviewSeconds { get; set; } = 60;
    public int ExceptionsSeconds { get; set; } = 30;
    public int OperationSeconds { get; set; } = 60;
}
