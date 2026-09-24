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

    /// <summary>Optional service principal. When empty, DefaultAzureCredential is used (managed identity, az login, VS, ...).</summary>
    public string? TenantId { get; set; }
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
