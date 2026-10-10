namespace AppInsightsLogs.Api.Models;

public sealed record OverviewKpis(
    long Requests,
    long FailedRequests,
    double? AvgDurationMs,
    double? P95DurationMs,
    long Exceptions,
    long FailedDependencies,
    long PreviousRequests,
    long PreviousFailedRequests,
    long PreviousExceptions);

public sealed record OverviewPoint(DateTimeOffset Timestamp, long Requests, long FailedRequests, double? P95DurationMs, long Exceptions);

public sealed record FailingOperation(string Operation, long Failed, long Total);

public sealed record SlowOperation(string Operation, long Count, double P95DurationMs);

public sealed record ExceptionSummary(string Type, string? Message, string? ProblemId, long Count);

public sealed record OverviewResponse(
    int RangeMinutes,
    int BinMinutes,
    OverviewKpis Kpis,
    IReadOnlyList<OverviewPoint> Series,
    IReadOnlyList<FailingOperation> TopFailing,
    IReadOnlyList<SlowOperation> Slowest,
    IReadOnlyList<ExceptionSummary> TopExceptions);

/// <summary>One item of a distributed operation: a request or dependency (a span with a duration) or a trace, exception or event.</summary>
public sealed record OperationItem(
    string ItemId,
    string Kind,
    DateTimeOffset Timestamp,
    string? Id,
    string? ParentId,
    string Name,
    string? Detail,
    string? Target,
    string? DependencyType,
    string? ResultCode,
    bool? Success,
    double? DurationMs,
    int? SeverityLevel,
    string? RoleName);

public sealed record OperationTimeline(string OperationId, IReadOnlyList<OperationItem> Items, bool Truncated);
