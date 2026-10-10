using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AppInsightsLogs.Api.Tests;

public class CredentialFactoryTests
{
    private static readonly Dictionary<string, string> AppService = new() { ["WEBSITE_INSTANCE_ID"] = "abc", ["IDENTITY_ENDPOINT"] = "http://localhost:42356/msi/token" };
    private static readonly Dictionary<string, string> Laptop = new();

    [Fact]
    public void InAzure_KeyVaultAndDefault_UseTheSystemAssignedManagedIdentity()
    {
        var credentials = Create(new AppInsightsOptions(), new KeyVaultOptions(), AppService);

        Assert.IsType<ManagedIdentityCredential>(credentials.KeyVault);
        Assert.Equal("managed identity (system-assigned)", credentials.KeyVaultDescription);
        Assert.IsType<ManagedIdentityCredential>(credentials.Default);
    }

    [Fact]
    public void Locally_KeyVaultUsesTheSameDeveloperSignIn()
    {
        var credentials = Create(new AppInsightsOptions(), new KeyVaultOptions(), Laptop);

        Assert.IsType<DefaultAzureCredential>(credentials.KeyVault);
        Assert.Same(credentials.Default, credentials.KeyVault);
        Assert.StartsWith("developer sign-in", credentials.KeyVaultDescription);
    }

    [Fact]
    public void InAzure_WithServicePrincipal_KeyVaultStillUsesTheManagedIdentity()
    {
        var servicePrincipal = new AppInsightsOptions { TenantId = "t", ClientId = "sp-client", ClientSecret = "secret" };

        var credentials = Create(servicePrincipal, new KeyVaultOptions(), AppService);

        Assert.IsType<ClientSecretCredential>(credentials.Default);
        Assert.IsType<ManagedIdentityCredential>(credentials.KeyVault);
    }

    [Fact]
    public void UserAssignedIdentity_ForKeyVault_IsUsed()
    {
        var credentials = Create(new AppInsightsOptions(), new KeyVaultOptions { ManagedIdentityClientId = "kv-identity" }, AppService);

        Assert.Equal("managed identity (user-assigned kv-identity)", credentials.KeyVaultDescription);
        Assert.Equal("managed identity (system-assigned)", credentials.DefaultDescription);
    }

    [Fact]
    public void UserAssignedIdentity_FallsBackToAzureClientIdVariable()
    {
        var env = new Dictionary<string, string>(AppService) { ["AZURE_CLIENT_ID"] = "from-env" };

        var credentials = Create(new AppInsightsOptions(), new KeyVaultOptions(), env);

        Assert.Equal("managed identity (user-assigned from-env)", credentials.KeyVaultDescription);
    }

    [Fact]
    public void ExplicitKeyVaultCredential_OverridesAuto()
    {
        var credentials = Create(new AppInsightsOptions(), new KeyVaultOptions { Credential = "AzureCli" }, AppService);

        Assert.IsType<AzureCliCredential>(credentials.KeyVault);
    }

    [Theory]
    [InlineData("CONTAINER_APP_NAME")]
    [InlineData("MSI_ENDPOINT")]
    [InlineData("WEBSITE_INSTANCE_ID")]
    public void IsRunningInAzure_RecognisesAzureHosts(string variable) =>
        Assert.True(CredentialFactory.IsRunningInAzure(name => name == variable ? "x" : null));

    [Fact]
    public void IsRunningInAzure_IsFalseOnADeveloperMachine() =>
        Assert.False(CredentialFactory.IsRunningInAzure(_ => null));

    private static AzureCredentials Create(AppInsightsOptions options, KeyVaultOptions keyVault, Dictionary<string, string> env) =>
        CredentialFactory.Create(options, keyVault, new FakeEnvironment(), name => env.GetValueOrDefault(name));

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public class CredentialNameTests
{
    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("Default", "Auto")]
    [InlineData("auto", "Auto")]
    [InlineData("MSI", "ManagedIdentity")]
    [InlineData("az", "AzureCli")]
    [InlineData("DefaultAzureCredential", "DefaultAzureCredential")]
    public void Normalize_AcceptsKnownNames(string? input, string expected) =>
        Assert.Equal(expected, CredentialFactory.Normalize(input));

    [Fact]
    public void Normalize_RejectsTypos() =>
        Assert.Throws<InvalidOperationException>(() => CredentialFactory.Normalize("ManagedIdentiy"));
}
