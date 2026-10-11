using System.Net;
using System.Text;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppInsightsLogs.Api.Tests;

public class ReliabilityTests
{
    [Fact]
    public async Task QueryCache_ReturnsCachedValue_BypassesOnRefresh_AndNeverCachesFailures()
    {
        var cache = new QueryCache(new MemoryCache(new MemoryCacheOptions()));
        var calls = 0;
        Task<int> Factory() => Task.FromResult(++calls);

        Assert.Equal(1, await cache.GetOrAddAsync("k", 60, bypass: false, Factory));
        Assert.Equal(1, await cache.GetOrAddAsync("k", 60, bypass: false, Factory));
        Assert.Equal(2, await cache.GetOrAddAsync("k", 60, bypass: true, Factory));
        Assert.Equal(2, await cache.GetOrAddAsync("k", 60, bypass: false, Factory));
        Assert.Equal(3, await cache.GetOrAddAsync("other", 0, bypass: false, Factory));
        Assert.Equal(4, await cache.GetOrAddAsync("other", 0, bypass: false, Factory));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync<int>("failing", 60, bypass: false, () => throw new InvalidOperationException()));
        Assert.Equal(5, await cache.GetOrAddAsync("failing", 60, bypass: false, Factory));
    }

    [Fact]
    public async Task ApplicationInsightsCalls_AreRetried_OnTransientErrors()
    {
        var handler = new FlakyHandler(failures: 1);
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("AzureAdB2C:DevelopmentUserEmail", "jane@contoso.com");
            builder.UseSetting("ConnectionStrings:LogViewerDb", "Server=x");
            builder.UseSetting("KeyVault:VaultUri", "https://vault.vault.azure.net/");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TokenCredential>(new FakeCredential());
                services.AddHttpClient<AppInsightsQueryClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

        using var scope = app.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<AppInsightsQueryClient>();
        var table = await client.QueryAsync("app-1", "requests | take 1", TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal(2, handler.Calls); // one 503, then success
        Assert.Single(table.Rows);
    }

    private sealed class FlakyHandler(int failures) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls <= failures)
            {
                var busy = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(busy);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tables":[{"name":"PrimaryResult","columns":[{"name":"n","type":"long"}],"rows":[[1]]}]}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
