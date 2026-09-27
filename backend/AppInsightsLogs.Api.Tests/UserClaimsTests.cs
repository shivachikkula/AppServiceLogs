using System.Security.Claims;
using AppInsightsLogs.Api.Services;

namespace AppInsightsLogs.Api.Tests;

public class UserClaimsTests
{
    [Theory]
    [InlineData("emails", " Jane.Doe@Contoso.com ", "jane.doe@contoso.com")] // B2C user flows
    [InlineData("email", "bob@contoso.com", "bob@contoso.com")]              // custom policies
    [InlineData("preferred_username", "amy@contoso.com", "amy@contoso.com")]
    [InlineData("preferred_username", "not-an-email", null)]
    public void GetEmail_ReadsKnownClaims(string type, string value, string? expected) =>
        Assert.Equal(expected, UserClaims.GetEmail(new ClaimsPrincipal(new ClaimsIdentity([new Claim(type, value)], "test"))));
}
