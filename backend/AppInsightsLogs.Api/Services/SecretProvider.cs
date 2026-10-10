using System.Net;
using AppInsightsLogs.Api.Options;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

public interface ISecretProvider
{
    /// <summary>Returns the secret's value, or null when the secret does not exist.</summary>
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Reads secrets from Azure Key Vault with the API's own identity (needs the "Key Vault Secrets User" role).</summary>
public sealed class KeyVaultSecretProvider(TokenCredential credential, IOptions<KeyVaultOptions> options, ILogger<KeyVaultSecretProvider> logger)
    : ISecretProvider
{
    private readonly Lazy<SecretClient?> _client = new(() =>
        string.IsNullOrWhiteSpace(options.Value.VaultUri) ? null : new SecretClient(new Uri(options.Value.VaultUri), credential));

    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken)
    {
        var client = _client.Value ?? throw new AppInsightsQueryException(
            HttpStatusCode.ServiceUnavailable,
            "Key Vault is not configured. Set KeyVault:VaultUri.",
            "KeyVaultNotConfigured");

        try
        {
            var secret = await client.GetSecretAsync(name, cancellationToken: cancellationToken);
            return secret.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Access to Key Vault secret {SecretName} was denied", name);
            throw new AppInsightsQueryException(
                HttpStatusCode.BadGateway,
                "The API's identity is not allowed to read secrets from Key Vault. Grant it the 'Key Vault Secrets User' role on the vault.",
                "KeyVaultForbidden");
        }
        catch (Exception ex) when (ex is Azure.Identity.AuthenticationFailedException or Azure.Identity.CredentialUnavailableException)
        {
            logger.LogError(ex, "Failed to authenticate to Key Vault");
            throw new AppInsightsQueryException(
                HttpStatusCode.BadGateway,
                "Could not acquire an Azure access token for Key Vault. " + AppInsightsQueryClient.FlattenMessages(ex),
                "AuthenticationFailed");
        }
    }
}
