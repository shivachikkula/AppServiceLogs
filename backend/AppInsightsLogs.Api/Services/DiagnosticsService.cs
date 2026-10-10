using Azure.Core;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

/// <summary>Go-live checks for the admin page: is each dependency configured and reachable with the API's identity?</summary>
public sealed class DiagnosticsService(
    LogViewerDbContext db,
    ISecretProvider secrets,
    AzureCredentials credentials,
    KqlAssistant assistant,
    IOptions<AzureAdB2COptions> b2c,
    IOptions<KeyVaultOptions> keyVault,
    IOptions<AppInsightsOptions> appInsights,
    IOptions<AdministrationOptions> administration,
    IHostEnvironment environment)
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Error = "error";

    public async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(CancellationToken cancellationToken)
    {
        var checks = new List<DiagnosticCheck>
        {
            CheckB2C(),
            CheckAdmins(),
        };
        checks.AddRange(await CheckDatabaseAsync(cancellationToken));
        checks.Add(await CheckKeyVaultAsync(cancellationToken));
        checks.Add(await CheckAppInsightsTokenAsync(cancellationToken));
        checks.Add(assistant.IsEnabled
            ? new DiagnosticCheck("AI query assistant", Ok, "Azure OpenAI is configured. Use the Query page to try it.")
            : new DiagnosticCheck("AI query assistant", Warning, "Not configured (optional). Set AzureOpenAI:Endpoint and Deployment to enable it."));
        return checks;
    }

    private DiagnosticCheck CheckB2C()
    {
        var options = b2c.Value;
        if (!options.IsConfigured)
        {
            return environment.IsDevelopment()
                ? new DiagnosticCheck("Azure AD B2C sign-in", Warning, $"Not configured: running as the Development user {options.DevelopmentUserEmail}.")
                : new DiagnosticCheck("Azure AD B2C sign-in", Error, "Not configured.");
        }
        return new DiagnosticCheck("Azure AD B2C sign-in", Ok,
            $"Tokens from {options.MetadataAuthority}, audiences: {string.Join(", ", options.ValidAudiences)}.");
    }

    private DiagnosticCheck CheckAdmins() =>
        new("Administrators", Ok, $"{administration.Value.AdminEmails.Length} admin email(s) in Administration:AdminEmails.");

    private async Task<IReadOnlyList<DiagnosticCheck>> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var grants = await db.UserApplications.CountAsync(cancellationToken);
            var apps = await db.UserApplications.Select(g => g.AppKey).Distinct().CountAsync(cancellationToken);
            var result = new List<DiagnosticCheck>
            {
                new("Database: UserApplications", grants > 0 ? Ok : Warning,
                    grants > 0 ? $"{grants} grant(s) for {apps} application(s)." : "Reachable, but no users have been granted access yet. Use the Access tab."),
            };
            try
            {
                await db.QueryAudit.AnyAsync(cancellationToken);
                result.Add(new DiagnosticCheck("Database: QueryAudit", Ok, "Audit table exists."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(new DiagnosticCheck("Database: QueryAudit", Error, "The QueryAudit table is missing: run database/schema.sql. " + AppInsightsQueryClient.FlattenMessages(ex)));
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [new DiagnosticCheck("Database", Error, "Cannot read the UserApplications table: " + AppInsightsQueryClient.FlattenMessages(ex))];
        }
    }

    private async Task<DiagnosticCheck> CheckKeyVaultAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyVault.Value.VaultUri))
        {
            return new DiagnosticCheck("Key Vault", Error, "KeyVault:VaultUri is not set.");
        }
        try
        {
            // A secret that does not exist: "not found" proves the vault is reachable and the identity may read secrets.
            await secrets.GetSecretAsync("osse-log-viewer-diagnostics-probe", cancellationToken);
            return new DiagnosticCheck("Key Vault", Ok,
                $"{keyVault.Value.VaultUri} is reachable and secrets can be read, signed in as the {credentials.KeyVaultDescription}. Use 'Check' on the Applications tab for each app.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck("Key Vault", Error,
                $"Signed in as the {credentials.KeyVaultDescription}: {AppInsightsQueryClient.FlattenMessages(ex)}" +
                (credentials.KeyVaultDescription.StartsWith("managed identity", StringComparison.Ordinal)
                    ? " | Make sure the identity is enabled on the App Service (Identity blade) and has the 'Key Vault Secrets User' role on the vault."
                    : string.Empty));
        }
    }

    private async Task<DiagnosticCheck> CheckAppInsightsTokenAsync(CancellationToken cancellationToken)
    {
        var options = appInsights.Value;
        try
        {
            await credentials.Default.GetTokenAsync(new TokenRequestContext([$"{options.QueryEndpoint.TrimEnd('/')}/.default"]), cancellationToken);
            return new DiagnosticCheck("Azure identity", Ok,
                $"Got a token for Application Insights as the {credentials.DefaultDescription}. Use 'Check' on the Applications tab to confirm Monitoring Reader on each resource.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck("Azure identity", Error, $"{credentials.DefaultDescription}: " + AppInsightsQueryClient.FlattenMessages(ex));
        }
    }
}
