using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using AppInsightsLogs.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Route("api/exceptions")]
public sealed class ExceptionsController(
    TelemetryService telemetry,
    QueryCache cache,
    IOptions<CachingOptions> caching,
    ApplicationCatalog catalog) : TelemetryControllerBase(catalog)
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
        [FromQuery] bool fresh = false,
        CancellationToken cancellationToken = default)
    {
        var query = new ExceptionsQuery(Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), search, problemId, roleName, operationId, take);
        return Run(appKey, applicationId => cache.GetOrAddAsync(
            $"exceptions:{applicationId}:{query}", caching.Value.ExceptionsSeconds, fresh,
            () => telemetry.GetExceptionsAsync(applicationId, query, cancellationToken)), cancellationToken);
    }

    /// <summary>Exceptions grouped by problem id, most frequent first.</summary>
    [HttpGet("summary")]
    public Task<ActionResult<IReadOnlyList<ExceptionGroup>>> GetSummary(
        [FromQuery] string? appKey,
        [FromQuery] int rangeMinutes = 60 * 24,
        [FromQuery] string? search = null,
        [FromQuery] string? roleName = null,
        [FromQuery] bool fresh = false,
        CancellationToken cancellationToken = default)
    {
        var query = new ExceptionsQuery(Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), search, null, roleName, null, 0);
        return Run(appKey, applicationId => cache.GetOrAddAsync(
            $"exceptions-summary:{applicationId}:{query}", caching.Value.ExceptionsSeconds, fresh,
            () => telemetry.GetExceptionSummaryAsync(applicationId, query, cancellationToken)), cancellationToken);
    }

    [HttpGet("{itemId}")]
    public Task<ActionResult<ExceptionDetail?>> GetException(
        string itemId,
        [FromQuery] string? appKey,
        [FromQuery] int rangeMinutes = 60 * 24 * 30,
        CancellationToken cancellationToken = default) =>
        Run(appKey, applicationId => telemetry.GetExceptionAsync(
            applicationId, itemId, Math.Clamp(rangeMinutes, 1, MaxRangeMinutes), cancellationToken), cancellationToken);
}
