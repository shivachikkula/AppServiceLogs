using System.Text;
using System.Text.Json;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

public sealed record LiveLogsQuery(
    DateTimeOffset? Since,
    int LookbackMinutes,
    IReadOnlyCollection<string> ItemTypes,
    int MinSeverity,
    string? Search,
    string? RoleName,
    string? OperationId,
    int Take);

public sealed record ExceptionsQuery(
    int RangeMinutes,
    string? Search,
    string? ProblemId,
    string? RoleName,
    string? OperationId,
    int Take);

/// <summary>Builds KQL for the two views and maps the results to DTOs.</summary>
public sealed class TelemetryService(AppInsightsQueryClient client, IOptions<AppInsightsOptions> options)
{
    /// <summary>App Insights item type (as shown in the UI) mapped to its table name.</summary>
    public static readonly IReadOnlyDictionary<string, string> Tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["trace"] = "traces",
        ["request"] = "requests",
        ["dependency"] = "dependencies",
        ["exception"] = "exceptions",
        ["customEvent"] = "customEvents",
    };

    private int MaxRows => Math.Max(1, options.Value.MaxRows);

    public async Task<LiveLogsResponse> GetLiveLogsAsync(LiveLogsQuery request, CancellationToken cancellationToken)
    {
        var serverTime = DateTimeOffset.UtcNow;
        var tables = request.ItemTypes
            .Select(t => Tables.TryGetValue(t, out var table) ? table : null)
            .OfType<string>()
            .Distinct()
            .ToList();
        if (tables.Count == 0)
        {
            tables = Tables.Values.ToList();
        }

        var take = Math.Clamp(request.Take, 1, MaxRows);
        var kql = new StringBuilder();
        // ingestion_time() is evaluated per table, before the union, so polling can resume from the last
        // ingested item even when telemetry arrives out of timestamp order.
        var sinceFilter = request.Since is { } since ? $" | where IngestedAt > {Kql.DateTime(since)}" : string.Empty;
        var subqueries = tables.Select(t =>
            $"({t} | where timestamp > ago({Kql.Minutes(request.LookbackMinutes)}) | extend IngestedAt = ingestion_time(){sinceFilter})");
        kql.AppendLine($"union isfuzzy=true {string.Join(", ", subqueries)}");

        // column_ifexists keeps the query valid when only some tables are selected.
        kql.AppendLine("""
            | extend _message = tostring(column_ifexists("message", "")),
                     _severity = toint(column_ifexists("severityLevel", int(null))),
                     _success = tostring(column_ifexists("success", "")),
                     _name = tostring(column_ifexists("name", "")),
                     _resultCode = tostring(column_ifexists("resultCode", "")),
                     _type = tostring(column_ifexists("type", "")),
                     _target = tostring(column_ifexists("target", "")),
                     _outerMessage = tostring(column_ifexists("outerMessage", "")),
                     _innermostMessage = tostring(column_ifexists("innermostMessage", "")),
                     _duration = todouble(column_ifexists("duration", real(null)))
            | extend Severity = coalesce(_severity, iff(_success =~ "false", 3, 1))
            | extend Message = case(
                itemType == "trace", _message,
                itemType == "request", strcat(_name, " -> ", _resultCode),
                itemType == "dependency", strcat(_type, " ", _target, " | ", _name, " -> ", _resultCode),
                itemType == "exception", strcat(_type, ": ", iff(isempty(_outerMessage), _innermostMessage, _outerMessage)),
                _name)
            """);
        if (request.MinSeverity > 0)
        {
            kql.AppendLine($"| where Severity >= {request.MinSeverity}");
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            kql.AppendLine($"| where Message contains {Kql.String(request.Search)} or operation_Name contains {Kql.String(request.Search)}");
        }
        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            kql.AppendLine($"| where cloud_RoleName =~ {Kql.String(request.RoleName)}");
        }
        if (!string.IsNullOrWhiteSpace(request.OperationId))
        {
            kql.AppendLine($"| where operation_Id == {Kql.String(request.OperationId)}");
        }

        kql.AppendLine($"| top {take} by IngestedAt desc");
        kql.AppendLine("| project itemId, timestamp, IngestedAt, itemType, Severity, Message, cloud_RoleName, cloud_RoleInstance, operation_Name, operation_Id, resultCode = _resultCode, duration = _duration, customDimensions");

        var table = await client.QueryAsync(kql.ToString(), TimeSpan.FromMinutes(request.LookbackMinutes + 5), cancellationToken);

        var items = table.Rows
            .Select(r => new LogEntry(
                ItemId: r.GetString("itemId") ?? Guid.NewGuid().ToString(),
                Timestamp: r.GetDateTime("timestamp") ?? serverTime,
                IngestedAt: r.GetDateTime("IngestedAt"),
                ItemType: r.GetString("itemType") ?? "unknown",
                SeverityLevel: r.GetInt("Severity") ?? 1,
                Message: r.GetString("Message") ?? string.Empty,
                RoleName: r.GetString("cloud_RoleName"),
                RoleInstance: r.GetString("cloud_RoleInstance"),
                OperationName: r.GetString("operation_Name"),
                OperationId: r.GetString("operation_Id"),
                ResultCode: NullIfEmpty(r.GetString("resultCode")),
                DurationMs: r.GetDouble("duration"),
                CustomDimensions: NullIfEmpty(r.GetString("customDimensions"))))
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var cursor = items.Count > 0 ? items.Max(i => i.IngestedAt) ?? request.Since : request.Since;
        return new LiveLogsResponse(items, cursor, serverTime);
    }

    public async Task<IReadOnlyList<ExceptionEntry>> GetExceptionsAsync(ExceptionsQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, MaxRows);
        var kql = new StringBuilder();
        kql.AppendLine("exceptions");
        kql.AppendLine($"| where timestamp > ago({Kql.Minutes(request.RangeMinutes)})");
        AppendExceptionFilters(kql, request);
        kql.AppendLine($"| top {take} by timestamp desc");
        kql.AppendLine(ExceptionProjection);

        var table = await client.QueryAsync(kql.ToString(), TimeSpan.FromMinutes(request.RangeMinutes + 5), cancellationToken);
        return table.Rows.Select(MapException).ToList();
    }

    public async Task<IReadOnlyList<ExceptionGroup>> GetExceptionSummaryAsync(ExceptionsQuery request, CancellationToken cancellationToken)
    {
        var kql = new StringBuilder();
        kql.AppendLine("exceptions");
        kql.AppendLine($"| where timestamp > ago({Kql.Minutes(request.RangeMinutes)})");
        AppendExceptionFilters(kql, request with { ProblemId = null });
        kql.AppendLine("""
            | summarize Count = count(), AffectedOperations = dcount(operation_Id), FirstSeen = min(timestamp), LastSeen = max(timestamp),
                        Type = take_any(type), Message = take_any(iff(isempty(outerMessage), innermostMessage, outerMessage)) by problemId
            | top 50 by Count desc
            """);

        var table = await client.QueryAsync(kql.ToString(), TimeSpan.FromMinutes(request.RangeMinutes + 5), cancellationToken);
        return table.Rows
            .Select(r => new ExceptionGroup(
                ProblemId: r.GetString("problemId") ?? "(none)",
                Type: r.GetString("Type"),
                Message: r.GetString("Message"),
                Count: r.GetLong("Count") ?? 0,
                AffectedOperations: r.GetLong("AffectedOperations") ?? 0,
                FirstSeen: r.GetDateTime("FirstSeen"),
                LastSeen: r.GetDateTime("LastSeen")))
            .ToList();
    }

    public async Task<ExceptionDetail?> GetExceptionAsync(string itemId, int rangeMinutes, CancellationToken cancellationToken)
    {
        var kql = $"""
            exceptions
            | where timestamp > ago({Kql.Minutes(rangeMinutes)})
            | where itemId == {Kql.String(itemId)}
            | take 1
            {ExceptionProjection}, details, customDimensions
            """;

        var table = await client.QueryAsync(kql, TimeSpan.FromMinutes(rangeMinutes + 5), cancellationToken);
        var row = table.Rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var details = row.GetDynamic("details");
        return new ExceptionDetail(
            MapException(row),
            BuildStackTrace(details),
            NullIfEmpty(row.GetString("customDimensions")),
            details?.GetRawText());
    }

    private const string ExceptionProjection =
        "| project itemId, timestamp, problemId, type, outerMessage, innermostMessage, method, assembly, severityLevel, cloud_RoleName, cloud_RoleInstance, operation_Name, operation_Id, client_Type";

    private static void AppendExceptionFilters(StringBuilder kql, ExceptionsQuery request)
    {
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = Kql.String(request.Search);
            kql.AppendLine($"| where type contains {term} or outerMessage contains {term} or innermostMessage contains {term} or problemId contains {term} or operation_Name contains {term}");
        }
        if (!string.IsNullOrWhiteSpace(request.ProblemId))
        {
            kql.AppendLine($"| where problemId == {Kql.String(request.ProblemId)}");
        }
        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            kql.AppendLine($"| where cloud_RoleName =~ {Kql.String(request.RoleName)}");
        }
        if (!string.IsNullOrWhiteSpace(request.OperationId))
        {
            kql.AppendLine($"| where operation_Id == {Kql.String(request.OperationId)}");
        }
    }

    private static ExceptionEntry MapException(QueryRow r) => new(
        ItemId: r.GetString("itemId") ?? string.Empty,
        Timestamp: r.GetDateTime("timestamp") ?? DateTimeOffset.MinValue,
        ProblemId: r.GetString("problemId"),
        Type: r.GetString("type"),
        Message: r.GetString("outerMessage"),
        InnermostMessage: r.GetString("innermostMessage"),
        Method: r.GetString("method"),
        Assembly: r.GetString("assembly"),
        SeverityLevel: r.GetInt("severityLevel") ?? 3,
        RoleName: r.GetString("cloud_RoleName"),
        RoleInstance: r.GetString("cloud_RoleInstance"),
        OperationName: r.GetString("operation_Name"),
        OperationId: r.GetString("operation_Id"),
        ClientType: r.GetString("client_Type"));

    /// <summary>
    /// Reconstructs a readable stack trace from the <c>details</c> column, which holds one entry per
    /// exception in the chain, each with either a <c>parsedStack</c> array or a <c>rawStack</c> string.
    /// </summary>
    public static string BuildStackTrace(JsonElement? details)
    {
        if (details is not { ValueKind: JsonValueKind.Array } array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var exception in array.EnumerateArray())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine("--- Inner exception ---");
            }

            builder.Append(Prop(exception, "type") ?? "Exception");
            var message = Prop(exception, "message");
            if (!string.IsNullOrEmpty(message))
            {
                builder.Append(": ").Append(message);
            }
            builder.AppendLine();

            if (exception.TryGetProperty("parsedStack", out var stack) && stack.ValueKind == JsonValueKind.Array && stack.GetArrayLength() > 0)
            {
                foreach (var frame in stack.EnumerateArray())
                {
                    builder.Append("   at ").Append(Prop(frame, "method") ?? "?");
                    var file = Prop(frame, "fileName");
                    if (!string.IsNullOrEmpty(file))
                    {
                        builder.Append(" in ").Append(file);
                        var line = Prop(frame, "line");
                        if (!string.IsNullOrEmpty(line) && line != "0")
                        {
                            builder.Append(':').Append(line);
                        }
                    }
                    builder.AppendLine();
                }
            }
            else if (Prop(exception, "rawStack") is { Length: > 0 } raw)
            {
                builder.AppendLine(raw.TrimEnd());
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null => null,
                _ => value.GetRawText(),
            }
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
