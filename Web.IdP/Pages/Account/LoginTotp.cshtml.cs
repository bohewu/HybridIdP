using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Events;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Web.IdP.Pages.Account;

public partial class LoginTotpModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaService _mfaService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ILogger<LoginTotpModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LoginTotpModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMfaService mfaService,
        IDomainEventPublisher eventPublisher,
        ILogger<LoginTotpModel> logger,
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
        
        // Verify user actually has TOTP enabled
        if (!user.TwoFactorEnabled)
        {
            // If only Email MFA, redirect there
            if (user.EmailMfaEnabled)
            {
                return RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe });
            }
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
                AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.Mfa);
                AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.Otp);

                // Create the principal with the new claims for the cookie
                 var claims = new List<System.Security.Claims.Claim>
                {
                    new System.Security.Claims.Claim("amr", Core.Domain.Constants.AuthConstants.Amr.Mfa),
                    new System.Security.Claims.Claim("amr", Core.Domain.Constants.AuthConstants.Amr.Otp)
                };

                await _signInManager.SignInWithClaimsAsync(user, RememberMe, claims);
                
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
            var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, cleanCode);
            
            if (result.Succeeded)
            {
                AddAmrToSession(AuthConstants.Amr.Mfa); // Recovery code is still mfa, but not otp? Actually design says "mfa" for recovery.

                await _signInManager.SignInAsync(user, isPersistent: RememberMe);
                _logger.LogInformation("User logged in with recovery code.");
                
                var remainingCodes = await _userManager.CountRecoveryCodesAsync(user);
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

    private void AddAmrToSession(string amr)
    {
        var currentAmrJson = HttpContext.Session.GetString("AuthenticationMethods");
        List<string> amrList = string.IsNullOrEmpty(currentAmrJson) 
            ? new List<string>() 
            : JsonSerializer.Deserialize<List<string>>(currentAmrJson) ?? new List<string>();
        
        if (!amrList.Contains(amr))
        {
            amrList.Add(amr);
            HttpContext.Session.SetString("AuthenticationMethods", JsonSerializer.Serialize(amrList));
        }
    }
}
