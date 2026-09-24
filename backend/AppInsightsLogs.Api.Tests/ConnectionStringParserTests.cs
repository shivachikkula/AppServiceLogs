using AppInsightsLogs.Api.Services;

namespace AppInsightsLogs.Api.Tests;

public class ConnectionStringParserTests
{
    private const string ConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/;LiveEndpoint=https://westeurope.livediagnostics.monitor.azure.com/;ApplicationId=11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Parse_ReadsAllSegments()
    {
        var parts = ConnectionStringParser.Parse(ConnectionString);
        Assert.Equal("00000000-0000-0000-0000-000000000001", parts["instrumentationkey"]);
        Assert.Equal("https://westeurope-5.in.applicationinsights.azure.com/", parts["IngestionEndpoint"]);
    }

    [Fact]
    public void ResolveApplicationId_UsesConnectionString() =>
        Assert.Equal("11111111-2222-3333-4444-555555555555", ConnectionStringParser.ResolveApplicationId(ConnectionString, null));

    [Fact]
    public void ResolveApplicationId_PrefersExplicitValue() =>
        Assert.Equal("explicit", ConnectionStringParser.ResolveApplicationId(ConnectionString, " explicit "));

    [Fact]
    public void ResolveApplicationId_ReturnsNullWhenMissing() =>
        Assert.Null(ConnectionStringParser.ResolveApplicationId("InstrumentationKey=abc", null));
}
