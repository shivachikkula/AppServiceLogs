using System.Net;
using System.Text;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppInsightsLogs.Api.Tests;

public class AdminServiceTests
{
    [Fact]
    public async Task CreateGrant_NormalizesEmail_AndReusesExistingApplicationName()
    {
        var (admin, db) = Create();

        var grant = await admin.CreateGrantAsync(new CreateGrantRequest("  Bob@Contoso.com ", "ignored", "orders-api"), CancellationToken.None);

        Assert.Equal(("bob@contoso.com", "Orders API", "orders-api"), (grant.Email, grant.ApplicationName, grant.AppKey));
        Assert.Equal(2, db.UserApplications.Count(g => g.AppKey == "orders-api"));
    }

    [Theory]
    [InlineData("not-an-email", "New App", "new-app", HttpStatusCode.BadRequest)]
    [InlineData("bob@contoso.com", "New App", "bad key!", HttpStatusCode.BadRequest)]
    [InlineData("bob@contoso.com", "", "new-app", HttpStatusCode.BadRequest)]
    [InlineData("jane@contoso.com", "Orders API", "orders-api", HttpStatusCode.Conflict)]
    public async Task CreateGrant_RejectsInvalidOrDuplicateGrants(string email, string name, string appKey, HttpStatusCode expected)
    {
        var (admin, _) = Create();

        var ex = await Assert.ThrowsAsync<AppInsightsQueryException>(() =>
            admin.CreateGrantAsync(new CreateGrantRequest(email, name, appKey), CancellationToken.None));

        Assert.Equal(expected, ex.StatusCode);
    }

    [Fact]
    public async Task RenameApplication_UpdatesEveryGrant_AndListGroupsByAppKey()
    {
        var (admin, _) = Create();
        await admin.CreateGrantAsync(new CreateGrantRequest("bob@contoso.com", null, "orders-api"), CancellationToken.None);

        var renamed = await admin.RenameApplicationAsync("orders-api", "Orders Service", CancellationToken.None);
        var apps = await admin.ListApplicationsAsync(CancellationToken.None);

        Assert.Equal(2, renamed);
        var orders = Assert.Single(apps, a => a.AppKey == "orders-api");
        Assert.Equal(("Orders Service", 2), (orders.ApplicationName, orders.Users));
    }

    [Fact]
    public async Task DeleteGrant_RemovesOnlyThatGrant()
    {
        var (admin, db) = Create();
        var id = db.UserApplications.Single(g => g.AppKey == "payments-api").Id;

        Assert.True(await admin.DeleteGrantAsync(id, CancellationToken.None));
        Assert.False(await admin.DeleteGrantAsync(id, CancellationToken.None));
        Assert.Single(db.UserApplications);
    }

    [Fact]
    public async Task CheckApplication_ReportsMissingSecret_AndSuccessfulQuery()
    {
        var (admin, _) = Create();

        var missing = await admin.CheckApplicationAsync("no-such-secret", CancellationToken.None);
        var ok = await admin.CheckApplicationAsync("orders-api", CancellationToken.None);

        Assert.False(missing.Ok);
        Assert.Contains("No Key Vault secret named 'no-such-secret'", missing.Message);
        Assert.True(ok.Ok);
        Assert.Equal(("11111111-0000-0000-0000-000000000001", 42L), (ok.ApplicationId, ok.RecentItems));
    }

    [Fact]
    public async Task ListAudit_FiltersAndPagesNewestFirst()
    {
        var (admin, db) = Create();
        for (var i = 0; i < 5; i++)
        {
            db.QueryAudit.Add(new QueryAuditEntry { Email = i % 2 == 0 ? "jane@contoso.com" : "bob@contoso.com", AppKey = "orders-api", Action = AuditActions.Query, Text = $"q{i}", Succeeded = i != 4 });
        }
        db.SaveChanges();

        var janes = await admin.ListAuditAsync("jane", null, false, null, 100, CancellationToken.None);
        var failed = await admin.ListAuditAsync(null, null, true, null, 100, CancellationToken.None);
        var page2 = await admin.ListAuditAsync(null, null, false, janes[0].Id, 2, CancellationToken.None);

        Assert.Equal(["q4", "q2", "q0"], janes.Select(a => a.Text));
        Assert.Equal("q4", Assert.Single(failed).Text);
        Assert.Equal(["q3", "q2"], page2.Select(a => a.Text));
    }

    [Theory]
    [InlineData("Admin@Contoso.com", true)]
    [InlineData("other@contoso.com", false)]
    [InlineData(null, false)]
    public void AdministrationOptions_MatchesEmailsCaseInsensitively(string? email, bool expected) =>
        Assert.Equal(expected, new AdministrationOptions { AdminEmails = ["admin@contoso.com"] }.IsAdmin(email));

    private static (AdminService Admin, LogViewerDbContext Db) Create()
    {
        var db = new LogViewerDbContext(new DbContextOptionsBuilder<LogViewerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.UserApplications.AddRange(
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "Orders API", AppKey = "orders-api" },
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "Payments API", AppKey = "payments-api" });
        db.SaveChanges();

        var catalog = new ApplicationCatalog(db, new Secrets(), new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions()));
        var client = new AppInsightsQueryClient(
            new HttpClient(new Handler("""{"tables":[{"name":"PrimaryResult","columns":[{"name":"Count","type":"long"}],"rows":[[42]]}]}""")),
            new Credential(), Microsoft.Extensions.Options.Options.Create(new AppInsightsOptions()), NullLogger<AppInsightsQueryClient>.Instance);
        return (new AdminService(db, catalog, client), db);
    }

    private sealed class Secrets : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(name == "orders-api" ? "InstrumentationKey=x;ApplicationId=11111111-0000-0000-0000-000000000001" : null);
    }

    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
