using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;
using Web.IdP.Services;
using Core.Application;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Controller for impersonation operations.
/// Separated from UsersController to avoid controller-level CSRF validation
/// that blocks the stop-impersonation endpoint (cookie auth needs special handling).
/// </summary>
[ApiController]
[Route("api/impersonation")]
[ValidateCsrfForCookies]
public class ImpersonationController : ControllerBase
{
    private readonly Web.IdP.Services.ICurrentUserLifecycleEligibility _lifecycleEligibility;
    private readonly IImpersonationService _impersonationService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ImpersonationController> _logger;

    public ImpersonationController(
        Web.IdP.Services.ICurrentUserLifecycleEligibility lifecycleEligibility,
        IImpersonationService impersonationService,
        ILogger<ImpersonationController> logger,
        IAuditService auditService)
    {
        _lifecycleEligibility = lifecycleEligibility;
        _impersonationService = impersonationService;
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// Reverts impersonation and restores the original identity.
    /// This endpoint requires cookie authentication only (not Bearer tokens)
    /// because impersonation state is stored in the authentication cookie.
    /// </summary>
    [HttpPost("stop")]
    [Authorize(AuthenticationSchemes = "Identity.Application")]  // Cookie auth only (same as IdentityConstants.ApplicationScheme)
    public async Task<IActionResult> Stop()
    {
        _logger.LogInformation("[ImpersonationController.Stop] Entering action");
        
        try
        {
            var (success, principal, error) = await _impersonationService.RevertImpersonationAsync(User);
            _logger.LogInformation("[ImpersonationController.Stop] Service result: Success={Success}, Error={Error}", success, error ?? "none");

            if (!success)
            {
                _logger.LogWarning("[ImpersonationController.Stop] Failed: {Error}", error);
                if (error == "Original user not found")
                {
                    // Force logout
                    await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return Ok(new { message = "Original user not found, logged out." });
                }
                return BadRequest(new { error });
            }

            var restoredUserId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal?.FindFirst("sub")?.Value;
            if (!Guid.TryParse(restoredUserId, out var userId) ||
                !await _lifecycleEligibility.IsEligibleAsync(userId, HttpContext.RequestAborted))
            {
                return BadRequest(new { error = "User cannot sign in" });
            }

            // Restore the cookie
            await _auditService.LogImpersonationEventAsync("ImpersonationStopped", User,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), HttpContext.RequestAborted);
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal!, new AuthenticationProperties
            {
                IsPersistent = false
            });

            return Ok(new { message = "Impersonation stopped successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImpersonationController.Stop] Exception occurred");
            return StatusCode(500, new { error = "An error occurred while stopping impersonation", details = ex.Message });
        }
    }
}
