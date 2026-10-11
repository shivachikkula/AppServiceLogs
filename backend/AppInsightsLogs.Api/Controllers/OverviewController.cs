using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using AppInsightsLogs.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class OverviewController(OverviewService overview, OperationService operations, ApplicationCatalog catalog)
    : TelemetryControllerBase(catalog)
{
    /// <summary>Headline numbers (with the previous period), trends and top problems for the selected range.</summary>
    [HttpGet("overview")]
    [EnableRateLimiting(RateLimitPolicies.Overview)]
    public Task<ActionResult<OverviewResponse>> GetOverview(
        [FromQuery] string? appKey,
        [FromQuery] int rangeMinutes = 1440,
        CancellationToken cancellationToken = default) =>
        Run(appKey, applicationId => overview.GetAsync(applicationId, rangeMinutes, cancellationToken), cancellationToken);

    /// <summary>All telemetry of one operation, for the timeline. <paramref name="around"/> narrows the search to ±1 day.</summary>
    [HttpGet("operations/{operationId}")]
    [EnableRateLimiting(RateLimitPolicies.Query)]
    public Task<ActionResult<OperationTimeline>> GetOperation(
        string operationId,
        [FromQuery] string? appKey,
        [FromQuery] DateTimeOffset? around,
        CancellationToken cancellationToken = default) =>
        Run(appKey, applicationId => operations.GetAsync(applicationId, operationId, around, cancellationToken), cancellationToken);
}
