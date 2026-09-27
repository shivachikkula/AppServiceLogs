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

/// <summary>An application the signed-in user may view. The connection string never leaves the server.</summary>
public sealed record ApplicationSummary(string ApplicationName, string AppKey);

public sealed record UserInfo(string Email, string? Name);

/// <summary>Settings the Angular app needs to sign users in with Azure AD B2C (all public values).</summary>
public sealed record AuthConfigResponse(
    bool Enabled,
    string? ClientId,
    string? Authority,
    IReadOnlyList<string> KnownAuthorities,
    IReadOnlyList<string> Scopes);
