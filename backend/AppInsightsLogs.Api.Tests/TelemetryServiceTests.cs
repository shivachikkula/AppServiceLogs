using System.Net;
using System.Text;
using System.Text.Json;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppInsightsLogs.Api.Tests;

public class TelemetryServiceTests
{
    private const string LiveResponse = """
        {
          "tables": [{
            "name": "PrimaryResult",
            "columns": [
              {"name":"itemId","type":"string"},{"name":"timestamp","type":"datetime"},{"name":"IngestedAt","type":"datetime"},
              {"name":"itemType","type":"string"},{"name":"Severity","type":"int"},{"name":"Message","type":"string"},
              {"name":"cloud_RoleName","type":"string"},{"name":"cloud_RoleInstance","type":"string"},{"name":"operation_Name","type":"string"},
              {"name":"operation_Id","type":"string"},{"name":"resultCode","type":"string"},{"name":"duration","type":"real"},
              {"name":"customDimensions","type":"dynamic"}
            ],
            "rows": [
              ["a","2026-09-24T10:00:01Z","2026-09-24T10:01:00Z","trace",2,"Something happened","api","i1","GET /x","op1","",null,"{\"k\":\"v\"}"],
              ["b","2026-09-24T10:00:02Z","2026-09-24T10:01:30Z","request",3,"GET /x -> 500","api","i1","GET /x","op1","500",12.5,null]
            ]
          }]
        }
        """;

    [Fact]
    public async Task GetLiveLogs_MapsRowsAndReturnsLatestIngestionTimeAsCursor()
    {
        var (service, handler) = Create(LiveResponse);

        var result = await service.GetLiveLogsAsync(
            new LiveLogsQuery(DateTimeOffset.Parse("2026-09-24T10:00:00Z"), 30, ["trace", "request"], 2, "boom\"", null, null, 100),
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("b", result.Items[0].ItemId);
        Assert.Equal(12.5, result.Items[0].DurationMs);
        Assert.Equal("{\"k\":\"v\"}", result.Items[1].CustomDimensions);
        Assert.Equal(DateTimeOffset.Parse("2026-09-24T10:01:30Z"), result.Cursor);

        Assert.Equal("https://api.applicationinsights.io/v1/apps/app-123/query", handler.RequestUri);
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.StartsWith("union isfuzzy=true (traces | where timestamp > ago(30m) | extend IngestedAt = ingestion_time() | where IngestedAt > datetime(2026-09-24T10:00:00.0000000Z)), (requests |", handler.Query);
        Assert.Contains("where Severity >= 2", handler.Query);
        Assert.Contains("contains \"boom\\\"\"", handler.Query);
    }

    [Fact]
    public async Task Query_ForbiddenResponse_ThrowsWithRoleHint()
    {
        var (service, _) = Create("""{"error":{"code":"InsufficientAccessError","message":"denied"}}""", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<AppInsightsQueryException>(() =>
            service.GetExceptionsAsync(new ExceptionsQuery(60, null, null, null, null, 10), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("Monitoring Reader", ex.Message);
    }

    [Fact]
    public void FlattenMessages_IncludesInnerExceptions()
    {
        var ex = new InvalidOperationException(
            "DefaultAzureCredential authentication failed due to an unhandled exception: ",
            new Exception("AADSTS50020: User account from identity provider does not exist in tenant.\nTrace ID: 1"));

        Assert.Equal(
            "DefaultAzureCredential authentication failed due to an unhandled exception: -> AADSTS50020: User account from identity provider does not exist in tenant. Trace ID: 1",
            AppInsightsQueryClient.FlattenMessages(ex));
    }

    [Fact]
    public void BuildStackTrace_UsesParsedStackAndRawStack()
    {
        using var details = JsonDocument.Parse("""
            [
              {"type":"System.InvalidOperationException","message":"outer","parsedStack":[
                {"level":0,"method":"App.Foo.Bar","assembly":"App","fileName":"Foo.cs","line":42},
                {"level":1,"method":"App.Foo.Baz","assembly":"App","line":0}
              ]},
              {"type":"System.NullReferenceException","message":"inner","rawStack":"   at Somewhere()\n"}
            ]
            """);

        var stack = TelemetryService.BuildStackTrace(details.RootElement);

        Assert.Equal(
            "System.InvalidOperationException: outer\n   at App.Foo.Bar in Foo.cs:42\n   at App.Foo.Baz\n\n--- Inner exception ---\nSystem.NullReferenceException: inner\n   at Somewhere()",
            stack.Replace("\r\n", "\n"));
    }

    private static (TelemetryService Service, CapturingHandler Handler) Create(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AppInsightsOptions
        {
            ConnectionString = "InstrumentationKey=ik;ApplicationId=app-123",
        });
        var handler = new CapturingHandler(body, status);
        var client = new AppInsightsQueryClient(new HttpClient(handler), new FakeCredential(), options, NullLogger<AppInsightsQueryClient>.Instance);
        return (new TelemetryService(client, options), handler);
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class CapturingHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string Query { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Query = payload.RootElement.GetProperty("query").GetString() ?? string.Empty;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
