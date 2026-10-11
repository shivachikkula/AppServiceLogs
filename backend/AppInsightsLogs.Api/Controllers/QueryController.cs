using System.Diagnostics;
using System.Net;
using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AppInsightsLogs.Api.Options;

namespace AppInsightsLogs.Api.Controllers;

/// <summary>User-written KQL against the selected application, its schema for IntelliSense, and the AI assistant.</summary>
[ApiController]
[Route("api/query")]
public sealed class QueryController(QueryService queries, KqlAssistant assistant, AuditLog audit, ApplicationCatalog catalog)
    : TelemetryControllerBase(catalog)
{
    [HttpGet("schema")]
    public Task<ActionResult<KqlSchema>> GetSchema([FromQuery] string? appKey, CancellationToken cancellationToken) =>
        Run(appKey, applicationId => queries.GetSchemaAsync(applicationId, cancellationToken), cancellationToken);

    [HttpPost("run")]
    [EnableRateLimiting(RateLimitPolicies.Query)]
    public Task<ActionResult<KqlResult>> RunQuery([FromBody] RunQueryRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        return Run(
            request.AppKey,
            async applicationId =>
            {
                var result = await queries.RunAsync(applicationId, request.Query ?? string.Empty, request.RangeMinutes, cancellationToken);
                await audit.RecordAsync(NewEntry(AuditActions.Query, request.AppKey, request.Query, request.RangeMinutes, stopwatch, succeeded: true, rowCount: result.TotalRows), cancellationToken);
                return result;
            },
            cancellationToken,
            ex => audit.RecordAsync(NewEntry(AuditActions.Query, request.AppKey, request.Query, request.RangeMinutes, stopwatch, succeeded: false, error: ex.Message), cancellationToken));
    }

    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<ActionResult<GeneratedQuery>> Generate([FromBody] GenerateQueryRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var fixing = !string.IsNullOrWhiteSpace(request.Error);
        var action = fixing ? AuditActions.AiFix : AuditActions.AiGenerate;
        var text = fixing ? $"{request.CurrentQuery}\n-- error: {request.Error}" : request.Prompt;
        return Run(
            request.AppKey,
            async applicationId =>
            {
                if (!assistant.IsEnabled)
                {
                    throw new AppInsightsQueryException(HttpStatusCode.NotFound, "The AI query assistant is not configured.", "AssistantDisabled");
                }
                var schema = await queries.GetSchemaAsync(applicationId, cancellationToken);
                var generated = await assistant.GenerateAsync(request, schema, cancellationToken);
                await audit.RecordAsync(NewEntry(action, request.AppKey, text, null, stopwatch, succeeded: true), cancellationToken);
                return generated;
            },
            cancellationToken,
            ex => audit.RecordAsync(NewEntry(action, request.AppKey, text, null, stopwatch, succeeded: false, error: ex.Message), cancellationToken));
    }

    private QueryAuditEntry NewEntry(
        string action, string? appKey, string? text, int? rangeMinutes, Stopwatch stopwatch,
        bool succeeded, int? rowCount = null, string? error = null) => new()
    {
        Email = UserClaims.GetEmail(User) ?? "(no email claim)",
        AppKey = string.IsNullOrWhiteSpace(appKey) ? "(none)" : appKey.Length > 127 ? appKey[..127] : appKey,
        Action = action,
        Text = text ?? string.Empty,
        RangeMinutes = rangeMinutes,
        Succeeded = succeeded,
        RowCount = rowCount,
        DurationMs = (int)stopwatch.ElapsedMilliseconds,
        Error = error,
    };
}

[ApiController]
[Authorize]
[Route("api/features")]
public sealed class FeaturesController(KqlAssistant assistant) : ControllerBase
{
    [HttpGet]
    public FeatureFlags Get() => new(assistant.IsEnabled);
}
