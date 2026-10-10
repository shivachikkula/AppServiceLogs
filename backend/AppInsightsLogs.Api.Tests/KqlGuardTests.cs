using AppInsightsLogs.Api.Services;

namespace AppInsightsLogs.Api.Tests;

public class KqlGuardTests
{
    [Theory]
    [InlineData("requests | where timestamp > ago(1h) | summarize count() by bin(timestamp, 5m)")]
    [InlineData("traces | where message has \"app(\" or message contains 'workspace(\"x\")'")]
    [InlineData("// app('other').requests\nrequests | take 10")]
    [InlineData("let appName = 'orders';\nrequests | where cloud_RoleName == appName")]
    [InlineData("requests | extend database_name = tostring(customDimensions.database) | take 5")]
    [InlineData("exceptions | where outerMessage has @\"cluster('x')\" | take 1")]
    [InlineData("requests | evaluate autocluster()")]
    [InlineData("print x = ```multi\napp(\"y\")\nline```")]
    public void Check_AllowsQueriesAgainstTheSelectedApplication(string query) =>
        Assert.Null(KqlGuard.Check(query));

    [Theory]
    [InlineData("app('11111111-2222-3333-4444-555555555555').requests | take 10", "app()")]
    [InlineData("union requests, app (\"other\").requests", "app()")]
    [InlineData("workspace('w').AppRequests", "workspace()")]
    [InlineData("requests | join (resource('/subscriptions/x').requests) on operation_Id", "resource()")]
    [InlineData("cluster('help').database('Samples').StormEvents", "cluster()")]
    [InlineData("['app'](\"other\").requests", "app()")]
    [InlineData("externaldata (x:string) [h@'https://example.com/x.csv']", "externaldata()")]
    [InlineData("evaluate http_request('https://example.com')", "http_request")]
    [InlineData("requests | evaluate python(typeof(*), 'result = df')", "python")]
    [InlineData(".show tables", "Management commands")]
    [InlineData("   ", "empty")]
    public void Check_RejectsCrossResourceAndExternalAccess(string query, string expectedReason) =>
        Assert.Contains(expectedReason, KqlGuard.Check(query) ?? "(allowed)", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Check_RejectsVeryLongQueries() =>
        Assert.NotNull(KqlGuard.Check("requests | take 1 //" + new string('x', KqlGuard.MaxQueryLength)));

    [Fact]
    public void StripCommentsAndStrings_KeepsCodeAndBlanksLiterals() =>
        Assert.Equal("requests | where name ==   | take 5", KqlGuard.StripCommentsAndStrings("requests | where name == 'a\\'pp(' | take 5"));
}
