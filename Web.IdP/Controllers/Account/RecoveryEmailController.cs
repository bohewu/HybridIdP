using System.Security.Claims;
using Core.Application.Ports;
using Core.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.IdP.Attributes;
using Web.IdP.Helpers;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Controllers.Account;

[ApiController]
[Route("api/account/recovery-email")]
[ApiAuthorize]
[ValidateCsrfForCookies]
[RecoveryEmailCsrf]
[EnableRateLimiting("login")]
public sealed class RecoveryEmailController : ControllerBase
{
    private readonly IRecoveryEmailService _recoveryEmailService;
    private readonly IRecoveryProofAuthorizer _authorizer;
    private readonly IRecoveryEmailPreferenceService? _preferences;
    private readonly RecoveryEmailStepUpService? _stepUp;

    public RecoveryEmailController(
        IRecoveryEmailService recoveryEmailService,
        IRecoveryProofAuthorizer authorizer,
        IRecoveryEmailPreferenceService? preferences = null,
        RecoveryEmailStepUpService? stepUp = null)
    {
        _recoveryEmailService = recoveryEmailService;
        _authorizer = authorizer;
        _preferences = preferences;
        _stepUp = stepUp;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId();
        if (accountId is null)
        {
            return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        }

        if (_preferences is not null)
        {
            var preferenceStatus = await _preferences.GetStatusAsync(accountId.Value, cancellationToken);
            if (preferenceStatus.Mode is "Legacy" or "UseDefault" or "UseCustom" or "Disabled")
                return Ok(preferenceStatus);
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

        if (_preferences?.Enabled == true)
            return SelfServiceOutcome(await _preferences.BeginAsync(accountId.Value, request.CandidateAddress,
                RecoveryReauthenticationSession.Context(HttpContext), cancellationToken));
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

        if (_preferences?.Enabled == true)
            return SelfServiceOutcome(await _preferences.VerifyAsync(accountId.Value, request.Code,
                RecoveryReauthenticationSession.Context(HttpContext), cancellationToken));
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

    [HttpPost("reauthenticate")]
    public async Task<IActionResult> Reauthenticate(CancellationToken cancellationToken)
    {
        var id = CurrentAccountId();
        if (id is null) return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        if (_stepUp?.Enabled != true) return SelfServiceOutcome(RecoveryProofOutcome.Unavailable);
        if (!await _authorizer.IsSelfServiceAuthorizedAsync(id.Value, cancellationToken))
            return SelfServiceOutcome(RecoveryProofOutcome.Unauthorized);
        var state = await _stepUp.ResolveAsync(id.Value, cancellationToken);
        if (state is null) return SelfServiceOutcome(RecoveryProofOutcome.Unavailable);
        if (state.Authority == "external" &&
            (await HttpContext.RequestServices.GetRequiredService<IPasskeyService>().GetUserPasskeysAsync(id.Value, cancellationToken)).Count == 0)
            return StatusCode(StatusCodes.Status403Forbidden, new RecoveryProofApiResponse("hardwareReauthenticationUnavailable"));
        RecoveryReauthenticationSession.Begin(HttpContext, state);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        return Ok(new { loginUrl = "/Account/Login?returnUrl=%2FAccount%2FProfile", hardwareOnly = state.Authority == "external" });
    }

    [HttpPost("resend")]
    public Task<IActionResult> Resend(CancellationToken cancellationToken) => PreferenceActionAsync(
        (id, context) => _preferences!.ResendAsync(id, context, cancellationToken));

    [HttpPost("cancel")]
    public Task<IActionResult> Cancel(CancellationToken cancellationToken) => PreferenceActionAsync(
        (id, context) => _preferences!.CancelAsync(id, context, cancellationToken));

    [HttpPost("use-default/prepare")]
    public async Task<IActionResult> PrepareDefault(CancellationToken cancellationToken)
    {
        var id = CurrentAccountId();
        if (id is null) return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        if (_preferences?.Enabled != true) return SelfServiceOutcome(RecoveryProofOutcome.Unavailable);
        var result = await _preferences.PrepareDefaultAsync(id.Value, RecoveryReauthenticationSession.Context(HttpContext), cancellationToken);
        return result is null ? SelfServiceOutcome(RecoveryProofOutcome.Unavailable) : Ok(result);
    }

    [HttpPost("use-default")]
    public Task<IActionResult> UseDefault([FromBody] RecoveryDefaultApiRequest request, CancellationToken cancellationToken) =>
        PreferenceActionAsync((id, context) => _preferences!.UseDefaultAsync(id, request.Confirmation, context, cancellationToken));

    private async Task<IActionResult> PreferenceActionAsync(Func<Guid, RecoveryPreferenceContext, Task<RecoveryProofOutcome>> operation)
    {
        var id = CurrentAccountId();
        if (id is null) return Unauthorized(new RecoveryProofApiResponse("unauthorized"));
        if (_preferences?.Enabled != true) return SelfServiceOutcome(RecoveryProofOutcome.Unavailable);
        return SelfServiceOutcome(await operation(id.Value, RecoveryReauthenticationSession.Context(HttpContext)));
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

public sealed record RecoveryEmailChangeApiRequest(string CandidateAddress)
{ public override string ToString() => nameof(RecoveryEmailChangeApiRequest); }
public sealed record RecoveryEmailVerificationApiRequest(string Code)
{ public override string ToString() => nameof(RecoveryEmailVerificationApiRequest); }
public sealed record RecoveryDefaultApiRequest(string Confirmation)
{ public override string ToString() => nameof(RecoveryDefaultApiRequest); }
public sealed record RecoveryEmailStatusResponse(bool IsConfigured, bool IsVerified, string? MaskedAddress);
public sealed record RecoveryProofApiResponse(string Outcome, int RetryAfterSeconds = 0);
