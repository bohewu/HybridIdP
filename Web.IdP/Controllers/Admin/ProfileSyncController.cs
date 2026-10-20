using System.Security.Claims;
using System.Text.Json;
using Core.Application.Ports;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/profile-sync")]
[ApiAuthorize]
[ValidateCsrfForCookies]
[Authorize(Policy = Permissions.Users.Update)]
[Authorize(Policy = Permissions.Persons.Update)]
public sealed class ProfileSyncController(IAdminProfileSyncService sync, IAdministrativeAuthorizationBoundary boundary)
    : ControllerBase
{
    [HttpGet("status")]
    public Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(async _ => Ok(await sync.GetStatusAsync(cancellationToken)));

    [HttpPost("users/{id:guid}/preview")]
    public Task<IActionResult> PreviewAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async actor => Ok(await sync.PreviewAsync(actor, id, cancellationToken)));

    [HttpPost("users/{id:guid}/apply")]
    public Task<IActionResult> ApplyAsync(Guid id, ProfileSyncApplyRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async actor => Ok(await sync.ApplyAsync(actor, id, request.PreviewToken, cancellationToken)));

    private async Task<IActionResult> ExecuteAsync(Func<string, Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var authority = await boundary.ResolveAsync();
        if (authority is null) return Unauthorized(new { error = "unauthorized" });
        var principal = authority.Principal;
        var id = authority.IsBearer ? principal.FindFirst("sub")?.Value :
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(id)) return Unauthorized(new { error = "unauthorized" });
        var actor = JsonSerializer.Serialize(new { authority.IsBearer, Id = id,
            ApplicationId = principal.FindFirst(AdministrativeClientGrant.ApplicationClaim)?.Value,
            Impersonator = principal.FindFirst("impersonator_id")?.Value,
            ActorId = principal.Identities.Select(x => x.Actor?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                x.Actor?.FindFirst("sub")?.Value).FirstOrDefault(x => x is not null) });
        try { return await action(actor); }
        catch (ProfileSyncException exception)
        {
            return StatusCode(exception.Code switch
            {
                "user_not_found" or "source_not_found" => 404,
                "preview_invalid" or "preview_stale" => 409,
                "source_unavailable" or "source_invalid_response" or "transaction_unavailable" => 503,
                "unauthorized" => 401,
                _ => 400
            }, new { error = exception.Code });
        }
        catch (NativeFieldPolicyException)
        {
            return Conflict(new { error = "invalid_stored_state" });
        }
    }
}
