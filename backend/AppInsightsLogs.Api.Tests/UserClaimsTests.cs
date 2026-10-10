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

public class AzureAdB2COptionsTests
{
    [Fact]
    public void ValidAudiences_IncludeBothClientIdsAndApiScopeAppIdUri()
    {
        var options = new Options.AzureAdB2COptions
        {
            SpaClientId = "spa-id",
            ApiClientId = "api-id",
            ApiScopes = ["https://contoso.onmicrosoft.com/logviewer-api/Logs.Read"],
        };

        Assert.Equal(["api-id", "spa-id", "https://contoso.onmicrosoft.com/logviewer-api"], options.ValidAudiences);
    }

    [Fact]
    public void ValidAudiences_SingleRegistration_IsTheSpaClientId() =>
        Assert.Equal(["spa-id"], new Options.AzureAdB2COptions { SpaClientId = "spa-id" }.ValidAudiences);
}
