using System.Text.Json;

namespace AppInsightsLogs.Api.Models;

public sealed record KqlSchemaColumn(string Name, string Type);

public sealed record KqlSchemaTable(string Name, string? Description, IReadOnlyList<KqlSchemaColumn> Columns);

public sealed record KqlSchema(IReadOnlyList<KqlSchemaTable> Tables);

public sealed record RunQueryRequest(string? AppKey, string? Query, int RangeMinutes = 1440);

public sealed record KqlColumn(string Name, string Type);

public sealed record KqlResult(
    IReadOnlyList<KqlColumn> Columns,
    IReadOnlyList<JsonElement[]> Rows,
    int TotalRows,
    bool Truncated,
    long ElapsedMs);

/// <summary>Ask the assistant to write a query, or (with CurrentQuery and Error) to fix one.</summary>
public sealed record GenerateQueryRequest(string? AppKey, string? Prompt, string? CurrentQuery = null, string? Error = null);

public sealed record GeneratedQuery(string Query, string Explanation);

public sealed record FeatureFlags(bool AiQueryEnabled);
