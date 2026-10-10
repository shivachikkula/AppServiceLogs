namespace AppInsightsLogs.Api.Options;

/// <summary>Limits for user-written KQL on the Query page.</summary>
public sealed class QueryOptions
{
    public const string SectionName = "Query";

    /// <summary>Rows returned to the browser; larger results are truncated (the total is still reported).</summary>
    public int MaxResultRows { get; set; } = 10_000;

    /// <summary>Upper bound for the time range a query may cover.</summary>
    public int MaxRangeDays { get; set; } = 90;

    /// <summary>How long the table/column schema of an application is cached.</summary>
    public int SchemaCacheMinutes { get; set; } = 60;
}
