using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Core.Domain;
using Core.Application;
using Core.Application.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Web.IdP.Pages.Account;

public class MfaSetupModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ISecurityPolicyService _securityPolicyService;

    public MfaSetupModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer<SharedResource> localizer,
        ISecurityPolicyService securityPolicyService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _localizer = localizer;
        _securityPolicyService = securityPolicyService;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    // Handled internally, not bound from URL
    public bool GracePeriodExpired { get; private set; }

    public int RemainingGraceDays { get; private set; }
    public bool IsMfaEnforced { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            _localizer["User not found or not authenticated."].ToString(); // Diagnostic hint
            return RedirectToPage("./Login");
        }

        _signInManager.Context.Items["MfaEnforcementUser"] = user; // Internal tracking
        
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        // Default to expired if policy enforces it, until we prove otherwise
        GracePeriodExpired = false;

        if (policy.EnforceMandatoryMfaEnrollment)
        {
             if (user.MfaRequirementNotifiedAt != null)
             {
                var expiry = user.MfaRequirementNotifiedAt.Value.AddDays(policy.MfaEnforcementGracePeriodDays);
                RemainingGraceDays = (int)Math.Max(0, (expiry - DateTime.UtcNow).TotalDays);
                
                if (DateTime.UtcNow > expiry)
                {
                    GracePeriodExpired = true;
                }
             }
             // If NotifiedAt is null, it means they just got flagged, so grace period starts now (not expired)
        }

        // UX Improvement: If acr_values=mfa was requested, MFA is enforced for this session.
        // Hide skip button and show "Enforced" message.
        // Read from session (set by AuthorizationService) for security - no URL tampering possible
        IsMfaEnforced = HttpContext.Session.GetString("MfaEnforcedByAcr") == "true";
        if (IsMfaEnforced)
        {
            GracePeriodExpired = true;
            // Clear the session flag after reading (one-time use)
            HttpContext.Session.Remove("MfaEnforcedByAcr");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSkipAsync()
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login");
        }

        // SECURITY FIX: Re-validate grace period server-side
        // Do not trust the GracePeriodExpired bind property
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (policy.EnforceMandatoryMfaEnrollment && user.MfaRequirementNotifiedAt != null)
        {
             var expiry = user.MfaRequirementNotifiedAt.Value.AddDays(policy.MfaEnforcementGracePeriodDays);
             if (DateTime.UtcNow > expiry)
             {
                 // Grace period expired, cannot skip
                 return Page(); 
             }
        }

        // Sign in user temporarily since they skipped MFA for now (within grace period)
        await _signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(ReturnUrl ?? Url.Content("~/"));
    }

    private async Task<ApplicationUser?> GetTwoFactorUserAsync()
    {
        // First try the standard Identity TFA state (stored in a cookie by SignInManager)
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user != null) return user;

        // If not in TFA state, check if the user is already fully authenticated (step-up enrollment scenario)
        if (User.Identity?.IsAuthenticated == true)
        {
            user = await _userManager.GetUserAsync(User);
            if (user != null) return user;
        }

        // Fallback for manual check of the 2FA principal
        var twoFactorPrincipal = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (twoFactorPrincipal.Succeeded && twoFactorPrincipal.Principal != null)
        {
            var userIdClaim = twoFactorPrincipal.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                user = await _userManager.FindByIdAsync(userId.ToString());
            }
        }
        
        return user;
    }
}
