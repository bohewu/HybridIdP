using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Core.Domain;
using Core.Application;
using Core.Application.Interfaces;
using System.ComponentModel.DataAnnotations;
using Web.IdP.Helpers;

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
    public bool ShowGracePeriod { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            _localizer["User not found or not authenticated."].ToString(); // Diagnostic hint
            return RedirectToPage("./Login");
        }

        await PromotePartialPrincipalForAntiforgeryAsync(user);
        _signInManager.Context.Items["MfaEnforcementUser"] = user; // Internal tracking
        
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        GracePeriodExpired = false;

        if (policy.EnforceMandatoryMfaEnrollment)
        {
            if (user.MfaRequirementNotifiedAt != null)
            {
                var now = DateTime.UtcNow;
                var expiry = user.MfaRequirementNotifiedAt.Value.AddDays(policy.MfaEnforcementGracePeriodDays);
                var remaining = expiry - now;

                if (remaining <= TimeSpan.Zero)
                {
                    GracePeriodExpired = true;
                }
                else
                {
                    RemainingGraceDays = Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));
                }
            }
            else
            {
                // The user was just flagged; show the configured grace period from its start.
                RemainingGraceDays = Math.Max(0, policy.MfaEnforcementGracePeriodDays);
                GracePeriodExpired = RemainingGraceDays == 0;
            }
        }

        // UX Improvement: If acr_values=mfa was requested, MFA is enforced for this session.
        // Hide skip button and show "Enforced" message.
        // Read from session (set by AuthorizationService) for security - no URL tampering possible
        // Note: We do NOT clear the flag here - it will be cleared when user completes MFA setup and is redirected away
        IsMfaEnforced = HttpContext.Session.GetString("MfaEnforcedByAcr") == "true";
        if (IsMfaEnforced)
        {
            GracePeriodExpired = true;
        }

        ShowGracePeriod = policy.EnforceMandatoryMfaEnrollment && !IsMfaEnforced;

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
        return this.SafeRedirect(ReturnUrl, "~/");
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

    private async Task PromotePartialPrincipalForAntiforgeryAsync(ApplicationUser user)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return;
        }

        var partialAuthentication =
            await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var partialPrincipal = partialAuthentication.Principal;
        var userId = partialPrincipal?.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (partialAuthentication.Succeeded &&
            partialPrincipal?.Identity?.IsAuthenticated == true &&
            Guid.TryParse(userId, out var partialUserId) &&
            partialUserId == user.Id)
        {
            HttpContext.User = partialPrincipal;
        }
    }
}
