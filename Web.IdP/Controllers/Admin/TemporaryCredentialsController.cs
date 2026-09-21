using System.Security.Claims;
using Core.Application.Ports;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/users")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public sealed class TemporaryCredentialsController(IAdminTemporaryCredentialService service) : ControllerBase
{
    [HttpPost("{id:guid}/credential-recovery/issue-temporary-password")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> IssueAsync(
        Guid id,
        [FromBody] TemporaryCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var actorValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (!Guid.TryParse(actorValue, out var actorAccountId))
        {
            return Unauthorized(new { outcome = "unauthorized" });
        }

        var result = await service.IssueAsync(
            new AdminTemporaryCredentialRequest(
                actorAccountId,
                id,
                request.IdentityCheckEvidence,
                request.Reason),
            cancellationToken);
        return result.Outcome switch
        {
            RecoveryProofOutcome.Success => Ok(new
            {
                outcome = "success",
                temporaryPassword = result.TemporaryPassword
            }),
            RecoveryProofOutcome.Unauthorized =>
                StatusCode(StatusCodes.Status403Forbidden, new { outcome = "highAssuranceRequired" }),
            _ => BadRequest(new { outcome = "unavailable" })
        };
    }
}

public sealed record TemporaryCredentialRequest(string IdentityCheckEvidence, string Reason);
