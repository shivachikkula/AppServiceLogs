namespace AppInsightsLogs.Api.Options;

public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    /// <summary>e.g. https://my-vault.vault.azure.net/</summary>
    public string? VaultUri { get; set; }

    /// <summary>How long connection strings read from Key Vault are cached in memory.</summary>
    public int CacheMinutes { get; set; } = 10;
}
