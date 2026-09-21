using System.Security.Claims;
using Core.Application.Ports;
using Core.Domain.Constants;
using Core.Domain.Entities;
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
public sealed class DirectoryRecoveryReconciliationController(
    IDirectoryCredentialOperatorResolutionService operatorResolutionService) : ControllerBase
{
    [HttpPost("{id:guid}/credential-recovery/reconcile-directory")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ReconcileAsync(
        Guid id,
        [FromBody] DirectoryRecoveryReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return BadRequest(new { outcome = "unavailable" });
    }

    [HttpGet("{id:guid}/credential-recovery/pending-directory-operation")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> GetPendingDirectoryOperationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (!TryGetActorAccountId(out var actorAccountId))
        {
            return Unauthorized(new { outcome = "unauthorized" });
        }

        var result = await operatorResolutionService.GetPendingAsync(
            actorAccountId,
            id,
            cancellationToken);
        return result switch
        {
            { Outcome: DirectoryCredentialOperatorResolutionOutcome.Available, Attempt: { } attempt } => Ok(new
            {
                outcome = "available",
                attemptId = attempt.AttemptId,
                version = attempt.Version,
                operationKind = attempt.OperationKind.ToString(),
                status = attempt.Status.ToString(),
                directoryObjectId = attempt.DirectoryObjectId
            }),
            { Outcome: DirectoryCredentialOperatorResolutionOutcome.Unauthorized } =>
                StatusCode(StatusCodes.Status403Forbidden, new { outcome = "highAssuranceRequired" }),
            _ => BadRequest(new { outcome = "unavailable" })
        };
    }

    [HttpPost("{id:guid}/credential-recovery/resolve-directory-operation")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResolveDirectoryOperationAsync(
        Guid id,
        [FromBody] DirectoryCredentialOperatorResolutionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return BadRequest(new { outcome = "unavailable" });
    }

    [HttpPost("{id:guid}/credential-recovery/prepare-directory-settlement")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> PrepareDirectorySettlementAsync(
        Guid id,
        [FromBody] DirectorySettlementPreparationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (!TryGetActorAccountId(out var actorAccountId))
        {
            return Unauthorized(new { outcome = "unauthorized" });
        }
        if (!Enum.TryParse<NativeDirectoryCredentialOperationKind>(request.OperationKind, false, out var operationKind) ||
            !Enum.TryParse<NativeDirectoryRecoveryStatus>(request.ExpectedStatus, false, out var expectedStatus) ||
            !Enum.TryParse<DirectorySettlementDisposition>(request.Disposition, false, out var disposition) ||
            !Enum.TryParse<DirectorySettlementEvidenceCategory>(request.EvidenceCategory, false, out var evidenceCategory))
        {
            return BadRequest(new { outcome = "unavailable" });
        }

        var result = await operatorResolutionService.PrepareAsync(
            new DirectorySettlementPreparationRequest(actorAccountId, id, request.AttemptId, request.ExpectedVersion,
                operationKind, expectedStatus, request.DirectoryObjectId, request.OriginalWritersDrained,
                disposition, evidenceCategory, request.EvidenceReference), cancellationToken);
        return result.Outcome switch
        {
            DirectoryCredentialOperatorResolutionOutcome.Available =>
                Ok(new { outcome = "prepared", preparationId = result.PreparationId }),
            DirectoryCredentialOperatorResolutionOutcome.Unauthorized =>
                StatusCode(StatusCodes.Status403Forbidden, new { outcome = "highAssuranceRequired" }),
            _ => BadRequest(new { outcome = "unavailable" })
        };
    }

    [HttpPost("credential-recovery/directory-settlement/{preparationId:guid}/cancel")]
    [HasPermission(Permissions.Users.Update)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> CancelDirectorySettlementAsync(
        Guid preparationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorAccountId(out var actorAccountId))
        {
            return Unauthorized(new { outcome = "unauthorized" });
        }
        var outcome = await operatorResolutionService.CancelAsync(actorAccountId, preparationId, cancellationToken);
        return outcome switch
        {
            DirectoryCredentialOperatorResolutionOutcome.Resolved => Ok(new { outcome = "cancelled" }),
            DirectoryCredentialOperatorResolutionOutcome.Unauthorized =>
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

public sealed record DirectoryRecoveryReconciliationRequest(string IntendedPassword);

public sealed record DirectoryCredentialOperatorResolutionApiRequest(
    Guid AttemptId,
    long ExpectedVersion,
    string OperationKind,
    string ExpectedStatus,
    Guid DirectoryObjectId,
    string CurrentCredential,
    bool OriginalWritersSettled,
    bool DirectoryOperationSettled,
    string IdentityCheckEvidence,
    string Reason);

public sealed record DirectorySettlementPreparationApiRequest(
    Guid AttemptId,
    long ExpectedVersion,
    string OperationKind,
    string ExpectedStatus,
    Guid DirectoryObjectId,
    bool OriginalWritersDrained,
    string Disposition,
    string EvidenceCategory,
    string EvidenceReference);
