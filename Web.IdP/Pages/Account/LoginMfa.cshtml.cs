using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Core.Application;
using Core.Domain;
using Core.Domain.Events;
using System.ComponentModel.DataAnnotations;

namespace Web.IdP.Pages.Account;

public partial class LoginMfaModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaService _mfaService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ILogger<LoginMfaModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LoginMfaModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMfaService mfaService,
        IDomainEventPublisher eventPublisher,
        ILogger<LoginMfaModel> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _mfaService = mfaService;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _localizer = localizer;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }
    
    /// <summary>
    /// Indicates if user also has Email MFA enabled (for showing switch link).
    /// </summary>
    public bool EmailMfaEnabled { get; set; }

    public class InputModel
    {
        [Display(Name = "VerificationCode")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "TotpCodeLength")]
        public string? TotpCode { get; set; }

        [Display(Name = "RecoveryCode")]
        public string? RecoveryCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, bool rememberMe = false)
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login");
        }
        
        // Router Logic:
        // If TOTP (TwoFactorEnabled) is TRUE, show this page (default MFA).
        // If TOTP is FALSE, check Email MFA.
        if (!user.TwoFactorEnabled)
        {
            // If only Email MFA, redirect there
            if (user.EmailMfaEnabled)
            {
                return RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe });
            }
            // If no MFA enabled, back to login (shouldn't be here)
            return RedirectToPage("./Login");
        }

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        EmailMfaEnabled = user.EmailMfaEnabled;
        
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var returnUrl = ReturnUrl ?? Url.Content("~/");

        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login");
        }

        // Check for lockout
        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("User account locked out.");
            return RedirectToPage("./Lockout");
        }

        // Validate that at least one code is provided
        if (string.IsNullOrWhiteSpace(Input.TotpCode) && string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            ModelState.AddModelError(string.Empty, _localizer["EnterCodeOrRecoveryCode"]);
            EmailMfaEnabled = user.EmailMfaEnabled;
            return Page();
        }
        
        // Try TOTP code first
        if (!string.IsNullOrWhiteSpace(Input.TotpCode))
        {
            var isValid = await _mfaService.ValidateTotpCodeAsync(user, Input.TotpCode);
            if (isValid)
            {
                await _signInManager.SignInAsync(user, isPersistent: RememberMe);
                _logger.LogInformation("User logged in with TOTP 2FA.");
                
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: user.Id.ToString(),
                    userName: user.UserName ?? string.Empty,
                    isSuccessful: true,
                    failureReason: null,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                return LocalRedirect(returnUrl);
            }

            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(nameof(Input.TotpCode), _localizer["InvalidMfaCode"]);
            EmailMfaEnabled = user.EmailMfaEnabled;
            return Page();
        }

        // Try recovery code
        if (!string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            var cleanCode = Input.RecoveryCode.Replace(" ", "").Replace("-", "");
            // Use MfaService for custom recovery codes
            var success = await _mfaService.ValidateRecoveryCodeAsync(user, cleanCode);
            
            if (success)
            {
                await _signInManager.SignInAsync(user, isPersistent: RememberMe);
                _logger.LogInformation("User logged in with recovery code.");
                
                var remainingCodes = await _mfaService.CountRecoveryCodesAsync(user);
                if (remainingCodes <= 3)
                {
                    _logger.LogWarning("User {UserName} has only {Count} recovery codes left.", user.UserName, remainingCodes);
                }

                return LocalRedirect(returnUrl);
            }

            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(nameof(Input.RecoveryCode), _localizer["InvalidRecoveryCode"]);
            EmailMfaEnabled = user.EmailMfaEnabled;
            return Page();
        }

        EmailMfaEnabled = user.EmailMfaEnabled;
        return Page();
    }
    
    private async Task<ApplicationUser?> GetTwoFactorUserAsync()
    {
        // Try standard Identity method first
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        
        // Fallback: manually look up user from cookie if Identity method fails (Guid key issue)
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
