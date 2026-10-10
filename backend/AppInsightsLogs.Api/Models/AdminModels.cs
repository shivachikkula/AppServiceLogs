namespace AppInsightsLogs.Api.Models;

public sealed record AdminGrant(int Id, string Email, string ApplicationName, string AppKey, DateTime CreatedAt);

public sealed record CreateGrantRequest(string? Email, string? ApplicationName, string? AppKey);

public sealed record AdminApplication(string AppKey, string ApplicationName, int Users);

public sealed record RenameApplicationRequest(string? ApplicationName);

/// <summary>Result of checking an application end to end: Key Vault secret, ApplicationId, and a test query.</summary>
public sealed record ApplicationCheck(bool Ok, string? ApplicationId, long? RecentItems, string Message);

public sealed record AuditEntryDto(
    long Id, DateTime Timestamp, string Email, string AppKey, string Action, string Text,
    int? RangeMinutes, bool Succeeded, int? RowCount, int? DurationMs, string? Error);

public sealed record DiagnosticCheck(string Name, string Status, string Detail);
