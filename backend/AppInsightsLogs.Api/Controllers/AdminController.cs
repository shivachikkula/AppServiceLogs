using AppInsightsLogs.Api.Data;
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
public sealed class AdminController(AdminService admin, DiagnosticsService diagnostics, AuditLog audit, ILogger<AdminController> logger) : ControllerBase
{
    public const string AdminPolicy = "Admin";

    [HttpGet("grants")]
    public Task<ActionResult<IReadOnlyList<AdminGrant>>> GetGrants([FromQuery] string? search, CancellationToken cancellationToken) =>
        Handle(() => admin.ListGrantsAsync(search, cancellationToken));

    [HttpPost("grants")]
    public Task<ActionResult<AdminGrant>> CreateGrant([FromBody] CreateGrantRequest request, CancellationToken cancellationToken) =>
        Handle(async () =>
        {
            var grant = await admin.CreateGrantAsync(request, cancellationToken);
            await RecordAsync(AuditActions.AdminGrant, grant.AppKey, $"Granted {grant.Email} access to {grant.ApplicationName}", cancellationToken);
            return grant;
        });

    [HttpDelete("grants/{id:int}")]
    public async Task<IActionResult> DeleteGrant(int id, CancellationToken cancellationToken)
    {
        var removed = await admin.DeleteGrantAsync(id, cancellationToken);
        if (removed is null)
        {
            return NotFound();
        }
        await RecordAsync(AuditActions.AdminRevoke, removed.AppKey, $"Removed {removed.Email}'s access to {removed.ApplicationName}", cancellationToken);
        return NoContent();
    }

    [HttpGet("applications")]
    public Task<ActionResult<IReadOnlyList<AdminApplication>>> GetApplications(CancellationToken cancellationToken) =>
        Handle(() => admin.ListApplicationsAsync(cancellationToken));

    [HttpPut("applications/{appKey}")]
    public Task<ActionResult<int>> RenameApplication(string appKey, [FromBody] RenameApplicationRequest request, CancellationToken cancellationToken) =>
        Handle(async () =>
        {
            var count = await admin.RenameApplicationAsync(appKey, request.ApplicationName, cancellationToken);
            await RecordAsync(AuditActions.AdminRename, appKey, $"Renamed application to {request.ApplicationName?.Trim()} ({count} grants)", cancellationToken);
            return count;
        });

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

    /// <summary>Admin changes go to the same audit log as queries, and to the application log.</summary>
    private Task RecordAsync(string action, string appKey, string text, CancellationToken cancellationToken)
    {
        var email = UserClaims.GetEmail(User) ?? "(no email claim)";
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Admin change by {Admin}: {Action} {AppKey}: {Text}", email, action, appKey, text);
        }
        return audit.RecordAsync(new QueryAuditEntry
        {
            Email = email,
            AppKey = appKey,
            Action = action,
            Text = text,
            Succeeded = true,
        }, cancellationToken);
    }

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
