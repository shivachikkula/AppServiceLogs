using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppInsightsLogs.Api.Tests;

public class OverviewAndOperationTests
{
    private static string Table(string columns, string rows) =>
        $$"""{"tables":[{"name":"PrimaryResult","columns":[{{columns}}],"rows":[{{rows}}]}]}""";

    [Fact]
    public async Task Overview_RunsFiveQueries_AndMapsEveryPart()
    {
        var handler = new RoutingHandler(query => query switch
        {
            _ when query.Contains("print") => Table(
                """{"name":"Requests","type":"long"},{"name":"FailedRequests","type":"long"},{"name":"AvgDuration","type":"real"},{"name":"P95Duration","type":"real"},{"name":"Exceptions","type":"long"},{"name":"FailedDependencies","type":"long"},{"name":"PreviousRequests","type":"long"},{"name":"PreviousFailedRequests","type":"long"},{"name":"PreviousExceptions","type":"long"}""",
                "[1000,50,120.5,480.25,12,3,900,30,20]"),
            _ when query.Contains("range timestamp") => Table(
                """{"name":"timestamp","type":"datetime"},{"name":"Requests","type":"long"},{"name":"FailedRequests","type":"long"},{"name":"P95","type":"real"},{"name":"Exceptions","type":"long"}""",
                """["2026-10-10T10:00:00Z",40,2,300.0,1],["2026-10-10T10:30:00Z",0,0,null,0]"""),
            _ when query.Contains("where Failed > 0") => Table(
                """{"name":"operation_Name","type":"string"},{"name":"Total","type":"long"},{"name":"Failed","type":"long"}""",
                """["POST /checkout",200,40]"""),
            _ when query.Contains("where Count >= 3") => Table(
                """{"name":"operation_Name","type":"string"},{"name":"Count","type":"long"},{"name":"P95","type":"real"}""",
                """["GET /report",10,2500.5]"""),
            _ => Table(
                """{"name":"type","type":"string"},{"name":"problemId","type":"string"},{"name":"Count","type":"long"},{"name":"Message","type":"string"}""",
                """["System.TimeoutException","p1",7,"The operation timed out"]"""),
        });

        var result = await new OverviewService(CreateClient(handler)).GetAsync("app-1", 1440, CancellationToken.None);

        Assert.Equal(5, handler.Queries.Count);
        Assert.Equal((1440, 30), (result.RangeMinutes, result.BinMinutes));
        Assert.Equal((1000L, 50L, 480.25, 900L, 20L), (result.Kpis.Requests, result.Kpis.FailedRequests, result.Kpis.P95DurationMs, result.Kpis.PreviousRequests, result.Kpis.PreviousExceptions));
        Assert.Equal(2, result.Series.Count);
        Assert.Null(result.Series[1].P95DurationMs);
        Assert.Equal("POST /checkout", Assert.Single(result.TopFailing).Operation);
        Assert.Equal(2500.5, Assert.Single(result.Slowest).P95DurationMs);
        Assert.Equal(("System.TimeoutException", 7L), (Assert.Single(result.TopExceptions).Type, result.TopExceptions[0].Count));
        // The headline query compares with the previous period, so it needs twice the range.
        Assert.Contains(handler.Queries, q => q.Query.Contains("print") && q.Timespan == "P2DT30M");
    }

    [Theory]
    [InlineData(60, 1)]
    [InlineData(360, 5)]
    [InlineData(1440, 30)]
    [InlineData(4320, 60)]
    [InlineData(10080, 180)]
    [InlineData(43200, 720)]
    public void BinMinutes_KeepsChartsAroundFiftyToHundredPoints(int range, int expectedBin) =>
        Assert.Equal(expectedBin, OverviewService.BinMinutes(range));

    [Fact]
    public async Task Operation_FiltersEveryTableByEscapedOperationId_AndMapsSpans()
    {
        var handler = new RoutingHandler(_ => Table(
            """{"name":"itemId","type":"string"},{"name":"timestamp","type":"datetime"},{"name":"Kind","type":"string"},{"name":"Id","type":"string"},{"name":"ParentId","type":"string"},{"name":"Name","type":"string"},{"name":"Detail","type":"string"},{"name":"Target","type":"string"},{"name":"DependencyType","type":"string"},{"name":"ResultCode","type":"string"},{"name":"Success","type":"string"},{"name":"Duration","type":"real"},{"name":"Severity","type":"int"},{"name":"RoleName","type":"string"}""",
            """
            ["a","2026-10-10T10:00:00Z","request","r1","","GET /orders","https://x/orders","","","500","False",420.0,null,"api"],
            ["b","2026-10-10T10:00:00.1Z","dependency","d1","r1","SELECT","select *","sql01","SQL","0","True",250.0,null,"api"],
            ["c","2026-10-10T10:00:00.3Z","exception","","d1","SqlException","Timeout","","","","",null,3,"api"]
            """));

        var around = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
        var result = await new OperationService(CreateClient(handler)).GetAsync("app-1", "op\"1", around, CancellationToken.None);

        var query = Assert.Single(handler.Queries).Query;
        Assert.Equal(5, query.Split("operation_Id == \"op\\\"1\"").Length - 1);
        Assert.Contains("datetime(2026-10-09T10:00:00.0000000Z) .. datetime(2026-10-11T10:00:00.0000000Z)", query);
        Assert.Equal(["request", "dependency", "exception"], result.Items.Select(i => i.Kind));
        Assert.Equal((false, 420.0, (string?)null), (result.Items[0].Success, result.Items[0].DurationMs, result.Items[0].ParentId));
        Assert.Equal(("r1", "sql01", true), (result.Items[1].ParentId, result.Items[1].Target, result.Items[1].Success));
        Assert.Null(result.Items[2].Success);
        Assert.False(result.Truncated);
    }

    private static AppInsightsQueryClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new Credential(), Microsoft.Extensions.Options.Options.Create(new AppInsightsOptions()), NullLogger<AppInsightsQueryClient>.Instance);

    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class RoutingHandler(Func<string, string> respond) : HttpMessageHandler
    {
        public ConcurrentBag<(string Query, string Timespan)> Queries { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var query = payload.RootElement.GetProperty("query").GetString()!;
            Queries.Add((query, payload.RootElement.GetProperty("timespan").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(query), Encoding.UTF8, "application/json") };
        }
    }
}
