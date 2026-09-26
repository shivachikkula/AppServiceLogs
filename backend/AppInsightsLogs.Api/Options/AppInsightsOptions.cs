namespace AppInsightsLogs.Api.Options;

/// <summary>
/// Settings for reading telemetry from Application Insights.
/// The connection string identifies the resource (via its ApplicationId segment);
/// reading data is authorised with Microsoft Entra ID.
/// </summary>
public sealed class AppInsightsOptions
{
    public const string SectionName = "ApplicationInsights";

    /// <summary>The Application Insights connection string (Overview blade of the resource).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Optional override. Older connection strings do not contain an ApplicationId segment;
    /// in that case copy it from the resource's "API Access" blade.
    /// </summary>
    public string? ApplicationId { get; set; }

    /// <summary>
    /// Tenant that owns the Application Insights resource. Used by every credential type; set it when your
    /// account belongs to several tenants (the most common cause of token failures when running locally).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Which credential to use when no client secret is configured:
    /// Default (DefaultAzureCredential), AzureCli, VisualStudio or ManagedIdentity.
    /// </summary>
    public string Credential { get; set; } = "Default";

    /// <summary>Optional client id of a user-assigned managed identity.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Optional service principal (together with TenantId).</summary>
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Base URL of the Application Insights query API. Change for sovereign clouds.</summary>
    public string QueryEndpoint { get; set; } = "https://api.applicationinsights.io";

    /// <summary>Upper bound on rows returned by any single query.</summary>
    public int MaxRows { get; set; } = 500;

    public bool UsesServicePrincipal =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
