using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/claims-preview")]
[Authorize(AuthenticationSchemes = "Identity.Application")]
[HasPermission(Permissions.Claims.Preview)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClaimsPreviewController(IClaimsPreviewService preview, IAuditService audit) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Preview([FromBody] ClaimsPreviewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await preview.PreviewAsync(request, cancellationToken);
            await audit.LogAdministrativeEventAsync(AuditEventTypes.ClaimsPreviewed, "User", result.UserId.ToString(),
                JsonSerializer.Serialize(new { clientId = result.ClientId, scopeCount = result.Scopes.Length,
                    hypotheticalScopes = true, sourceCount = result.Sources.Count,
                    profileReadMode = "live_read_only" }), cancellationToken);
            return Ok(result);
        }
        catch (ClaimsPreviewException error) { return BadRequest(new { code = error.Code }); }
    }

    [HttpGet("clients")]
    public async Task<IActionResult> Clients([FromQuery] string? search = null, [FromQuery] int skip = 0,
        [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        try { return Ok(new { items = await preview.GetClientsAsync(search, skip, take, cancellationToken) }); }
        catch (ClaimsPreviewException error) { return BadRequest(new { code = error.Code }); }
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts([FromQuery] string? search = null, [FromQuery] int skip = 0,
        [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        try { return Ok(new { items = await preview.GetAccountsAsync(search, skip, take, cancellationToken) }); }
        catch (ClaimsPreviewException error) { return BadRequest(new { code = error.Code }); }
    }

    [HttpGet("scopes")]
    public async Task<IActionResult> Scopes([FromQuery] string clientId, CancellationToken cancellationToken)
    {
        try { return Ok(await preview.GetScopesAsync(clientId, cancellationToken)); }
        catch (ClaimsPreviewException error) { return BadRequest(new { code = error.Code }); }
    }
}
