using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppInsightsLogs.Api.Controllers;

/// <summary>Admin page: access grants, applications, audit log and go-live diagnostics. Admins: Administration:AdminEmails.</summary>
[ApiController]
[Authorize] // the default policy (valid B2C token, required scope) ...
[Authorize(Policy = AdminPolicy)] // ... and an admin email
[Route("api/admin")]
public sealed class AdminController(AdminService admin, DiagnosticsService diagnostics) : ControllerBase
{
    public const string AdminPolicy = "Admin";

    [HttpGet("grants")]
    public Task<ActionResult<IReadOnlyList<AdminGrant>>> GetGrants([FromQuery] string? search, CancellationToken cancellationToken) =>
        Handle(() => admin.ListGrantsAsync(search, cancellationToken));

    [HttpPost("grants")]
    public Task<ActionResult<AdminGrant>> CreateGrant([FromBody] CreateGrantRequest request, CancellationToken cancellationToken) =>
        Handle(() => admin.CreateGrantAsync(request, cancellationToken));

    [HttpDelete("grants/{id:int}")]
    public async Task<IActionResult> DeleteGrant(int id, CancellationToken cancellationToken) =>
        await admin.DeleteGrantAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("applications")]
    public Task<ActionResult<IReadOnlyList<AdminApplication>>> GetApplications(CancellationToken cancellationToken) =>
        Handle(() => admin.ListApplicationsAsync(cancellationToken));

    [HttpPut("applications/{appKey}")]
    public Task<ActionResult<int>> RenameApplication(string appKey, [FromBody] RenameApplicationRequest request, CancellationToken cancellationToken) =>
        Handle(() => admin.RenameApplicationAsync(appKey, request.ApplicationName, cancellationToken));

    [HttpPost("applications/{appKey}/check")]
    public Task<ActionResult<ApplicationCheck>> CheckApplication(string appKey, CancellationToken cancellationToken) =>
        Handle(() => admin.CheckApplicationAsync(appKey, cancellationToken));

    [HttpGet("audit")]
    public Task<ActionResult<IReadOnlyList<AuditEntryDto>>> GetAudit(
        [FromQuery] string? email,
        [FromQuery] string? appKey,
        [FromQuery] bool failedOnly = false,
        [FromQuery] long? beforeId = null,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Handle(() => admin.ListAuditAsync(email, appKey, failedOnly, beforeId, take, cancellationToken));

    [HttpGet("diagnostics")]
    public Task<ActionResult<IReadOnlyList<DiagnosticCheck>>> GetDiagnostics(CancellationToken cancellationToken) =>
        Handle(() => diagnostics.RunAsync(cancellationToken));

    private async Task<ActionResult<T>> Handle<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (AppInsightsQueryException ex)
        {
            return Problem(title: ex.Code, detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            return Problem(title: "DatabaseError", detail: AppInsightsQueryClient.FlattenMessages(ex), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
