using System.Net;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AppInsightsLogs.Api.Tests;

public class ApplicationCatalogTests
{
    [Fact]
    public async Task ListForUser_ReturnsOnlyTheUsersApplications_SortedByName()
    {
        var (catalog, _) = Create();

        var apps = await catalog.ListForUserAsync("jane@contoso.com", CancellationToken.None);

        Assert.Equal(["Broken", "No app id", "Orders API", "Payments API"], apps.Select(a => a.ApplicationName));
        Assert.DoesNotContain(apps, a => a.AppKey == "hr-portal");
    }

    [Fact]
    public async Task Resolve_ReadsApplicationIdFromKeyVaultSecret_AndCachesIt()
    {
        var (catalog, secrets) = Create();

        var first = await catalog.ResolveAsync("jane@contoso.com", "orders-api", CancellationToken.None);
        var second = await catalog.ResolveAsync("jane@contoso.com", "orders-api", CancellationToken.None);

        Assert.Equal("Orders API", first.ApplicationName);
        Assert.Equal("11111111-0000-0000-0000-000000000001", first.ApplicationId);
        Assert.Equal(first, second);
        Assert.Equal(1, secrets.Calls);
    }

    [Fact]
    public async Task Resolve_AppKeyGrantedToAnotherUser_IsForbidden_AndDoesNotReadKeyVault()
    {
        var (catalog, secrets) = Create();

        var ex = await Assert.ThrowsAsync<AppInsightsQueryException>(() =>
            catalog.ResolveAsync("jane@contoso.com", "hr-portal", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal(0, secrets.Calls);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("../../secrets", HttpStatusCode.BadRequest)]
    [InlineData("missing-secret", HttpStatusCode.NotFound)]
    [InlineData("no-app-id", HttpStatusCode.UnprocessableEntity)]
    public async Task Resolve_InvalidOrBrokenApplications_AreRejected(string? appKey, HttpStatusCode expected)
    {
        var (catalog, _) = Create();

        var ex = await Assert.ThrowsAsync<AppInsightsQueryException>(() =>
            catalog.ResolveAsync("jane@contoso.com", appKey, CancellationToken.None));

        Assert.Equal(expected, ex.StatusCode);
    }

    private static (ApplicationCatalog Catalog, FakeSecrets Secrets) Create()
    {
        var db = new LogViewerDbContext(new DbContextOptionsBuilder<LogViewerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.UserApplications.AddRange(
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "Payments API", AppKey = "payments-api" },
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "Orders API", AppKey = "orders-api" },
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "Broken", AppKey = "missing-secret" },
            new UserApplication { Email = "jane@contoso.com", ApplicationName = "No app id", AppKey = "no-app-id" },
            new UserApplication { Email = "bob@contoso.com", ApplicationName = "HR Portal", AppKey = "hr-portal" });
        db.SaveChanges();

        var secrets = new FakeSecrets();
        var catalog = new ApplicationCatalog(db, secrets, new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions()));
        return (catalog, secrets);
    }

    private sealed class FakeSecrets : ISecretProvider
    {
        public int Calls { get; private set; }

        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(name switch
            {
                "orders-api" => "InstrumentationKey=ik1;IngestionEndpoint=https://x/;ApplicationId=11111111-0000-0000-0000-000000000001",
                "payments-api" => "InstrumentationKey=ik2;ApplicationId=22222222-0000-0000-0000-000000000002",
                "hr-portal" => "InstrumentationKey=ik3;ApplicationId=33333333-0000-0000-0000-000000000003",
                "no-app-id" => "InstrumentationKey=ik4",
                _ => null,
            });
        }
    }
}
