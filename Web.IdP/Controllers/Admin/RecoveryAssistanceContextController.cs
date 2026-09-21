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
public sealed class RecoveryAssistanceContextController(
    IRecoveryAssistanceContextService contextService) : ControllerBase
{
    [HttpGet("{id:guid}/credential-recovery/context")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (!TryGetActorAccountId(out var actorAccountId))
        {
            return Unauthorized(new { outcome = "unauthorized" });
        }

        var result = await contextService.GetAsync(actorAccountId, id, cancellationToken);
        return result switch
        {
            { Outcome: RecoveryAssistanceContextOutcome.Available, Context: { } context } => Ok(context),
            { Outcome: RecoveryAssistanceContextOutcome.Unauthorized } =>
                StatusCode(StatusCodes.Status403Forbidden, new { outcome = "highAssuranceRequired" }),
            _ => BadRequest(new { outcome = "unavailable" })
        };
    }

    private bool TryGetActorAccountId(out Guid actorAccountId)
    {
        var actorValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        return Guid.TryParse(actorValue, out actorAccountId);
    }
}
