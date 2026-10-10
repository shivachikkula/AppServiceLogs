using System.Net;
using System.Text;
using System.Text.Json;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppInsightsLogs.Api.Tests;

public class QueryServiceTests
{
    [Fact]
    public async Task Run_ReturnsTypedColumns_AndTruncatesLargeResults()
    {
        var rows = string.Join(",", Enumerable.Range(1, 5).Select(i => $"[\"op{i}\",{i},{{\"k\":\"v\"}}]"));
        var (service, handler) = Create($$"""{"tables":[{"name":"PrimaryResult","columns":[{"name":"name","type":"string"},{"name":"n","type":"long"},{"name":"bag","type":"dynamic"}],"rows":[{{rows}}]}]}""", maxRows: 3);

        var result = await service.RunAsync("app-1", "requests | take 5", 60, CancellationToken.None);

        Assert.Equal(["name:string", "n:long", "bag:dynamic"], result.Columns.Select(c => $"{c.Name}:{c.Type}"));
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(5, result.TotalRows);
        Assert.True(result.Truncated);
        Assert.Equal(JsonValueKind.Object, result.Rows[0][2].ValueKind);
        Assert.Equal("requests | take 5", handler.Query);
        Assert.Equal("PT1H", handler.Timespan);
    }

    [Fact]
    public async Task Run_BlockedQuery_IsRejectedWithoutCallingApplicationInsights()
    {
        var (service, handler) = Create("{}");

        var ex = await Assert.ThrowsAsync<AppInsightsQueryException>(() =>
            service.RunAsync("app-1", "app('other').requests", 60, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Null(handler.Query);
    }

    [Fact]
    public async Task GetSchema_GroupsColumnsByTable_AndIsCached()
    {
        var (service, handler) = Create("""
            {"tables":[{"name":"PrimaryResult","columns":[{"name":"TableName","type":"string"},{"name":"ColumnName","type":"string"},{"name":"ColumnType","type":"string"}],
             "rows":[["requests","timestamp","datetime"],["requests","duration","real"],["traces","message","string"]]}]}
            """);

        var schema = await service.GetSchemaAsync("app-1", CancellationToken.None);
        await service.GetSchemaAsync("app-1", CancellationToken.None);

        Assert.Equal(["requests", "traces"], schema.Tables.Select(t => t.Name));
        Assert.Equal(["timestamp:datetime", "duration:real"], schema.Tables[0].Columns.Select(c => $"{c.Name}:{c.Type}"));
        Assert.Contains("getschema", handler.Query);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void AssistantPrompt_IncludesSchemaAndCrossResourceRule()
    {
        var schema = new KqlSchema([new KqlSchemaTable("requests", "Incoming requests", [new KqlSchemaColumn("duration", "real")])]);

        var prompt = KqlAssistant.BuildSystemPrompt(schema);

        Assert.Contains("- requests: Incoming requests", prompt);
        Assert.Contains("duration:real", prompt);
        Assert.Contains("Never use cross-resource", prompt);
    }

    [Fact]
    public void AssistantUserMessage_ForFix_IncludesQueryAndError()
    {
        var message = KqlAssistant.BuildUserMessage("", "requests | where durationz > 1", "Failed to resolve column 'durationz'");

        Assert.Contains("requests | where durationz > 1", message);
        Assert.Contains("Failed to resolve column 'durationz'", message);
        Assert.Contains("Fix the query.", message);
    }

    [Fact]
    public void AssistantResponse_IsParsedFromChatCompletion()
    {
        var content = JsonSerializer.Serialize(new { query = "requests | take 10", explanation = "Ten requests." });
        var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

        var result = KqlAssistant.ParseResult(body);

        Assert.Equal("requests | take 10", result.Query);
        Assert.Equal("Ten requests.", result.Explanation);
    }

    [Fact]
    public void AssistantResponse_WithoutQuery_IsAnError() =>
        Assert.Throws<AppInsightsQueryException>(() => KqlAssistant.ParseResult("""{"choices":[{"message":{"content":"{\"explanation\":\"x\"}"}}]}"""));

    private static (QueryService Service, CapturingHandler Handler) Create(string body, int maxRows = 100)
    {
        var handler = new CapturingHandler(body);
        var client = new AppInsightsQueryClient(new HttpClient(handler), new FakeCredential(),
            Microsoft.Extensions.Options.Options.Create(new AppInsightsOptions()), NullLogger<AppInsightsQueryClient>.Instance);
        var service = new QueryService(client, new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new QueryOptions { MaxResultRows = maxRows }));
        return (service, handler);
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public string? Query { get; private set; }
        public string? Timespan { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Query = payload.RootElement.GetProperty("query").GetString();
            Timespan = payload.RootElement.GetProperty("timespan").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
