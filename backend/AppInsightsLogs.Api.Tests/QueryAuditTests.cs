using System.Net;
using System.Security.Claims;
using System.Text;
using AppInsightsLogs.Api.Controllers;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppInsightsLogs.Api.Tests;

public class QueryAuditTests
{
    private const string OneRow = """{"tables":[{"name":"PrimaryResult","columns":[{"name":"n","type":"long"}],"rows":[[1],[2]]}]}""";

    [Fact]
    public async Task SuccessfulQuery_IsAudited_WithRowCount()
    {
        var (controller, db) = Create(OneRow);

        var response = await controller.RunQuery(new RunQueryRequest("orders-api", "requests | take 2", 60), CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        var entry = Assert.Single(db.QueryAudit);
        Assert.Equal(("jane@contoso.com", "orders-api", AuditActions.Query, "requests | take 2"), (entry.Email, entry.AppKey, entry.Action, entry.Text));
        Assert.True(entry.Succeeded);
        Assert.Equal(2, entry.RowCount);
        Assert.Equal(60, entry.RangeMinutes);
    }

    [Fact]
    public async Task RejectedQuery_IsAudited_AsFailure()
    {
        var (controller, db) = Create(OneRow);

        var response = await controller.RunQuery(new RunQueryRequest("orders-api", "app('other').requests", 60), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        var entry = Assert.Single(db.QueryAudit);
        Assert.False(entry.Succeeded);
        Assert.Contains("app()", entry.Error);
    }

    [Fact]
    public async Task QueryOnUnassignedApplication_IsAudited_AsDenied()
    {
        var (controller, db) = Create(OneRow);

        var response = await controller.RunQuery(new RunQueryRequest("hr-portal", "requests", 60), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        var entry = Assert.Single(db.QueryAudit);
        Assert.Equal("hr-portal", entry.AppKey);
        Assert.False(entry.Succeeded);
    }

    [Fact]
    public async Task AssistantRequest_WhenDisabled_IsAudited_AsFailure()
    {
        var (controller, db) = Create(OneRow);

        await controller.Generate(new GenerateQueryRequest("orders-api", "failed requests"), CancellationToken.None);

        var entry = Assert.Single(db.QueryAudit);
        Assert.Equal((AuditActions.AiGenerate, "failed requests", false), (entry.Action, entry.Text, entry.Succeeded));
    }

    private static (QueryController Controller, LogViewerDbContext Db) Create(string appInsightsResponse)
    {
        var db = new LogViewerDbContext(new DbContextOptionsBuilder<LogViewerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.UserApplications.Add(new UserApplication { Email = "jane@contoso.com", ApplicationName = "Orders API", AppKey = "orders-api" });
        db.SaveChanges();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new ApplicationCatalog(db, new Secrets(), cache, Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions()));
        var client = new AppInsightsQueryClient(new HttpClient(new Handler(appInsightsResponse)), new Credential(),
            Microsoft.Extensions.Options.Options.Create(new AppInsightsOptions()), NullLogger<AppInsightsQueryClient>.Instance);
        var queries = new QueryService(client, cache, Microsoft.Extensions.Options.Options.Create(new QueryOptions()));
        var assistant = new KqlAssistant(new HttpClient(), new Credential(), Microsoft.Extensions.Options.Options.Create(new AzureOpenAIOptions()), NullLogger<KqlAssistant>.Instance);

        var controller = new QueryController(queries, assistant, new AuditLog(db, NullLogger<AuditLog>.Instance), catalog)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "jane@contoso.com")], "test")),
                },
            },
        };
        return (controller, db);
    }

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

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
