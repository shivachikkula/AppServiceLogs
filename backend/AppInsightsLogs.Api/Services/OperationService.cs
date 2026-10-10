using System.Globalization;
using System.Text;
using AppInsightsLogs.Api.Models;

namespace AppInsightsLogs.Api.Services;

/// <summary>Everything recorded for one operation (operation_Id), for the timeline / waterfall view.</summary>
public sealed class OperationService(AppInsightsQueryClient client)
{
    public const int MaxItems = 2000;

    public async Task<OperationTimeline> GetAsync(string applicationId, string operationId, DateTimeOffset? around, CancellationToken cancellationToken)
    {
        // Search a day either side of the hint (fast), or the last 30 days when there is none.
        var (from, to) = around is { } t
            ? (t.AddDays(-1), t.AddDays(1))
            : (DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow);
        var filter = $"| where timestamp between ({Kql.DateTime(from)} .. {Kql.DateTime(to)}) and operation_Id == {Kql.String(operationId)}";

        var kql = new StringBuilder("union isfuzzy=true\n");
        kql.AppendJoin(",\n", new[]
        {
            $"(requests {filter} | project itemId, timestamp, Kind = \"request\", Id = id, ParentId = operation_ParentId, Name = name, Detail = url, ResultCode = tostring(resultCode), Success = tostring(success), Duration = todouble(duration), RoleName = cloud_RoleName)",
            $"(dependencies {filter} | project itemId, timestamp, Kind = \"dependency\", Id = id, ParentId = operation_ParentId, Name = name, Detail = data, Target = target, DependencyType = type, ResultCode = tostring(resultCode), Success = tostring(success), Duration = todouble(duration), RoleName = cloud_RoleName)",
            $"(exceptions {filter} | project itemId, timestamp, Kind = \"exception\", ParentId = operation_ParentId, Name = type, Detail = iff(isempty(outerMessage), innermostMessage, outerMessage), Severity = toint(severityLevel), RoleName = cloud_RoleName)",
            $"(traces {filter} | project itemId, timestamp, Kind = \"trace\", ParentId = operation_ParentId, Name = message, Severity = toint(severityLevel), RoleName = cloud_RoleName)",
            $"(customEvents {filter} | project itemId, timestamp, Kind = \"event\", ParentId = operation_ParentId, Name = name, RoleName = cloud_RoleName)",
        });
        kql.Append(CultureInfo.InvariantCulture, $"\n| order by timestamp asc\n| take {MaxItems + 1}");

        var table = await client.QueryAsync(applicationId, kql.ToString(), to - from, cancellationToken);
        var items = table.Rows
            .Take(MaxItems)
            .Select(r => new OperationItem(
                r.GetString("itemId") ?? Guid.NewGuid().ToString(),
                r.GetString("Kind") ?? "trace",
                r.GetDateTime("timestamp") ?? DateTimeOffset.MinValue,
                NullIfEmpty(r.GetString("Id")),
                NullIfEmpty(r.GetString("ParentId")),
                r.GetString("Name") ?? string.Empty,
                NullIfEmpty(r.GetString("Detail")),
                NullIfEmpty(r.GetString("Target")),
                NullIfEmpty(r.GetString("DependencyType")),
                NullIfEmpty(r.GetString("ResultCode")),
                r.GetString("Success") is { Length: > 0 } s ? !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) : null,
                r.GetDouble("Duration"),
                r.GetInt("Severity"),
                NullIfEmpty(r.GetString("RoleName"))))
            .ToList();

        return new OperationTimeline(operationId, items, table.Rows.Count > MaxItems);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
