using AppInsightsLogs.Api.Services;

namespace AppInsightsLogs.Api.Tests;

public class KqlTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"c:\temp", "\"c:\\\\temp\"")]
    [InlineData("x\" | take 1 //", "\"x\\\" | take 1 //\"")]
    [InlineData("line1\nline2", "\"line1\\nline2\"")]
    public void String_EscapesSpecialCharacters(string input, string expected) =>
        Assert.Equal(expected, Kql.String(input));

    [Fact]
    public void DateTime_IsUtcIso8601()
    {
        var value = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal("datetime(2026-09-24T10:00:00.0000000Z)", Kql.DateTime(value));
    }
}
