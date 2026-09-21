using System.Security.Cryptography;
using System.Text;
using Core.Application.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Web.IdP.Controllers.Account;

[ApiController]
[AllowAnonymous]
[Route("api/account/pending-directory-settlement")]
public sealed class PendingDirectorySettlementController(
    IDirectoryCredentialOperatorResolutionService settlementService) : ControllerBase
{
    private const string PreparationKey = "PendingDirectorySettlement.Preparation";
    private const string ContextKey = "PendingDirectorySettlement.Context";
    private const string CsrfKey = "PendingDirectorySettlement.Csrf";

    [HttpPost("claim")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ClaimAsync(
        [FromBody] PendingDirectorySettlementClaimApiRequest request,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var context = CreateContext();
        var result = await settlementService.ClaimAsync(
            new DirectorySettlementClaimRequest(request.Continuation, context), cancellationToken);
        if (result is { Outcome: DirectorySettlementVerificationOutcome.ChallengeIssued, PreparationId: { } preparationId })
        {
            HttpContext.Session.SetString(PreparationKey, preparationId.ToString());
            return Ok(new { outcome = "verificationRequired" });
        }

        ClearContext();
        return BadRequest(new { outcome = "unavailable" });
    }

    [HttpPost("verify")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyAsync(
        [FromBody] PendingDirectorySettlementVerificationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (!TryGetContext(out var preparationId, out var context))
        {
            return BadRequest(new { outcome = "unavailable" });
        }

        var outcome = await settlementService.VerifyAndFinalizeAsync(
            new DirectorySettlementVerificationRequest(
                preparationId, request.OwnershipCode, request.CurrentCredential, context), cancellationToken);
        if (outcome == DirectorySettlementVerificationOutcome.Resolved)
        {
            ClearContext();
            return Ok(new { outcome = "resolved" });
        }
        return BadRequest(new { outcome = "unavailable" });
    }

    [HttpPost("cancel")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> CancelAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetContext(out var preparationId, out var context))
            return BadRequest(new { outcome = "unavailable" });
        var outcome = await settlementService.CancelUserAsync(preparationId, context, cancellationToken);
        if (outcome == DirectorySettlementVerificationOutcome.Resolved)
        {
            ClearContext();
            return Ok(new { outcome = "cancelled" });
        }
        return BadRequest(new { outcome = "unavailable" });
    }

    private NativeRecoveryContext CreateContext()
    {
        var browserContext = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var csrfContext = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        HttpContext.Session.SetString(ContextKey, browserContext);
        HttpContext.Session.SetString(CsrfKey, csrfContext);
        return new NativeRecoveryContext(Hash($"{HttpContext.Session.Id}:{browserContext}"), Hash(csrfContext));
    }

    private bool TryGetContext(out Guid preparationId, out NativeRecoveryContext context)
    {
        var preparationValue = HttpContext.Session.GetString(PreparationKey);
        var browserContext = HttpContext.Session.GetString(ContextKey);
        var csrfContext = HttpContext.Session.GetString(CsrfKey);
        if (!Guid.TryParse(preparationValue, out preparationId) || string.IsNullOrWhiteSpace(browserContext) ||
            string.IsNullOrWhiteSpace(csrfContext))
        {
            context = default!;
            return false;
        }
        context = new NativeRecoveryContext(Hash($"{HttpContext.Session.Id}:{browserContext}"), Hash(csrfContext));
        return true;
    }

    private void ClearContext()
    {
        HttpContext.Session.Remove(PreparationKey);
        HttpContext.Session.Remove(ContextKey);
        HttpContext.Session.Remove(CsrfKey);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record PendingDirectorySettlementClaimApiRequest(string Continuation);
public sealed record PendingDirectorySettlementVerificationApiRequest(string OwnershipCode, string CurrentCredential);
