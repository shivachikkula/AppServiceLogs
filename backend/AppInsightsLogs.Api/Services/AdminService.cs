using System.Net;
using System.Net.Mail;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AppInsightsLogs.Api.Services;

/// <summary>Manages who may view which application (UserApplications) and reads the audit log.</summary>
public sealed class AdminService(LogViewerDbContext db, ApplicationCatalog catalog, AppInsightsQueryClient client)
{
    public async Task<IReadOnlyList<AdminGrant>> ListGrantsAsync(string? search, CancellationToken cancellationToken)
    {
        var query = db.UserApplications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(g => g.Email.Contains(term) || g.ApplicationName.Contains(term) || g.AppKey.Contains(term));
        }

        var grants = await query
            .OrderBy(g => g.Email).ThenBy(g => g.ApplicationName)
            .Take(2000)
            .ToListAsync(cancellationToken);
        return grants.Select(g => new AdminGrant(g.Id, g.Email, g.ApplicationName, g.AppKey, Utc(g.CreatedAt))).ToList();
    }

    public async Task<AdminGrant> CreateGrantAsync(CreateGrantRequest request, CancellationToken cancellationToken)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var appKey = (request.AppKey ?? string.Empty).Trim();
        var name = (request.ApplicationName ?? string.Empty).Trim();

        if (email.Length is 0 or > 256 || !MailAddress.TryCreate(email, out _))
        {
            throw Invalid("Enter a valid email address.");
        }
        if (!ApplicationCatalog.IsValidAppKey(appKey))
        {
            throw Invalid("The app key must be a Key Vault secret name: 1–127 letters, digits and dashes.");
        }

        // An existing application keeps its name; a new one needs a name.
        var existingName = await db.UserApplications.AsNoTracking()
            .Where(g => g.AppKey == appKey).Select(g => g.ApplicationName).FirstOrDefaultAsync(cancellationToken);
        name = existingName ?? name;
        if (name.Length is 0 or > 200)
        {
            throw Invalid("Enter an application name (up to 200 characters).");
        }

        if (await db.UserApplications.AnyAsync(g => g.Email == email && g.AppKey == appKey, cancellationToken))
        {
            throw new AppInsightsQueryException(HttpStatusCode.Conflict, $"{email} already has access to {name}.", "GrantExists");
        }

        var grant = new UserApplication { Email = email, ApplicationName = name, AppKey = appKey };
        db.UserApplications.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        return new AdminGrant(grant.Id, grant.Email, grant.ApplicationName, grant.AppKey, Utc(grant.CreatedAt));
    }

    public async Task<bool> DeleteGrantAsync(int id, CancellationToken cancellationToken)
    {
        var grant = await db.UserApplications.FindAsync([id], cancellationToken);
        if (grant is null)
        {
            return false;
        }
        db.UserApplications.Remove(grant);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AdminApplication>> ListApplicationsAsync(CancellationToken cancellationToken)
    {
        var applications = await db.UserApplications.AsNoTracking()
            .GroupBy(g => g.AppKey)
            .Select(g => new { AppKey = g.Key, ApplicationName = g.Max(x => x.ApplicationName), Users = g.Count() })
            .ToListAsync(cancellationToken);

        return applications
            .Select(a => new AdminApplication(a.AppKey, a.ApplicationName ?? a.AppKey, a.Users))
            .OrderBy(a => a.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Renames an application for every user who has access to it.</summary>
    public async Task<int> RenameApplicationAsync(string appKey, string? applicationName, CancellationToken cancellationToken)
    {
        var name = (applicationName ?? string.Empty).Trim();
        if (name.Length is 0 or > 200)
        {
            throw Invalid("Enter an application name (up to 200 characters).");
        }

        var grants = await db.UserApplications.Where(g => g.AppKey == appKey).ToListAsync(cancellationToken);
        foreach (var grant in grants)
        {
            grant.ApplicationName = name;
        }
        await db.SaveChangesAsync(cancellationToken);
        return grants.Count;
    }

    /// <summary>Reads the Key Vault secret again (bypassing the cache) and runs a small query against the application.</summary>
    public async Task<ApplicationCheck> CheckApplicationAsync(string appKey, CancellationToken cancellationToken)
    {
        if (!ApplicationCatalog.IsValidAppKey(appKey))
        {
            return new ApplicationCheck(false, null, null, "The app key is not a valid Key Vault secret name.");
        }

        string applicationId;
        try
        {
            applicationId = await catalog.ResolveApplicationIdAsync(appKey, appKey, bypassCache: true, cancellationToken);
        }
        catch (AppInsightsQueryException ex)
        {
            return new ApplicationCheck(false, null, null, ex.Message);
        }

        try
        {
            var table = await client.QueryAsync(
                applicationId,
                "union isfuzzy=true requests, traces, exceptions, dependencies | where timestamp > ago(1d) | count",
                TimeSpan.FromDays(1),
                cancellationToken);
            var count = table.Rows.Count > 0 ? table.Rows[0].GetLong("Count") : 0;
            return new ApplicationCheck(true, applicationId, count, $"OK: Key Vault secret found and Application Insights answered ({count:N0} items in the last 24 hours).");
        }
        catch (AppInsightsQueryException ex)
        {
            return new ApplicationCheck(false, applicationId, null, $"Key Vault secret found (ApplicationId {applicationId}), but querying Application Insights failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AuditEntryDto>> ListAuditAsync(
        string? email, string? appKey, bool failedOnly, long? beforeId, int take, CancellationToken cancellationToken)
    {
        var query = db.QueryAudit.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var term = email.Trim();
            query = query.Where(a => a.Email.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(appKey))
        {
            query = query.Where(a => a.AppKey == appKey.Trim());
        }
        if (failedOnly)
        {
            query = query.Where(a => !a.Succeeded);
        }
        if (beforeId is { } before)
        {
            query = query.Where(a => a.Id < before);
        }

        var entries = await query
            .OrderByDescending(a => a.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        // SQL Server returns DATETIME2 without a kind; the values are UTC, so say so in the JSON.
        return entries
            .Select(a => new AuditEntryDto(a.Id, Utc(a.Timestamp), a.Email, a.AppKey, a.Action, a.Text, a.RangeMinutes, a.Succeeded, a.RowCount, a.DurationMs, a.Error))
            .ToList();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static AppInsightsQueryException Invalid(string message) => new(HttpStatusCode.BadRequest, message, "InvalidRequest");
}
