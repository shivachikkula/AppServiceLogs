using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Route("api/logs")]
public sealed class LogsController(TelemetryService telemetry) : ControllerBase
{
    /// <summary>
    /// Returns recent telemetry across traces, requests, dependencies, exceptions and custom events.
    /// Poll with the returned <c>cursor</c> as <c>since</c> to receive only newly ingested items.
    /// </summary>
    [HttpGet("live")]
    public Task<LiveLogsResponse> GetLive(
        [FromQuery] DateTimeOffset? since,
        [FromQuery] int lookbackMinutes = 30,
        [FromQuery] string? types = null,
        [FromQuery] int minSeverity = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? roleName = null,
        [FromQuery] string? operationId = null,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        var itemTypes = (types ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(TelemetryService.Tables.ContainsKey)
            .ToList();

        var query = new LiveLogsQuery(
            since,
            Math.Clamp(lookbackMinutes, 1, 60 * 24 * 7),
            itemTypes,
            Math.Clamp(minSeverity, 0, 4),
            search,
            roleName,
            operationId,
            take);

        return telemetry.GetLiveLogsAsync(query, cancellationToken);
    }
}
