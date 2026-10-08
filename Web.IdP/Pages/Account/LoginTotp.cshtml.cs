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
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Web.IdP.Pages.Account;

public partial class LoginTotpModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaService _mfaService;
    private readonly IUserManagementService _userManagementService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IMigrationIssuanceGuard _migrationIssuanceGuard;
    private readonly ICurrentUserLifecycleEligibility _lifecycleEligibility;
    private readonly ILogger<LoginTotpModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LoginTotpModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMfaService mfaService,
        IUserManagementService userManagementService,
        IDomainEventPublisher eventPublisher,
        ILogger<LoginTotpModel> logger,
        IStringLocalizer<SharedResource> localizer,
        IMigrationIssuanceGuard migrationIssuanceGuard,
        ICurrentUserLifecycleEligibility lifecycleEligibility)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _mfaService = mfaService;
        _userManagementService = userManagementService;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _localizer = localizer;
        _migrationIssuanceGuard = migrationIssuanceGuard;
        _lifecycleEligibility = lifecycleEligibility;
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

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
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
                if (!await CanIssueFullCookieAsync(user, cancellationToken))
                {
                    return RedirectToPage("./Login");
                }

                AuthenticationMethodSession.Add(
                    HttpContext.Session, user,
                    AuthConstants.Amr.Mfa,
                    AuthConstants.Amr.Otp);
                var claims = AuthenticationMethodSession.CreateClaims(HttpContext.Session, user);

                RecoveryReauthenticationSession.MarkFullCompletion(HttpContext, user.Id);
                AccountSecurityOperationSession.MarkVerified(HttpContext, user, "totp");
                await PendingExternalLoginLink.MarkMfaCompletionAsync(HttpContext, user, "totp");
                await _signInManager.SignInWithClaimsAsync(user, RememberMe, claims);
                AuthenticationMethodSession.Consume(HttpContext.Session);
                await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
                
                _logger.LogInformation("User logged in with TOTP 2FA.");
                
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: user.Id.ToString(),
                    userName: user.UserName ?? string.Empty,
                    isSuccessful: true,
                    failureReason: null,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                return this.SafeRedirect(returnUrl);
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
            var usedCustomCode = await _mfaService.ValidateRecoveryCodeAsync(user, cleanCode, cancellationToken);
            var succeeded = usedCustomCode ||
                await _mfaService.ValidateNativeRecoveryCodeAsync(user, cleanCode, cancellationToken);
            
            if (succeeded)
            {
                if (!await CanIssueFullCookieAsync(user, cancellationToken))
                {
                    return RedirectToPage("./Login");
                }

                AuthenticationMethodSession.Add(HttpContext.Session, user, AuthConstants.Amr.Mfa);
                var claims = AuthenticationMethodSession.CreateClaims(HttpContext.Session, user);

                RecoveryReauthenticationSession.MarkFullCompletion(HttpContext, user.Id);
                AccountSecurityOperationSession.MarkVerified(HttpContext, user, "recovery");
                await PendingExternalLoginLink.MarkMfaCompletionAsync(HttpContext, user, "recovery");
                await _signInManager.SignInWithClaimsAsync(user, isPersistent: RememberMe, claims);
                AuthenticationMethodSession.Consume(HttpContext.Session);
                await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
                _logger.LogInformation("User logged in with recovery code.");
                
                var remainingCodes = usedCustomCode
                    ? await _mfaService.CountRecoveryCodesAsync(user, cancellationToken)
                    : await _userManager.CountRecoveryCodesAsync(user);
                if (remainingCodes <= 3)
                {
                    _logger.LogWarning("User {UserName} has only {Count} recovery codes left.", user.UserName, remainingCodes);
                }

                return this.SafeRedirect(returnUrl);
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
        return await TwoFactorAuthenticationSession.GetUserAsync(HttpContext, _userManager);
    }

    private async Task<bool> CanIssueFullCookieAsync(
        ApplicationUser user,
        CancellationToken cancellationToken) =>
        await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken) &&
        await _migrationIssuanceGuard.CanIssueAsync(user.Id, cancellationToken) &&
        await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken);

}
