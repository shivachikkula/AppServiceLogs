using System.Net;
using System.Text.RegularExpressions;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

public sealed record ResolvedApplication(string ApplicationName, string AppKey, string ApplicationId);

/// <summary>
/// Maps signed-in users to the applications they may view (UserApplications table) and resolves an application's
/// Application Insights id from the connection string stored in Key Vault under its app key.
/// </summary>
public sealed partial class ApplicationCatalog(
    LogViewerDbContext db,
    ISecretProvider secrets,
    IMemoryCache cache,
    IOptions<KeyVaultOptions> keyVaultOptions)
{
    public Task<IReadOnlyList<ApplicationSummary>> ListForUserAsync(string email, CancellationToken cancellationToken) =>
        QueryDatabaseAsync<IReadOnlyList<ApplicationSummary>>(async () => await db.UserApplications
            .AsNoTracking()
            .Where(a => a.Email == email)
            .OrderBy(a => a.ApplicationName)
            .Select(a => new ApplicationSummary(a.ApplicationName, a.AppKey))
            .ToListAsync(cancellationToken));

    /// <summary>
    /// Verifies that <paramref name="email"/> has been granted <paramref name="appKey"/> and returns the application's
    /// Application Insights id. Throws <see cref="AppInsightsQueryException"/> with 403 when the user has no access.
    /// </summary>
    public async Task<ResolvedApplication> ResolveAsync(string email, string? appKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appKey))
        {
            throw new AppInsightsQueryException(HttpStatusCode.BadRequest, "Select an application first (appKey is required).", "AppKeyRequired");
        }

        if (!SecretNamePattern().IsMatch(appKey))
        {
            throw new AppInsightsQueryException(HttpStatusCode.BadRequest, "The app key is not a valid Key Vault secret name.", "InvalidAppKey");
        }

        var grant = await QueryDatabaseAsync(() => db.UserApplications
            .AsNoTracking()
            .Where(a => a.Email == email && a.AppKey == appKey)
            .Select(a => new { a.ApplicationName, a.AppKey })
            .FirstOrDefaultAsync(cancellationToken))
            ?? throw new AppInsightsQueryException(HttpStatusCode.Forbidden, "You do not have access to this application.", "ApplicationForbidden");

        var applicationId = await cache.GetOrCreateAsync($"appinsights-appid:{grant.AppKey}", async entry =>
        {
            var connectionString = await secrets.GetSecretAsync(grant.AppKey, cancellationToken)
                ?? throw new AppInsightsQueryException(
                    HttpStatusCode.NotFound,
                    $"No Key Vault secret named '{grant.AppKey}' was found for application '{grant.ApplicationName}'.",
                    "SecretNotFound");

            var id = ConnectionStringParser.ResolveApplicationId(connectionString, null)
                ?? throw new AppInsightsQueryException(
                    HttpStatusCode.UnprocessableEntity,
                    $"The connection string for '{grant.ApplicationName}' has no ApplicationId segment. Store the full connection string from the Application Insights Overview page.",
                    "ApplicationIdMissing");

            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, keyVaultOptions.Value.CacheMinutes));
            return id;
        });

        return new ResolvedApplication(grant.ApplicationName, grant.AppKey, applicationId!);
    }

    private static async Task<T> QueryDatabaseAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return await query();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException or DbUpdateException)
        {
            throw new AppInsightsQueryException(
                HttpStatusCode.ServiceUnavailable,
                "Could not read the UserApplications table. Check ConnectionStrings:LogViewerDb and that database/schema.sql has been run. " +
                AppInsightsQueryClient.FlattenMessages(ex),
                "DatabaseUnavailable");
        }
    }

    // Key Vault secret names: 1-127 characters, letters, digits and dashes.
    [GeneratedRegex("^[0-9a-zA-Z-]{1,127}$")]
    private static partial Regex SecretNamePattern();
}
