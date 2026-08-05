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

namespace Web.IdP.Pages.Account;

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

    public LoginMfaModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMfaService mfaService,
        IUserManagementService userManagementService,
        IPasskeyService passkeyService,
        IDomainEventPublisher eventPublisher,
        ILogger<LoginMfaModel> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _mfaService = mfaService;
        _userManagementService = userManagementService;
        _passkeyService = passkeyService;
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

    public bool TotpMfaEnabled { get; private set; }

    public bool PasskeyEnabled { get; private set; }

    public string? PasskeyUserName { get; private set; }

    public class InputModel
    {
        [Display(Name = "VerificationCode")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "TotpCodeLength")]
        public string? TotpCode { get; set; }

        [Display(Name = "RecoveryCode")]
        public string? RecoveryCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(
        string? returnUrl = null,
        bool rememberMe = false,
        CancellationToken cancellationToken = default)
    {
        var user = await GetMfaUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login", new { returnUrl });
        }

        await LoadAvailableMethodsAsync(user, cancellationToken);

        if (!TotpMfaEnabled)
        {
            if (EmailMfaEnabled && !PasskeyEnabled)
            {
                return RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe });
            }

            if (!PasskeyEnabled)
            {
                return RedirectToPage("./Login", new { returnUrl });
            }
        }

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        EmailMfaEnabled = user.EmailMfaEnabled;
        
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        var returnUrl = ReturnUrl ?? Url.Content("~/");

        var user = await GetMfaUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login", new { returnUrl });
        }

        await LoadAvailableMethodsAsync(user, cancellationToken);

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
        if (!string.IsNullOrWhiteSpace(Input.TotpCode))
        {
            var isValid = await _mfaService.ValidateTotpCodeAsync(user, Input.TotpCode);
            if (isValid)
            {
                AuthenticationMethodSession.Add(
                    HttpContext.Session,
                    Core.Domain.Constants.AuthConstants.Amr.Mfa,
                    Core.Domain.Constants.AuthConstants.Amr.Otp);
                var claims = AuthenticationMethodSession.CreateClaims(HttpContext.Session);

                await _signInManager.SignInWithClaimsAsync(user, isPersistent: RememberMe, claims);
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
        if (!string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            var cleanCode = Input.RecoveryCode.Replace(" ", "").Replace("-", "");
            // Use MfaService for custom recovery codes
            var success = await _mfaService.ValidateRecoveryCodeAsync(user, cleanCode);
            
            if (success)
            {
                AuthenticationMethodSession.Add(
                    HttpContext.Session,
                    Core.Domain.Constants.AuthConstants.Amr.Mfa);
                var claims = AuthenticationMethodSession.CreateClaims(HttpContext.Session);

                await _signInManager.SignInWithClaimsAsync(user, isPersistent: RememberMe, claims);
                await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
                LogLoginWithRecovery(_logger);
                
                var remainingCodes = await _mfaService.CountRecoveryCodesAsync(user);
                if (remainingCodes <= 3)
                {
                    LogLowRecoveryCodes(_logger, user.UserName ?? "Unknown", remainingCodes);
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

        return Page();
    }

    private async Task LoadAvailableMethodsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        TotpMfaEnabled = user.TwoFactorEnabled;
        EmailMfaEnabled = user.EmailMfaEnabled;
        PasskeyUserName = user.UserName;
        PasskeyEnabled = !string.IsNullOrWhiteSpace(PasskeyUserName) &&
            (await _passkeyService.GetUserPasskeysAsync(user.Id, cancellationToken)).Count > 0;
    }

    private async Task<ApplicationUser?> GetMfaUserAsync()
    {
        // Try standard Identity method first
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();

        // Client-triggered step-up starts from an existing password-authenticated session,
        // not Identity's temporary two-factor cookie.
        if (user == null && User.Identity?.IsAuthenticated == true)
        {
            user = await _userManager.GetUserAsync(User);
        }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account locked out.")]
    static partial void LogAccountLocked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in with TOTP 2FA.")]
    static partial void LogLoginWithTotp(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in with recovery code.")]
    static partial void LogLoginWithRecovery(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserName} has only {Count} recovery codes left.")]
    static partial void LogLowRecoveryCodes(ILogger logger, string userName, int count);
}
