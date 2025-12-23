using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Audit logging endpoints for compliance and monitoring.
/// </summary>
[ApiController]
[Route("api/admin/audit")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    /// <summary>
    /// Get audit events with filtering and pagination.
    /// </summary>
    [HttpGet("events")]
    [HasPermission(Permissions.Audit.Read)]
    public async Task<ActionResult> GetEvents([FromQuery] AuditEventFilterDto filter)
    {
        var (items, totalCount) = await _auditService.GetEventsAsync(filter);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Export a specific audit event.
    /// </summary>
    [HttpGet("events/{id}/export")]
    [HasPermission(Permissions.Audit.Read)]
    public async Task<ActionResult> ExportEvent(int id)
    {
        var exportDto = await _auditService.ExportEventAsync(id);
        if (exportDto == null)
        {
            return NotFound();
        }
        return Ok(exportDto);
    }
}
