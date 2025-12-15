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

        // Validate that at least one code is provided
        if (string.IsNullOrWhiteSpace(Input.TotpCode) && string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            ModelState.AddModelError(string.Empty, _localizer["EnterCodeOrRecoveryCode"]);
            return Page();
        }

        // Try TOTP code first
        if (!string.IsNullOrWhiteSpace(Input.TotpCode))
        {
            var totpCode = Input.TotpCode.Replace(" ", "").Trim();
            var isValid = await _mfaService.ValidateTotpCodeAsync(user, totpCode);

            if (isValid)
            {
                await CompleteMfaSignInAsync(user, returnUrl, RememberMe, "TOTP");
                return LocalRedirect(returnUrl);
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
                await CompleteMfaSignInAsync(user, returnUrl, RememberMe, "RecoveryCode");
                
                // Warn user about remaining recovery codes
                var remainingCodes = await _userManager.CountRecoveryCodesAsync(user);
                if (remainingCodes <= 3)
                {
                    LogLowRecoveryCodes(user.UserName, remainingCodes);
                }

                return LocalRedirect(returnUrl);
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
}
