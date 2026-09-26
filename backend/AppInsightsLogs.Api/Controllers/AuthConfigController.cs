using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Controllers;

/// <summary>
/// Public sign-in settings for the Angular app, so the same build works in every environment.
/// Contains no secrets: client ids and authorities are visible to any browser anyway.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/auth-config")]
public sealed class AuthConfigController(IOptions<AzureAdB2COptions> options) : ControllerBase
{
    [HttpGet]
    public AuthConfigResponse Get()
    {
        var b2c = options.Value;
        return b2c.IsConfigured
            ? new AuthConfigResponse(true, b2c.SpaClientId, b2c.Authority, [b2c.AuthorityHost], b2c.EffectiveScopes)
            : new AuthConfigResponse(false, null, null, [], []);
    }
}
