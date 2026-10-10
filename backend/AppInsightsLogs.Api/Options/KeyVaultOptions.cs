namespace AppInsightsLogs.Api.Options;

public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    /// <summary>e.g. https://my-vault.vault.azure.net/</summary>
    public string? VaultUri { get; set; }

    /// <summary>
    /// How the API signs in to Key Vault. Auto (default): the managed identity when running in Azure, the developer
    /// sign-in elsewhere. Also: ManagedIdentity, AzureCli, VisualStudio, DefaultAzureCredential.
    /// </summary>
    public string Credential { get; set; } = "Auto";

    /// <summary>Client id of a user-assigned managed identity for Key Vault. Empty: system-assigned (or ApplicationInsights:ManagedIdentityClientId).</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>How long connection strings read from Key Vault are cached in memory.</summary>
    public int CacheMinutes { get; set; } = 10;
}
