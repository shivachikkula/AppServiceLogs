using System.Diagnostics;
using System.Globalization;
using System.Text;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

/// <summary>Runs user-written KQL and describes the tables available to it.</summary>
public sealed class QueryService(AppInsightsQueryClient client, IMemoryCache cache, IOptions<QueryOptions> options)
{
    /// <summary>Application Insights tables, with a short description shown in the editor and given to the assistant.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownTables = new Dictionary<string, string>
    {
        ["requests"] = "Incoming HTTP requests handled by the application (name, url, resultCode, success, duration in ms).",
        ["dependencies"] = "Outgoing calls: SQL, HTTP, storage, queues (type, target, name, data, resultCode, success, duration in ms).",
        ["exceptions"] = "Exceptions (type, outerMessage, innermostMessage, problemId, method, details).",
        ["traces"] = "Log messages from ILogger and similar (message, severityLevel 0=Verbose .. 4=Critical).",
        ["customEvents"] = "Custom events tracked with TrackEvent (name, customDimensions).",
        ["customMetrics"] = "Custom metrics (name, value, valueCount, valueSum, valueMin, valueMax).",
        ["pageViews"] = "Browser page views (name, url, duration).",
        ["browserTimings"] = "Browser page load timings (networkDuration, processingDuration, totalDuration).",
        ["availabilityResults"] = "Availability (ping/web) test results (name, location, success, duration).",
        ["performanceCounters"] = "Performance counters (category, counter, instance, value).",
    };

    public async Task<KqlSchema> GetSchemaAsync(string applicationId, CancellationToken cancellationToken)
    {
        var cacheKey = $"kql-schema:{applicationId}";
        if (cache.TryGetValue(cacheKey, out KqlSchema? cached) && cached is not null)
        {
            return cached;
        }

        // One round trip: getschema of every known table, labelled with its name.
        var kql = new StringBuilder("union isfuzzy=true\n");
        kql.AppendJoin(",\n", KnownTables.Keys.Select(t => $"    ({t} | getschema | extend TableName = \"{t}\")"));
        kql.Append("\n| project TableName, ColumnName, ColumnType");

        var table = await client.QueryAsync(applicationId, kql.ToString(), TimeSpan.FromDays(1), cancellationToken);
        var columnsByTable = table.Rows
            .GroupBy(r => r.GetString("TableName") ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<KqlSchemaColumn>)g
                    .Select(r => new KqlSchemaColumn(r.GetString("ColumnName") ?? string.Empty, r.GetString("ColumnType") ?? "string"))
                    .ToList());

        var schema = new KqlSchema(KnownTables
            .Where(t => columnsByTable.ContainsKey(t.Key))
            .Select(t => new KqlSchemaTable(t.Key, t.Value, columnsByTable[t.Key]))
            .ToList());

        cache.Set(cacheKey, schema, TimeSpan.FromMinutes(Math.Max(1, options.Value.SchemaCacheMinutes)));
        return schema;
    }

    public async Task<KqlResult> RunAsync(string applicationId, string query, int rangeMinutes, CancellationToken cancellationToken)
    {
        KqlGuard.EnsureAllowed(query);

        var limits = options.Value;
        var range = TimeSpan.FromMinutes(Math.Clamp(rangeMinutes, 1, limits.MaxRangeDays * 24 * 60));
        var stopwatch = Stopwatch.StartNew();
        var table = await client.QueryAsync(applicationId, query, range, cancellationToken);
        stopwatch.Stop();

        var maxRows = Math.Max(1, limits.MaxResultRows);
        var rows = table.RawRows.Count > maxRows ? table.RawRows.Take(maxRows).ToList() : table.RawRows;
        return new KqlResult(
            table.Columns.Select((name, i) => new KqlColumn(name, table.ColumnTypes[i])).ToList(),
            rows,
            table.RawRows.Count,
            table.RawRows.Count > maxRows,
            stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Compact "table(column:type, ...)" listing used in the assistant's prompt.</summary>
    public static string DescribeSchema(KqlSchema schema)
    {
        var builder = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- {table.Name}: {table.Description}\n  columns: ");
            builder.AppendJoin(", ", table.Columns.Select(c => $"{c.Name}:{c.Type}"));
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
