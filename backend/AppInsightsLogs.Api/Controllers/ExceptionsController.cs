using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Route("api/exceptions")]
public sealed class ExceptionsController(TelemetryService telemetry, ApplicationCatalog catalog) : TelemetryControllerBase(catalog)
{
    private const int MaxRangeMinutes = 60 * 24 * 90;

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<ExceptionEntry>>> GetExceptions(
        [FromQuery] string? appKey,
        [FromQuery] int rangeMinutes = 60 * 24,
        [FromQuery] string? search = null,
        [FromQuery] string? problemId = null,
        [FromQuery] string? roleName = null,
        [FromQuery] string? operationId = null,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default) =>
        Run(appKey, applicationId => telemetry.GetExceptionsAsync(
            applicationId,
            new ExceptionsQuery(Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), search, problemId, roleName, operationId, take),
            cancellationToken), cancellationToken);

    /// <summary>Exceptions grouped by problem id, most frequent first.</summary>
    [HttpGet("summary")]
    public Task<ActionResult<IReadOnlyList<ExceptionGroup>>> GetSummary(
        [FromQuery] string? appKey,
        [FromQuery] int rangeMinutes = 60 * 24,
        [FromQuery] string? search = null,
        [FromQuery] string? roleName = null,
        CancellationToken cancellationToken = default) =>
        Run(appKey, applicationId => telemetry.GetExceptionSummaryAsync(
            applicationId,
            new ExceptionsQuery(Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), search, null, roleName, null, 0),
            cancellationToken), cancellationToken);

    [HttpGet("{itemId}")]
    public Task<ActionResult<ExceptionDetail?>> GetException(
        string itemId,
        [FromQuery] string? appKey,
        [FromQuery] int rangeMinutes = 60 * 24 * 30,
        CancellationToken cancellationToken = default) =>
        Run(appKey, applicationId => telemetry.GetExceptionAsync(
            applicationId, itemId, Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), cancellationToken), cancellationToken);
}
