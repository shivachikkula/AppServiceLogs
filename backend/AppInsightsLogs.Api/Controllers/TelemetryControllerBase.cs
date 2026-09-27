using System.Net;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

[Authorize]
public abstract class TelemetryControllerBase(ApplicationCatalog catalog) : ControllerBase
{
    /// <summary>
    /// Checks that the signed-in user may view <paramref name="appKey"/>, resolves its Application Insights id from
    /// Key Vault and runs the query. Failures become ProblemDetails here, rather than propagating to middleware
    /// (which also makes the Visual Studio debugger break on "user-unhandled" exceptions).
    /// </summary>
    protected async Task<ActionResult<T>> Run<T>(string? appKey, Func<string, Task<T>> query, CancellationToken cancellationToken)
    {
        try
        {
            var email = UserClaims.GetEmail(User) ?? throw new AppInsightsQueryException(
                HttpStatusCode.Forbidden,
                "Your sign-in token contains no email address. In the B2C user flow, enable 'Email Addresses' under Application claims.",
                "EmailClaimMissing");

            var application = await catalog.ResolveAsync(email, appKey, cancellationToken);
            var result = await query(application.ApplicationId);
            return result is null ? NotFound() : Ok(result);
        }
        catch (AppInsightsQueryException ex)
        {
            return Problem(title: ex.Code ?? "Application Insights query failed", detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
    }
}
