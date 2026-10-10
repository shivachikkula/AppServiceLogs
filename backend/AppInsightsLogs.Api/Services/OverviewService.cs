using AppInsightsLogs.Api.Models;

namespace AppInsightsLogs.Api.Services;

/// <summary>Health overview of one application: headline numbers, trends and top problems.</summary>
public sealed class OverviewService(AppInsightsQueryClient client)
{
    // `success` is a bool in requests/dependencies; compare as text so the query also works if it is stored as a string.
    private const string Failed = "tostring(success) =~ \"false\"";

    public async Task<OverviewResponse> GetAsync(string applicationId, int rangeMinutes, CancellationToken cancellationToken)
    {
        var range = Math.Clamp(rangeMinutes, 15, 60 * 24 * 30);
        var bin = BinMinutes(range);
        var r = Kql.Minutes(range);
        var b = Kql.Minutes(bin);
        var window = TimeSpan.FromMinutes(range + bin);

        var kpisTask = client.QueryAsync(applicationId, $$"""
            let start = ago({{r}});
            let previousStart = ago(2 * {{r}});
            let req = materialize(requests | where timestamp > previousStart | project timestamp, duration, failed = {{Failed}});
            let exc = exceptions | where timestamp > previousStart | project timestamp;
            print
                Requests = toscalar(req | where timestamp > start | count),
                FailedRequests = toscalar(req | where timestamp > start and failed | count),
                AvgDuration = toscalar(req | where timestamp > start | summarize avg(duration)),
                P95Duration = toscalar(req | where timestamp > start | summarize percentile(duration, 95)),
                Exceptions = toscalar(exc | where timestamp > start | count),
                FailedDependencies = toscalar(dependencies | where timestamp > start and {{Failed}} | count),
                PreviousRequests = toscalar(req | where timestamp <= start | count),
                PreviousFailedRequests = toscalar(req | where timestamp <= start and failed | count),
                PreviousExceptions = toscalar(exc | where timestamp <= start | count)
            """, TimeSpan.FromMinutes(2 * range + bin), cancellationToken);

        var seriesTask = client.QueryAsync(applicationId, $$"""
            let start = bin(ago({{r}}), {{b}});
            let req = requests | where timestamp >= start
                | summarize Requests = count(), FailedRequests = countif({{Failed}}), P95 = percentile(duration, 95) by timestamp = bin(timestamp, {{b}});
            let exc = exceptions | where timestamp >= start
                | summarize Exceptions = count() by timestamp = bin(timestamp, {{b}});
            range timestamp from start to bin(now(), {{b}}) step {{b}}
            | join kind=leftouter req on timestamp
            | join kind=leftouter exc on timestamp
            | project timestamp,
                Requests = coalesce(Requests, tolong(0)),
                FailedRequests = coalesce(FailedRequests, tolong(0)),
                P95,
                Exceptions = coalesce(Exceptions, tolong(0))
            | order by timestamp asc
            """, window, cancellationToken);

        var failingTask = client.QueryAsync(applicationId, $$"""
            requests
            | where timestamp > ago({{r}})
            | summarize Total = count(), Failed = countif({{Failed}}) by operation_Name
            | where Failed > 0
            | top 5 by Failed desc
            """, window, cancellationToken);

        var slowestTask = client.QueryAsync(applicationId, $$"""
            requests
            | where timestamp > ago({{r}})
            | summarize Count = count(), P95 = percentile(duration, 95) by operation_Name
            | where Count >= 3
            | top 5 by P95 desc
            """, window, cancellationToken);

        var exceptionsTask = client.QueryAsync(applicationId, $$"""
            exceptions
            | where timestamp > ago({{r}})
            | summarize Count = count(), Message = take_any(iff(isempty(outerMessage), innermostMessage, outerMessage)) by type, problemId
            | top 5 by Count desc
            """, window, cancellationToken);

        await Task.WhenAll(kpisTask, seriesTask, failingTask, slowestTask, exceptionsTask);

        var k = kpisTask.Result.Rows.Count > 0 ? kpisTask.Result.Rows[0] : null;
        var kpis = new OverviewKpis(
            k?.GetLong("Requests") ?? 0,
            k?.GetLong("FailedRequests") ?? 0,
            k?.GetDouble("AvgDuration"),
            k?.GetDouble("P95Duration"),
            k?.GetLong("Exceptions") ?? 0,
            k?.GetLong("FailedDependencies") ?? 0,
            k?.GetLong("PreviousRequests") ?? 0,
            k?.GetLong("PreviousFailedRequests") ?? 0,
            k?.GetLong("PreviousExceptions") ?? 0);

        var series = seriesTask.Result.Rows
            .Select(row => new OverviewPoint(
                row.GetDateTime("timestamp") ?? DateTimeOffset.MinValue,
                row.GetLong("Requests") ?? 0,
                row.GetLong("FailedRequests") ?? 0,
                row.GetDouble("P95"),
                row.GetLong("Exceptions") ?? 0))
            .ToList();

        return new OverviewResponse(
            range,
            bin,
            kpis,
            series,
            failingTask.Result.Rows.Select(row => new FailingOperation(row.GetString("operation_Name") ?? "(unnamed)", row.GetLong("Failed") ?? 0, row.GetLong("Total") ?? 0)).ToList(),
            slowestTask.Result.Rows.Select(row => new SlowOperation(row.GetString("operation_Name") ?? "(unnamed)", row.GetLong("Count") ?? 0, row.GetDouble("P95") ?? 0)).ToList(),
            exceptionsTask.Result.Rows.Select(row => new ExceptionSummary(row.GetString("type") ?? "(unknown)", row.GetString("Message"), row.GetString("problemId"), row.GetLong("Count") ?? 0)).ToList());
    }

    /// <summary>Chart bucket size: roughly 50–100 points for any range.</summary>
    public static int BinMinutes(int rangeMinutes) => rangeMinutes switch
    {
        <= 60 => 1,
        <= 360 => 5,
        <= 1440 => 30,
        <= 4320 => 60,
        <= 10080 => 180,
        _ => 720,
    };

}
