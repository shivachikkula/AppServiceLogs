namespace AppInsightsLogs.Api.Options;

/// <summary>Per-user request limits (per signed-in email, or per client IP before sign-in).</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>All /api calls. Each open Live logs tab polls about 6 times a minute.</summary>
    public int ApiPerMinute { get; set; } = 600;

    /// <summary>Running KQL on the Query page and loading operation timelines.</summary>
    public int QueryPerMinute { get; set; } = 30;

    /// <summary>AI assistant requests (each one is a paid Azure OpenAI call).</summary>
    public int AiPerMinute { get; set; } = 10;

    /// <summary>Overview dashboard loads (five Application Insights queries each).</summary>
    public int OverviewPerMinute { get; set; } = 30;
}

public static class RateLimitPolicies
{
    public const string Query = "query";
    public const string Ai = "ai";
    public const string Overview = "overview";
}
