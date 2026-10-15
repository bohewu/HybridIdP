using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Events;
using System.ComponentModel.DataAnnotations;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Web.IdP.Pages.Account;

[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
public partial class LoginMfaModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaService _mfaService;
    private readonly IUserManagementService _userManagementService;
    private readonly IPasskeyService _passkeyService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ILogger<LoginMfaModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IMigrationIssuanceGuard _migrationIssuanceGuard;
    private readonly ICurrentUserLifecycleEligibility _lifecycleEligibility;

    public LoginMfaModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMfaService mfaService,
        IUserManagementService userManagementService,
        IPasskeyService passkeyService,
        IDomainEventPublisher eventPublisher,
        ILogger<LoginMfaModel> logger,
        IStringLocalizer<SharedResource> localizer,
        IMigrationIssuanceGuard migrationIssuanceGuard,
        ICurrentUserLifecycleEligibility lifecycleEligibility)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _mfaService = mfaService;
        _userManagementService = userManagementService;
        _passkeyService = passkeyService;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _localizer = localizer;
        _migrationIssuanceGuard = migrationIssuanceGuard;
        _lifecycleEligibility = lifecycleEligibility;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Method { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RecoveryPurpose { get; set; }
    public bool IsMfaEnrollmentReauthentication =>
        MfaEnrollmentSession.HasPending(HttpContext.Session);
    public string MfaReauthenticationNoticeKey => MfaEnrollmentSession.GetPendingNoticeKey(HttpContext.Session);
    
    /// <summary>
    /// Indicates if user also has Email MFA enabled (for showing switch link).
    /// </summary>
    public bool EmailMfaEnabled { get; set; }

    public bool TotpMfaEnabled { get; private set; }

    public bool PasskeyEnabled { get; private set; }

    public bool AccountRecoveryCodesAvailable { get; private set; }
    public bool CanSelectRecoveryEnrollment { get; private set; }
    public bool RecoveryCodesAvailable { get; private set; }
    public bool IsRecoveryOnly => AccountRecoveryCodesAvailable && !TotpMfaEnabled && !EmailMfaEnabled && !PasskeyEnabled;

    public string? PasskeyUserName { get; private set; }

    public class InputModel
    {
        [Display(Name = "VerificationCode")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "TotpCodeLength")]
        public string? TotpCode { get; set; }

        [Display(Name = "RecoveryCode")]
        public string? RecoveryCode { get; set; }

        public string? RecoveryPurpose { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(
        string? returnUrl = null,
        bool rememberMe = false,
        CancellationToken cancellationToken = default)
    {
        var user = await GetMfaUserAsync(HttpContext, _userManager);
        if (user == null || !await CanChallengeAsync(user, cancellationToken))
        {
            return RedirectToPage("./Login", new { returnUrl });
        }

        await LoadAvailableMethodsAsync(user, cancellationToken);

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        Method = IsMethodAvailable(Method) ? Method : null;
        Input.RecoveryPurpose = RecoveryPurpose;
        if (Method == "email")
            return RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe, recoveryPurpose = RecoveryPurpose });
        
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        var returnUrl = ReturnUrl ?? Url.Content("~/");

        var user = await GetMfaUserAsync(HttpContext, _userManager);
        if (user == null)
        {
            return RedirectToPage("./Login", new { returnUrl });
        }

        if (!await CanChallengeAsync(user, cancellationToken))
            return RedirectToPage("./Login", new { returnUrl });
        await LoadAvailableMethodsAsync(user, cancellationToken);
        RecoveryPurpose = Input.RecoveryPurpose;

        // The selected method is navigation, never evidence of an available factor.
        // Reject stale choices and mixed fields before spending the account's attempt budget.
        if (!IsMethodAvailable(Method) || Method is not ("totp" or "recovery") ||
            (Method == "totp" && !string.IsNullOrWhiteSpace(Input.RecoveryCode)) ||
            (Method == "recovery" && !string.IsNullOrWhiteSpace(Input.TotpCode)))
        {
            Method = null;
            ModelState.AddModelError(string.Empty, _localizer["ChooseAvailableVerificationMethod"]);
            return Page();
        }

        // Check for lockout
        if (await _userManager.IsLockedOutAsync(user))
        {
            LogAccountLocked(_logger);
            return RedirectToPage("./Lockout");
        }

        // Validate that at least one code is provided
        if (string.IsNullOrWhiteSpace(Input.TotpCode) && string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            ModelState.AddModelError(string.Empty, _localizer["EnterCodeOrRecoveryCode"]);
            return Page();
        }
        
        // Try TOTP code first
        if (Method == "totp" && !string.IsNullOrWhiteSpace(Input.TotpCode))
        {
            var isValid = await _mfaService.ValidateTotpCodeAsync(user, Input.TotpCode);
            if (isValid)
            {
                if (!await CanIssueFullCookieAsync(user, cancellationToken))
                {
                    return RedirectToPage("./Login", new { returnUrl });
                }

                AuthenticationMethodSession.Add(
                    HttpContext.Session, user,
                    Core.Domain.Constants.AuthConstants.Amr.Mfa,
                    Core.Domain.Constants.AuthConstants.Amr.Otp);
                var claims = AuthenticationMethodSession.CreateClaims(HttpContext.Session, user);

                RecoveryReauthenticationSession.MarkFullCompletion(HttpContext, user.Id);
                AccountSecurityOperationSession.MarkVerified(HttpContext, user, "totp");
                await PendingExternalLoginLink.MarkMfaCompletionAsync(HttpContext, user, "totp");
                await _signInManager.SignInWithClaimsAsync(user, isPersistent: RememberMe, claims);
                AuthenticationMethodSession.Consume(HttpContext.Session);
                await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
                LogLoginWithTotp(_logger);
                
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
            return Page();
        }

        // Try recovery code
        if (Method == "recovery" && !string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            var cleanCode = Input.RecoveryCode.Replace(" ", "").Replace("-", "");
            var accountCodes = HttpContext.RequestServices.GetRequiredService<IAccountRecoveryCodeService>();
            var operation = AccountSecurityOperationSession.GetRecoveryOperationBinding(HttpContext, user);
            if (CanSelectRecoveryEnrollment && Input.RecoveryPurpose == MfaEnrollmentSession.RecoveryEnrollmentPurpose)
                operation = MfaEnrollmentSession.RecoveryEnrollmentPurpose;
            var success = await accountCodes.RedeemAsync(user, cleanCode, operation, cancellationToken);
            var accountCodeRedeemed = success && accountCodes.HasRedemptionProof(user, operation);
            
            if (success)
            {
                if (!await CanIssueFullCookieAsync(user, cancellationToken))
                {
                    return RedirectToPage("./Login", new { returnUrl });
                }

                AuthenticationMethodSession.Add(
                    HttpContext.Session, user,
                    Core.Domain.Constants.AuthConstants.Amr.Mfa);
                var claims = AuthenticationMethodSession.CreateClaims(HttpContext.Session, user);

                RecoveryReauthenticationSession.MarkFullCompletion(HttpContext, user.Id);
                AccountSecurityOperationSession.MarkVerified(HttpContext, user, accountCodeRedeemed ? "account-recovery" : "recovery");
                await PendingExternalLoginLink.MarkMfaCompletionAsync(HttpContext, user, accountCodeRedeemed ? "account-recovery" : "recovery");
                await _signInManager.SignInWithClaimsAsync(user, isPersistent: RememberMe, claims);
                AuthenticationMethodSession.Consume(HttpContext.Session);
                await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
                LogLoginWithRecovery(_logger);
                
                var remainingCodes = accountCodeRedeemed
                    ? (await accountCodes.GetStatusAsync(user.Id, cancellationToken)).RemainingCodes
                    : await _mfaService.CountRecoveryCodesAsync(user);
                if (remainingCodes <= 3)
                {
                    LogLowRecoveryCodes(_logger, user.UserName ?? "Unknown", remainingCodes);
                }

                if (accountCodeRedeemed && operation == MfaEnrollmentSession.RecoveryEnrollmentPurpose &&
                    MfaEnrollmentSession.TryBeginRecoveryEnrollment(HttpContext, user))
                    return RedirectToPage("./MfaSetup", new
                    {
                        returnUrl = RedirectHelper.IsSafeReturnUrl(returnUrl) ? returnUrl : "/"
                    });

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

        return Page();
    }

    private async Task LoadAvailableMethodsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var codes = await HttpContext.RequestServices.GetRequiredService<IAccountRecoveryCodeService>()
            .GetStatusAsync(user.Id, cancellationToken);
        AccountRecoveryCodesAvailable = codes.RemainingCodes > 0;
        RecoveryCodesAvailable = codes.TotalRemainingCodes > 0;
        CanSelectRecoveryEnrollment = AccountRecoveryCodesAvailable &&
            AccountSecurityOperationSession.GetRecoveryOperationBinding(HttpContext, user) == "login-mfa";
        TotpMfaEnabled = user.TwoFactorEnabled;
        EmailMfaEnabled = user.EmailMfaEnabled;
        PasskeyUserName = user.UserName;
        PasskeyEnabled = !string.IsNullOrWhiteSpace(PasskeyUserName) &&
            (await _passkeyService.GetUserPasskeysAsync(user.Id, cancellationToken)).Count > 0;
    }

    private bool IsMethodAvailable(string? method) => method switch
    {
        "totp" => TotpMfaEnabled,
        "email" => EmailMfaEnabled,
        "passkey" => PasskeyEnabled,
        "recovery" => RecoveryCodesAvailable,
        _ => false
    };

    // Shared only by the selector and its Email OTP destination. Other temporary
    // authentication consumers retain their existing, narrower cookie contract.
    internal static async Task<ApplicationUser?> GetMfaUserAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        var authentication = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (authentication.Succeeded)
            return await TwoFactorAuthenticationSession.GetUserAsync(http, users);
        ApplicationUser? user = null;

        // Client-triggered step-up starts from an existing password-authenticated session,
        // not Identity's temporary two-factor cookie.
        if (user == null && http.User.Identity?.IsAuthenticated == true)
        {
            var application = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (!application.Succeeded || application.Principal?.Identity?.IsAuthenticated != true) return null;
            user = await users.GetUserAsync(application.Principal);
            if (user == null || string.IsNullOrEmpty(user.SecurityStamp) ||
                application.Principal.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value != user.SecurityStamp ||
                http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value != user.Id.ToString()) return null;
        }

        
        return user;
    }

    private async Task<bool> CanChallengeAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        user.IsActive && !user.IsDeleted && !user.RequiresPasswordChange &&
        await _signInManager.CanSignInAsync(user) && await CanIssueFullCookieAsync(user, cancellationToken);

    private async Task<bool> CanIssueFullCookieAsync(
        ApplicationUser user,
        CancellationToken cancellationToken) =>
        await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken) &&
        await _migrationIssuanceGuard.CanIssueAsync(user.Id, cancellationToken) &&
        await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account locked out.")]
    static partial void LogAccountLocked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in with TOTP 2FA.")]
    static partial void LogLoginWithTotp(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in with recovery code.")]
    static partial void LogLoginWithRecovery(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserName} has only {Count} recovery codes left.")]
    static partial void LogLowRecoveryCodes(ILogger logger, string userName, int count);
}
