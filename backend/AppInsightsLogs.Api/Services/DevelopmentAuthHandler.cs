using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Development-only authentication used when Azure AD B2C is not configured: every request is signed in as
/// AzureAdB2C:DevelopmentUserEmail. Program.cs registers it only in the Development environment.
/// </summary>
public sealed class DevelopmentAuthHandler(
    IOptionsMonitor<DevelopmentAuthHandler.SchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<DevelopmentAuthHandler.SchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";

    public sealed class SchemeOptions : AuthenticationSchemeOptions
    {
        public string Email { get; set; } = string.Empty;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim("email", Options.Email), new Claim("name", "Development user")],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
