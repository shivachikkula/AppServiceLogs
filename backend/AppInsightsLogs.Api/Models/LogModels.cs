namespace AppInsightsLogs.Api.Models;

public sealed record LogEntry(
    string ItemId,
    DateTimeOffset Timestamp,
    DateTimeOffset? IngestedAt,
    string ItemType,
    int SeverityLevel,
    string Message,
    string? RoleName,
    string? RoleInstance,
    string? OperationName,
    string? OperationId,
    string? ResultCode,
    double? DurationMs,
    string? CustomDimensions);

public sealed record LiveLogsResponse(
    IReadOnlyList<LogEntry> Items,
    /// <summary>Pass back as <c>since</c> on the next poll to receive only newly ingested items.</summary>
    DateTimeOffset? Cursor,
    DateTimeOffset ServerTime);

public sealed record ExceptionEntry(
    string ItemId,
    DateTimeOffset Timestamp,
    string? ProblemId,
    string? Type,
    string? Message,
    string? InnermostMessage,
    string? Method,
    string? Assembly,
    int SeverityLevel,
    string? RoleName,
    string? RoleInstance,
    string? OperationName,
    string? OperationId,
    string? ClientType);

public sealed record ExceptionDetail(
    ExceptionEntry Exception,
    string StackTrace,
    string? CustomDimensions,
    string? RawDetails);

public sealed record ExceptionGroup(
    string ProblemId,
    string? Type,
    string? Message,
    long Count,
    long AffectedOperations,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen);

public sealed record StatusResponse(
    bool Configured,
    string? ApplicationId,
    string AuthenticationMode,
    string QueryEndpoint);
