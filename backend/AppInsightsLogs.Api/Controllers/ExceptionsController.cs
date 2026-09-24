using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Route("api/exceptions")]
public sealed class ExceptionsController(TelemetryService telemetry) : ControllerBase
{
    private const int MaxRangeMinutes = 60 * 24 * 90;

    [HttpGet]
    public Task<IReadOnlyList<ExceptionEntry>> GetExceptions(
        [FromQuery] int rangeMinutes = 60 * 24,
        [FromQuery] string? search = null,
        [FromQuery] string? problemId = null,
        [FromQuery] string? roleName = null,
        [FromQuery] string? operationId = null,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default) =>
        telemetry.GetExceptionsAsync(
            new ExceptionsQuery(Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), search, problemId, roleName, operationId, take),
            cancellationToken);

    /// <summary>Exceptions grouped by problem id, most frequent first.</summary>
    [HttpGet("summary")]
    public Task<IReadOnlyList<ExceptionGroup>> GetSummary(
        [FromQuery] int rangeMinutes = 60 * 24,
        [FromQuery] string? search = null,
        [FromQuery] string? roleName = null,
        CancellationToken cancellationToken = default) =>
        telemetry.GetExceptionSummaryAsync(
            new ExceptionsQuery(Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), search, null, roleName, null, 0),
            cancellationToken);

    [HttpGet("{itemId}")]
    public async Task<ActionResult<ExceptionDetail>> GetException(
        string itemId,
        [FromQuery] int rangeMinutes = 60 * 24 * 30,
        CancellationToken cancellationToken = default)
    {
        var detail = await telemetry.GetExceptionAsync(itemId, Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), cancellationToken);
        return detail is null ? NotFound() : detail;
    }
}
