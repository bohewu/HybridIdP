using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Controller for impersonation operations.
/// Separated from UsersController to avoid controller-level CSRF validation
/// that blocks the stop-impersonation endpoint (cookie auth needs special handling).
/// </summary>
[ApiController]
[Route("api/impersonation")]
public class ImpersonationController : ControllerBase
{
    private readonly IImpersonationService _impersonationService;
    private readonly ILogger<ImpersonationController> _logger;

    public ImpersonationController(
        IImpersonationService impersonationService,
        ILogger<ImpersonationController> logger)
    {
        _impersonationService = impersonationService;
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

            // Restore the cookie
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
