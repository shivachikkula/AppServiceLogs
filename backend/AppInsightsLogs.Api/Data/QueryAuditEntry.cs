namespace AppInsightsLogs.Api.Data;

/// <summary>One user-written KQL query or AI assistant request: who, when, which application, what, and the outcome.</summary>
public sealed class QueryAuditEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Email { get; set; }
    public required string AppKey { get; set; }

    /// <summary><see cref="AuditActions"/>: query, ai.generate or ai.fix.</summary>
    public required string Action { get; set; }

    /// <summary>The KQL that was run, or the description sent to the AI assistant.</summary>
    public required string Text { get; set; }

    public int? RangeMinutes { get; set; }
    public bool Succeeded { get; set; }
    public int? RowCount { get; set; }
    public int? DurationMs { get; set; }
    public string? Error { get; set; }
}

public static class AuditActions
{
    public const string Query = "query";
    public const string AiGenerate = "ai.generate";
    public const string AiFix = "ai.fix";
}
