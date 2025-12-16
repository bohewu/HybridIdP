using System.ComponentModel.DataAnnotations;
using Core.Application;
using Core.Domain;
using Core.Domain.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Web.IdP.Pages.Account;

/// <summary>
/// MFA verification page shown after successful username/password authentication
/// when the user has 2FA enabled.
/// </summary>
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
    /// Indicates if user has Email MFA enabled (used to show Email option on UI).
    /// </summary>
    public bool EmailMfaEnabled { get; set; }
    
    /// <summary>
    /// Indicates if user has TOTP MFA enabled (used to show TOTP option on UI).
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    public class InputModel
    {
        [Display(Name = "VerificationCode")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "TotpCodeLength")]
        public string? TotpCode { get; set; }

        [Display(Name = "RecoveryCode")]
        public string? RecoveryCode { get; set; }
        
        [Display(Name = "EmailCode")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "EmailCodeLength")]
        public string? EmailCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, bool rememberMe = false)
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
        
        if (user == null)
        {
            return RedirectToPage("./Login");
        }

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        
        // Check if user has Email MFA or TOTP enabled
        EmailMfaEnabled = user.EmailMfaEnabled;
        TwoFactorEnabled = user.TwoFactorEnabled;
        
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var returnUrl = ReturnUrl ?? Url.Content("~/");

        // Try standard Identity method first
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        
        // Fallback: manually look up user from cookie if Identity method fails
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
        
        if (user == null)
        {
            return RedirectToPage("./Login");
        }

        // Check if user is locked out
        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("User account locked out.");
            return RedirectToPage("./Lockout");
        }

        // Validate that at least one code is provided
        if (string.IsNullOrWhiteSpace(Input.TotpCode) && string.IsNullOrWhiteSpace(Input.RecoveryCode) && string.IsNullOrWhiteSpace(Input.EmailCode))
        {
            ModelState.AddModelError(string.Empty, _localizer["EnterCodeOrRecoveryCode"]);
            EmailMfaEnabled = user.EmailMfaEnabled;
            return Page();
        }
        
        // Try Email MFA code first (if provided)
        if (!string.IsNullOrWhiteSpace(Input.EmailCode))
        {
            var emailCode = Input.EmailCode.Replace(" ", "").Trim();
            var isValid = await _mfaService.VerifyEmailMfaCodeAsync(user, emailCode);
            
            if (isValid)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await CompleteMfaSignInAsync(user, returnUrl, RememberMe, "EmailMFA");
                return LocalRedirect(returnUrl);
            }
            
            // Record failed attempt and check for lockout
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }
            
            ModelState.AddModelError(nameof(Input.EmailCode), _localizer["InvalidOrExpiredEmailCode"]);
            EmailMfaEnabled = user.EmailMfaEnabled;
            return Page();
        }

        // Try TOTP code first
        if (!string.IsNullOrWhiteSpace(Input.TotpCode))
        {
            var totpCode = Input.TotpCode.Replace(" ", "").Trim();
            var isValid = await _mfaService.ValidateTotpCodeAsync(user, totpCode);

            if (isValid)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await CompleteMfaSignInAsync(user, returnUrl, RememberMe, "TOTP");
                return LocalRedirect(returnUrl);
            }

            // Record failed attempt and check for lockout
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(nameof(Input.TotpCode), _localizer["InvalidVerificationCode"]);
            return Page();
        }

        // Try recovery code
        if (!string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            // Keep original format - Identity expects codes as generated (e.g., XXXXX-XXXXX)
            var recoveryCode = Input.RecoveryCode.Trim().ToUpperInvariant();
            var isValid = await _mfaService.ValidateRecoveryCodeAsync(user, recoveryCode);

            if (isValid)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await CompleteMfaSignInAsync(user, returnUrl, RememberMe, "RecoveryCode");
                
                // Warn user about remaining recovery codes
                var remainingCodes = await _userManager.CountRecoveryCodesAsync(user);
                if (remainingCodes <= 3)
                {
                    LogLowRecoveryCodes(user.UserName, remainingCodes);
                }

                return LocalRedirect(returnUrl);
            }

            // Record failed attempt and check for lockout
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(nameof(Input.RecoveryCode), _localizer["InvalidRecoveryCode"]);
            return Page();
        }

        return Page();
    }

    private async Task CompleteMfaSignInAsync(ApplicationUser user, string returnUrl, bool rememberMe, string method)
    {
        // Complete 2FA sign-in
        await _signInManager.SignInAsync(user, isPersistent: rememberMe);
        
        LogMfaSuccess(user.UserName, method);

        // Publish audit event
        await _eventPublisher.PublishAsync(new LoginAttemptEvent(
            userId: user.Id.ToString(),
            userName: user.UserName ?? "unknown",
            isSuccessful: true,
            failureReason: null,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers["User-Agent"].ToString()
        ));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' completed MFA verification via {Method}.")]
    partial void LogMfaSuccess(string? userName, string method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User '{UserName}' has only {Count} recovery codes remaining.")]
    partial void LogLowRecoveryCodes(string? userName, int count);
    
    /// <summary>
    /// Handler for sending Email MFA code (called via AJAX from the page).
    /// </summary>
    public async Task<IActionResult> OnPostSendEmailCodeAsync()
    {
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            // Fallback: manually look up user from cookie
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
        
        if (user == null || !user.EmailMfaEnabled)
        {
            return new JsonResult(new { success = false, error = "notAvailable" });
        }
        
        await _mfaService.SendEmailMfaCodeAsync(user);
        _logger.LogInformation("Email MFA code sent to user {UserId} during login", user.Id);
        
        return new JsonResult(new { success = true });
    }
}
