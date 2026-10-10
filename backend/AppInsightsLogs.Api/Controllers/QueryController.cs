using System.Net;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

/// <summary>User-written KQL against the selected application, its schema for IntelliSense, and the AI assistant.</summary>
[ApiController]
[Route("api/query")]
public sealed class QueryController(QueryService queries, KqlAssistant assistant, ApplicationCatalog catalog)
    : TelemetryControllerBase(catalog)
{
    [HttpGet("schema")]
    public Task<ActionResult<KqlSchema>> GetSchema([FromQuery] string? appKey, CancellationToken cancellationToken) =>
        Run(appKey, applicationId => queries.GetSchemaAsync(applicationId, cancellationToken), cancellationToken);

    [HttpPost("run")]
    public Task<ActionResult<KqlResult>> RunQuery([FromBody] RunQueryRequest request, CancellationToken cancellationToken) =>
        Run(request.AppKey, applicationId => queries.RunAsync(applicationId, request.Query ?? string.Empty, request.RangeMinutes, cancellationToken), cancellationToken);

    [HttpPost("generate")]
    public Task<ActionResult<GeneratedQuery>> Generate([FromBody] GenerateQueryRequest request, CancellationToken cancellationToken) =>
        Run(request.AppKey, async applicationId =>
        {
            if (!assistant.IsEnabled)
            {
                throw new AppInsightsQueryException(HttpStatusCode.NotFound, "The AI query assistant is not configured.", "AssistantDisabled");
            }
            var schema = await queries.GetSchemaAsync(applicationId, cancellationToken);
            return await assistant.GenerateAsync(request, schema, cancellationToken);
        }, cancellationToken);
}

[ApiController]
[Authorize]
[Route("api/features")]
public sealed class FeaturesController(KqlAssistant assistant) : ControllerBase
{
    [HttpGet]
    public FeatureFlags Get() => new(assistant.IsEnabled);
}
