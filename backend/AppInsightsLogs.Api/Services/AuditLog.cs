using AppInsightsLogs.Api.Data;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Records user-written queries and AI requests in the QueryAudit table. A failure to write the audit row is logged
/// but does not fail the user's request.
/// </summary>
public sealed class AuditLog(LogViewerDbContext db, ILogger<AuditLog> logger)
{
    private const int MaxErrorLength = 2000;

    public async Task RecordAsync(QueryAuditEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Error is { Length: > MaxErrorLength })
        {
            entry.Error = entry.Error[..MaxErrorLength];
        }

        try
        {
            db.QueryAudit.Add(entry);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            db.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            logger.LogError(ex, "Could not write the query audit entry for {Email} on {AppKey} ({Action})", entry.Email, entry.AppKey, entry.Action);
        }
    }
}
