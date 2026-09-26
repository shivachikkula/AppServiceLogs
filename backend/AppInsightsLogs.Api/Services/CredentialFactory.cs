using AppInsightsLogs.Api.Options;
using Azure.Core;
using Azure.Identity;

namespace AppInsightsLogs.Api.Services;

public static class CredentialFactory
{
    /// <summary>Returns a short name for the credential that <see cref="Create"/> will build.</summary>
    public static string Describe(AppInsightsOptions options) =>
        options.UsesServicePrincipal ? "ServicePrincipal" : Normalize(options.Credential);

    public static TokenCredential Create(AppInsightsOptions options, IHostEnvironment environment)
    {
        var tenantId = string.IsNullOrWhiteSpace(options.TenantId) ? null : options.TenantId.Trim();
        var managedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
            : options.ManagedIdentityClientId.Trim();

        if (options.UsesServicePrincipal)
        {
            return new ClientSecretCredential(tenantId, options.ClientId, options.ClientSecret);
        }

        return Normalize(options.Credential) switch
        {
            "AzureCli" => new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),
            "VisualStudio" => new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = tenantId }),
            "ManagedIdentity" => new ManagedIdentityCredential(string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId)),
            _ => new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                TenantId = tenantId,
                ManagedIdentityClientId = managedIdentityClientId,
                // There is no managed identity on a developer machine; probing for one only adds delay and noise.
                ExcludeManagedIdentityCredential = environment.IsDevelopment(),
            }),
        };
    }

    private static string Normalize(string? credential) => credential?.Trim().ToLowerInvariant() switch
    {
        "azurecli" or "cli" or "az" => "AzureCli",
        "visualstudio" or "vs" => "VisualStudio",
        "managedidentity" or "msi" => "ManagedIdentity",
        _ => "Default",
    };
}
