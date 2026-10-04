using System.Security.Claims;
using Core.Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Account;

[ApiController]
[Route("api/account/recovery-email")]
[ApiAuthorize]
[ValidateCsrfForCookies]
[EnableRateLimiting("login")]
public sealed class RecoveryEmailController : ControllerBase
{
    private readonly IRecoveryEmailService _recoveryEmailService;
    private readonly IRecoveryProofAuthorizer _authorizer;

    public RecoveryEmailController(
        IRecoveryEmailService recoveryEmailService,
        IRecoveryProofAuthorizer authorizer)
    {
        _recoveryEmailService = recoveryEmailService;
        _authorizer = authorizer;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId();
        if (accountId is null)
        {
            return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        }

        if (!await _authorizer.IsSelfServiceAuthorizedAsync(accountId.Value, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new RecoveryProofApiResponse("highAssuranceRequired"));
        }

        var status = await _recoveryEmailService.GetStatusAsync(accountId.Value, cancellationToken);
        return Ok(new RecoveryEmailStatusResponse(status.IsConfigured, status.IsVerified, status.MaskedAddress));
    }

    [HttpPost("change")]
    public async Task<IActionResult> BeginChange(
        [FromBody] RecoveryEmailChangeApiRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId();
        if (accountId is null)
        {
            return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        }

        var result = await _recoveryEmailService.BeginAuthenticatedChangeAsync(
            new RecoveryEmailChangeRequest(accountId.Value, request.CandidateAddress),
            cancellationToken);
        return SelfServiceOutcome(result.Outcome);
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] RecoveryEmailVerificationApiRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId();
        if (accountId is null)
        {
            return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        }

        var outcome = await _recoveryEmailService.VerifyAuthenticatedAsync(
            new RecoveryEmailVerificationRequest(accountId.Value, request.Code),
            cancellationToken);
        return SelfServiceOutcome(outcome);
    }

    [HttpDelete]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId();
        if (accountId is null)
        {
            return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        }

        return SelfServiceOutcome(
            await _recoveryEmailService.RevokeAuthenticatedAsync(accountId.Value, cancellationToken));
    }

    private IActionResult SelfServiceOutcome(RecoveryProofOutcome outcome)
    {
        var response = new RecoveryProofApiResponse(OutcomeCode(outcome));
        return outcome switch
        {
            RecoveryProofOutcome.Success => Ok(response),
            RecoveryProofOutcome.Unauthorized => StatusCode(StatusCodes.Status403Forbidden, response),
            RecoveryProofOutcome.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }

    private Guid? CurrentAccountId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var accountId) ? accountId : null;
    }

    private static string OutcomeCode(RecoveryProofOutcome outcome) => outcome switch
    {
        RecoveryProofOutcome.Success => "success",
        RecoveryProofOutcome.Invalid => "invalid",
        RecoveryProofOutcome.Expired => "expired",
        RecoveryProofOutcome.Exhausted => "exhausted",
        RecoveryProofOutcome.Replayed => "replayed",
        RecoveryProofOutcome.Cooldown => "cooldown",
        RecoveryProofOutcome.Unauthorized => "highAssuranceRequired",
        RecoveryProofOutcome.Missing => "missing",
        _ => "unavailable"
    };
}

public sealed record RecoveryEmailChangeApiRequest(string CandidateAddress);
public sealed record RecoveryEmailVerificationApiRequest(string Code);
public sealed record RecoveryEmailStatusResponse(bool IsConfigured, bool IsVerified, string? MaskedAddress);
public sealed record RecoveryProofApiResponse(string Outcome, int RetryAfterSeconds = 0);
