using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class ApplicationsController(ApplicationCatalog catalog) : ControllerBase
{
    /// <summary>The signed-in user's email address and display name.</summary>
    [HttpGet("me")]
    public ActionResult<UserInfo> GetMe()
    {
        var email = UserClaims.GetEmail(User);
        return email is null
            ? Problem(title: "EmailClaimMissing", detail: "Your sign-in token contains no email address. In the B2C user flow, enable 'Email Addresses' under Application claims.", statusCode: StatusCodes.Status403Forbidden)
            : new UserInfo(email, UserClaims.GetName(User));
    }

    /// <summary>Applications the signed-in user may view (application name and app key only).</summary>
    [HttpGet("applications")]
    public async Task<ActionResult<IReadOnlyList<ApplicationSummary>>> GetApplications(CancellationToken cancellationToken)
    {
        var email = UserClaims.GetEmail(User);
        if (email is null)
        {
            return Ok(Array.Empty<ApplicationSummary>());
        }

        try
        {
            return Ok(await catalog.ListForUserAsync(email, cancellationToken));
        }
        catch (AppInsightsQueryException ex)
        {
            return Problem(title: ex.Code, detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
    }
}
