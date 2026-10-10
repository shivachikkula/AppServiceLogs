using AppInsightsLogs.Api.Options;
using Azure.Core;
using Azure.Identity;

namespace AppInsightsLogs.Api.Services;

/// <summary>The Azure identities the API uses, with a readable description of each for diagnostics.</summary>
public sealed record AzureCredentials(
    TokenCredential Default,
    string DefaultDescription,
    TokenCredential KeyVault,
    string KeyVaultDescription)
{
    public const string KeyVaultKey = "KeyVault";
}

/// <summary>
/// Chooses how the API signs in to Azure.
/// <list type="bullet">
/// <item><b>Auto</b> (default): the App Service / Functions / Container Apps <b>managed identity</b> when running in Azure;
/// a developer sign-in (Azure CLI, Visual Studio, azd, environment variables) when running anywhere else.</item>
/// <item>AzureCli, VisualStudio, ManagedIdentity: exactly that credential.</item>
/// <item>DefaultAzureCredential: the full DefaultAzureCredential chain (the previous behaviour). "Default" means Auto.</item>
/// </list>
/// A service principal (TenantId/ClientId/ClientSecret) replaces Auto for Application Insights and Azure OpenAI.
/// Key Vault always uses the managed identity in Azure unless KeyVault:Credential says otherwise.
/// </summary>
public static class CredentialFactory
{
    private static readonly string[] AzureHostVariables = ["IDENTITY_ENDPOINT", "MSI_ENDPOINT", "WEBSITE_INSTANCE_ID", "CONTAINER_APP_NAME"];

    public static AzureCredentials Create(
        AppInsightsOptions options,
        KeyVaultOptions keyVault,
        IHostEnvironment environment,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var env = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var inAzure = IsRunningInAzure(env);
        var tenantId = Trimmed(options.TenantId);
        var defaultIdentityClientId = Trimmed(options.ManagedIdentityClientId) ?? Trimmed(env("AZURE_CLIENT_ID"));

        var (defaultCredential, defaultDescription) = Build(
            Normalize(options.Credential),
            inAzure,
            tenantId,
            defaultIdentityClientId,
            options.UsesServicePrincipal ? options : null,
            environment);

        var keyVaultKind = Normalize(keyVault.Credential);
        var keyVaultIdentityClientId = Trimmed(keyVault.ManagedIdentityClientId) ?? defaultIdentityClientId;
        var (keyVaultCredential, keyVaultDescription) = keyVaultKind == "Auto" && !inAzure
            ? (defaultCredential, defaultDescription) // locally: the same developer sign-in (or service principal)
            : Build(keyVaultKind, inAzure, tenantId, keyVaultIdentityClientId, servicePrincipal: null, environment);

        return new AzureCredentials(defaultCredential, defaultDescription, keyVaultCredential, keyVaultDescription);
    }

    /// <summary>
    /// True on Azure App Service, Azure Functions and Azure Container Apps (they set these variables, and expose a
    /// managed identity endpoint when an identity is enabled).
    /// </summary>
    public static bool IsRunningInAzure(Func<string, string?> env) =>
        AzureHostVariables.Any(name => !string.IsNullOrWhiteSpace(env(name)));

    private static (TokenCredential Credential, string Description) Build(
        string kind,
        bool inAzure,
        string? tenantId,
        string? managedIdentityClientId,
        AppInsightsOptions? servicePrincipal,
        IHostEnvironment environment)
    {
        if (servicePrincipal is not null && kind is "Auto" or "DefaultAzureCredential")
        {
            return (new ClientSecretCredential(tenantId, servicePrincipal.ClientId, servicePrincipal.ClientSecret),
                $"service principal {servicePrincipal.ClientId}");
        }

        return kind switch
        {
            "ManagedIdentity" => ManagedIdentity(managedIdentityClientId),
            "Auto" when inAzure => ManagedIdentity(managedIdentityClientId),
            "Auto" => (new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                TenantId = tenantId,
                // Not in Azure: there is no managed identity to find, and probing for one only adds delay.
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
            }), "developer sign-in (Azure CLI, Visual Studio, azd or environment variables)"),
            "AzureCli" => (new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }), "Azure CLI sign-in"),
            "VisualStudio" => (new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = tenantId }), "Visual Studio sign-in"),
            _ /* DefaultAzureCredential */ => (new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                TenantId = tenantId,
                ManagedIdentityClientId = managedIdentityClientId,
                ExcludeManagedIdentityCredential = environment.IsDevelopment(),
            }), "DefaultAzureCredential"),
        };
    }

    private static (TokenCredential, string) ManagedIdentity(string? clientId) =>
        string.IsNullOrWhiteSpace(clientId)
            ? (new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned), "managed identity (system-assigned)")
            : (new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId)), $"managed identity (user-assigned {clientId})");

    internal static string Normalize(string? credential) => credential?.Trim().ToLowerInvariant() switch
    {
        // "Default" was the previous default value in appsettings; it now means Auto as well.
        null or "" or "auto" or "default" => "Auto",
        "defaultazurecredential" => "DefaultAzureCredential",
        "azurecli" or "cli" or "az" => "AzureCli",
        "visualstudio" or "vs" => "VisualStudio",
        "managedidentity" or "msi" => "ManagedIdentity",
        var other => throw new InvalidOperationException(
            $"Unknown credential '{other}'. Use Auto, ManagedIdentity, AzureCli, VisualStudio or DefaultAzureCredential."),
    };

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
