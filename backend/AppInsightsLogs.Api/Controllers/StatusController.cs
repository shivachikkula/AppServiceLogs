using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController(AppInsightsQueryClient client, IOptions<AppInsightsOptions> options) : ControllerBase
{
    [HttpGet]
    public StatusResponse Get() => new(
        client.IsConfigured,
        client.ApplicationId,
        CredentialFactory.Describe(options.Value),
        options.Value.QueryEndpoint);
}
