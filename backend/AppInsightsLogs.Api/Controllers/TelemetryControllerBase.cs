using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

public abstract class TelemetryControllerBase : ControllerBase
{
    /// <summary>
    /// Runs a query and turns Application Insights failures into ProblemDetails here, rather than letting them
    /// propagate to middleware (which also makes the Visual Studio debugger break on "user-unhandled" exceptions).
    /// </summary>
    protected async Task<ActionResult<T>> Run<T>(Func<Task<T>> query)
    {
        try
        {
            var result = await query();
            return result is null ? NotFound() : Ok(result);
        }
        catch (AppInsightsQueryException ex)
        {
            return Problem(title: ex.Code ?? "Application Insights query failed", detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
    }
}
