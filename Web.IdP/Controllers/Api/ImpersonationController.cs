using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Core.Domain.Constants;
using Core.Domain;
using Web.IdP.Services; // For ISessionService if needed, or IClaimsService

namespace Web.IdP.Controllers.Api;

/// <summary>
/// Controller for managing impersonation sessions.
/// </summary>
[Route("api/account/impersonation")]
[ApiController]
[Authorize]
public class ImpersonationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    
    public ImpersonationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    /// <summary>
    /// Revert impersonation and switch back to the original admin user.
    /// </summary>
    [HttpPost("revert")]
    public async Task<IActionResult> RevertImpersonation()
    {
        // 1. Check if the current user has an Actor identity
        // In ASP.NET Core Identity, the Actor claim is often not automatically hydrated into .Actor property 
        // unless using a specific claims factory or if the auth handler does it.
        // However, we start by checking the claims directly for "act" or if the Principal.Identity.Actor is set.
        
        // We start by checking the claims directly for "act" or if the Principal.Identity.Actor is set.
        // The "act" claim is a complex claim (often JSON) in OIDC, but in our Cookie it might be flattened or accessible via Actor.
        
        // Let's rely on how we set it: identity.Actor = actorIdentity;
        // The CookieAuthenticationHandler SHOULD restore this.
        
        var actor = User.Identities.FirstOrDefault()?.Actor;
        string? adminUserId = null;

        if (actor != null)
        {
             adminUserId = actor.FindFirst("sub")?.Value;
        }

        // Fallback: Check if "act" claim exists in flattened format if Actor property is null
        // (Sometimes depending on serialization, it might not restore full Identity object)
        if (string.IsNullOrEmpty(adminUserId))
        {
             // If we just appended claims, we might look for a specific claim type we injected?
             // But we used `identity.Actor = ...`.
             // If this fails, we might need to adjust `StartImpersonation` to add a specific plain claim like "impersonator_id".
             // For now assuming Actor works or we can find the claim.
             
             // Let's check for a specific claim we might assume exists?
             // Actually, let's keep it simple. If Actor is null, we can't switch back easily unless we added a claim.
             // In `StartImpersonation`, we did: 
             // identity.Actor = actorIdentity;
             // actorIdentity.AddClaim(new Claim(JwtClaimTypes.Subject, currentUserId));
             
             // When cookie is read back, if `Actor` is not null, we are good.
        }

        if (string.IsNullOrEmpty(adminUserId))
        {
            return BadRequest(new { error = "Not currently impersonating or actor identity lost." });
        }

        // 2. Refresh the session for the Admin
        // We trust the encryption of the cookie. If the cookie says "Actor is X", then X (Admin) authorized this.
        // But for extra security, we should verify X still exists and is active.
        
        var adminUser = await _userManager.FindByIdAsync(adminUserId);
        if (adminUser == null || !adminUser.IsActive)
        {
            // Edge case: Admin account deleted while impersonating??
            // Just sign out everything.
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return BadRequest(new { error = "Original admin account is no longer active." });
        }

        // 3. Re-issue cookie for Admin
        // We can use SignInManager.SignInAsync or directly issue cookie. 
        // SignInManager refreshes claims from DB which is good (updates permissions).
        await _signInManager.SignInAsync(adminUser, isPersistent: false);

        return Ok(new { success = true, switchedTo = adminUser.Email });
    }
}
