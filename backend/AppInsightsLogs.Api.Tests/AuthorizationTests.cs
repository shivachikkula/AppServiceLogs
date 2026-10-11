using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AppInsightsLogs.Api.Tests;

/// <summary>
/// End-to-end authorization through the real pipeline with Azure AD B2C enabled: tokens are signed with a test key
/// that replaces the B2C metadata, the database is in memory, and Key Vault / Application Insights are faked.
/// </summary>
public sealed class AuthorizationTests : IDisposable
{
    private const string Issuer = "https://test-b2c.example/tenant/v2.0/";
    private const string ApiClientId = "api-client-id";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("unit-test-signing-key-0123456789-abcdef"));
    private static readonly SymmetricSecurityKey OtherKey = new(Encoding.UTF8.GetBytes("some-other-signing-key-0123456789-xyz"));

    private readonly WebApplicationFactory<Program> _app;
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public AuthorizationTests()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("AzureAdB2C:Instance", "https://test-b2c.example");
            builder.UseSetting("AzureAdB2C:Domain", "tenant");
            builder.UseSetting("AzureAdB2C:SignUpSignInPolicyId", "B2C_1_signin");
            builder.UseSetting("AzureAdB2C:SpaClientId", "spa-client-id");
            builder.UseSetting("AzureAdB2C:ApiClientId", ApiClientId);
            builder.UseSetting("AzureAdB2C:RequiredScope", "access_as_user");
            builder.UseSetting("Administration:AdminEmails:0", "admin@contoso.com");
            builder.UseSetting("ConnectionStrings:LogViewerDb", "Server=unused");
            builder.UseSetting("KeyVault:VaultUri", "https://vault.vault.azure.net/");

            builder.ConfigureTestServices(services =>
            {
                // Validate test tokens instead of downloading B2C metadata.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(SigningKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                });

                services.RemoveAll<DbContextOptions<LogViewerDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<LogViewerDbContext>>();
                services.AddDbContext<LogViewerDbContext>(o => o.UseInMemoryDatabase(_databaseName));

                services.AddSingleton<ISecretProvider>(new Secrets());
                services.AddSingleton<TokenCredential>(new Credential());
                services.AddHttpClient<AppInsightsQueryClient>().ConfigurePrimaryHttpMessageHandler(() => new EmptyResults());
            });
        });

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LogViewerDbContext>();
        db.UserApplications.AddRange(
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "Orders API", AppKey = "orders-api" },
            new UserApplication { Email = "bob@contoso.com", ApplicationName = "HR Portal", AppKey = "hr-portal" });
        db.SaveChanges();
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task NoToken_Is401()
    {
        var response = await _app.CreateClient().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    public async Task InvalidTokens_Are401_WithAReason(string problem)
    {
        var token = problem switch
        {
            "wrong-key" => Token("jane@contoso.com", key: OtherKey),
            "wrong-audience" => Token("jane@contoso.com", audience: "someone-else"),
            "expired" => Token("jane@contoso.com", expires: DateTime.UtcNow.AddMinutes(-10)),
            _ => Token("jane@contoso.com", issuer: "https://evil.example/v2.0/"),
        };

        var response = await Client(token).GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutRequiredScope_Is403()
    {
        var response = await Client(Token("jane@contoso.com", scope: "something_else")).GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_ReturnsTheUser_AndOnlyTheirApplications()
    {
        var client = Client(Token("Jane@Contoso.com"));

        var me = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/me");
        var apps = await client.GetStringAsync("/api/applications");

        Assert.Equal("jane@contoso.com", me!["email"].ToString());
        Assert.Contains("orders-api", apps);
        Assert.DoesNotContain("hr-portal", apps);
    }

    [Fact]
    public async Task AnotherUsersApplication_Is403_AndOwnApplicationWorks()
    {
        var client = Client(Token("jane@contoso.com"));

        var other = await client.GetAsync("/api/logs/live?appKey=hr-portal");
        var own = await client.GetAsync("/api/logs/live?appKey=orders-api");

        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_RequireAnAdminEmail_AndAdminChangesAreAudited()
    {
        var user = Client(Token("jane@contoso.com"));
        var admin = Client(Token("admin@contoso.com"));

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/grants")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/grants")).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/admin/grants", new { email = "amy@contoso.com", applicationName = "", appKey = "orders-api" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var scope = _app.Services.CreateScope();
        var entry = Assert.Single(scope.ServiceProvider.GetRequiredService<LogViewerDbContext>().QueryAudit);
        Assert.Equal(("admin@contoso.com", AuditActions.AdminGrant, "orders-api"), (entry.Email, entry.Action, entry.AppKey));
        Assert.Contains("amy@contoso.com", entry.Text);
    }

    private HttpClient Client(string token)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(
        string email,
        string audience = ApiClientId,
        string issuer = Issuer,
        string scope = "access_as_user",
        DateTime? expires = null,
        SymmetricSecurityKey? key = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            NotBefore = (expires ?? DateTime.UtcNow.AddMinutes(30)).AddHours(-1),
            IssuedAt = (expires ?? DateTime.UtcNow.AddMinutes(30)).AddHours(-1),
            Subject = new ClaimsIdentity([new Claim("email", email), new Claim("scp", scope), new Claim("name", "Test user")]),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });

    private sealed class Secrets : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("InstrumentationKey=x;ApplicationId=11111111-0000-0000-0000-000000000001");
    }

    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class EmptyResults : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tables":[{"name":"PrimaryResult","columns":[],"rows":[]}]}""", Encoding.UTF8, "application/json"),
            });
    }
}
