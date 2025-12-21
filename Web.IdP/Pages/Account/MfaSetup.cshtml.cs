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

    [BindProperty(SupportsGet = true)]
    public bool GracePeriodExpired { get; set; }

    public int RemainingGraceDays { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login");
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (user.MfaRequirementNotifiedAt != null)
        {
            var expiry = user.MfaRequirementNotifiedAt.Value.AddDays(policy.MfaEnforcementGracePeriodDays);
            RemainingGraceDays = (int)Math.Max(0, (expiry - DateTime.UtcNow).TotalDays);
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
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            var twoFactorPrincipal = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
            if (twoFactorPrincipal.Succeeded && twoFactorPrincipal.Principal != null)
            {
                var userIdClaim = twoFactorPrincipal.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    user = await _userManager.FindByIdAsync(userId.ToString());
                }
            }
        }
        return user;
    }
}
