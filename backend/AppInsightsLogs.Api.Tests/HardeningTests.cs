using System.Net;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AppInsightsLogs.Api.Tests;

public class HardeningTests
{
    private static WebApplicationFactory<Program> CreateApp(string environment, Dictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static Dictionary<string, string?> DevelopmentSettings(int apiPerMinute = 600) => new()
    {
        ["AzureAdB2C:DevelopmentUserEmail"] = "jane@contoso.com",
        ["ConnectionStrings:LogViewerDb"] = "Server=localhost,1;Database=x;User Id=sa;Password=x",
        ["KeyVault:VaultUri"] = "https://vault.vault.azure.net/",
        ["RateLimits:ApiPerMinute"] = apiPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    [Fact]
    public async Task Responses_CarrySecurityHeaders_AndApiResponsesAreNotCached()
    {
        using var app = CreateApp(Environments.Development, DevelopmentSettings());
        using var client = app.CreateClient();

        var response = await client.GetAsync("/api/auth-config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task TooManyRequests_Returns429_WithRetryAfter()
    {
        using var app = CreateApp(Environments.Development, DevelopmentSettings(apiPerMinute: 2));
        using var client = app.CreateClient();

        var first = await client.GetAsync("/api/me");
        var second = await client.GetAsync("/api/me");
        var third = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.Contains("Too many requests", await third.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_SendsNoCorsHeaders_ForTheAngularDevServer()
    {
        var settings = DevelopmentSettings();
        settings.Remove("AzureAdB2C:DevelopmentUserEmail");
        settings["AzureAdB2C:Instance"] = "https://contoso.b2clogin.com";
        settings["AzureAdB2C:Domain"] = "contoso.onmicrosoft.com";
        settings["AzureAdB2C:SignUpSignInPolicyId"] = "B2C_1_signin";
        settings["AzureAdB2C:SpaClientId"] = "spa-id";
        using var app = CreateApp(Environments.Production, settings);
        using var client = app.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth-config");
        request.Headers.Add("Origin", "http://localhost:4200");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("https://contoso.b2clogin.com", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public void Production_WithMissingSettings_RefusesToStart()
    {
        using var app = CreateApp(Environments.Production, new Dictionary<string, string?>
        {
            ["AzureAdB2C:Instance"] = "https://contoso.b2clogin.com",
            ["AzureAdB2C:Domain"] = "contoso.onmicrosoft.com",
            ["AzureAdB2C:SignUpSignInPolicyId"] = "B2C_1_signin",
            ["AzureAdB2C:SpaClientId"] = "spa-id",
        });

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("ConnectionStrings:LogViewerDb is not set", ex.ToString());
        Assert.Contains("KeyVault:VaultUri is not set", ex.ToString());
    }

    [Theory]
    [InlineData("KeyVault:VaultUri", "http://vault.vault.azure.net/", "must be an absolute https:// URL")]
    [InlineData("KeyVault:Credential", "ManagedIdentiy", "Unknown credential")]
    [InlineData("AzureOpenAI:Endpoint", "https://openai.example/", "set both Endpoint and Deployment")]
    public void StartupValidation_ReportsInvalidSettings(string key, string value, string expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:LogViewerDb"] = "Server=x",
            ["KeyVault:VaultUri"] = "https://vault.vault.azure.net/",
            [key] = value,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var result = StartupValidation.Check(configuration, new FakeEnvironment());

        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ContentSecurityPolicy_WithoutB2C_AllowsNoFrames() =>
        Assert.Contains("frame-src 'none'", SecurityHeaders.BuildContentSecurityPolicy(new AzureAdB2COptions()));

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
