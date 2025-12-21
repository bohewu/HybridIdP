using System.ComponentModel.DataAnnotations;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Events;
using Core.Application.Options; // Added
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options; // Added
using Microsoft.AspNetCore.RateLimiting;
using Core.Application.Interfaces;
using System.Text.Json;

namespace Web.IdP.Pages.Account;

[EnableRateLimiting("login")]
public partial class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoginService _loginService;
    private readonly ITurnstileService _turnstileService;
    private readonly ILoginHistoryService _loginHistoryService;
    private readonly INotificationService _notificationService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly TurnstileOptions _turnstileOptions; // Changed
    private readonly ILogger<LoginModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ITurnstileStateService _turnstileStateService; // Added
    private readonly ISettingsService _settingsService; // Added
    private readonly IPasskeyService _passkeyService;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILoginService loginService,
        ITurnstileService turnstileService,
        ILoginHistoryService loginHistoryService,
        INotificationService notificationService,
        ISecurityPolicyService securityPolicyService,
        IDomainEventPublisher eventPublisher,
        IOptions<TurnstileOptions> turnstileOptions,
        ILogger<LoginModel> logger,
        IStringLocalizer<SharedResource> localizer,
        ITurnstileStateService turnstileStateService,
        ISettingsService settingsService,
        IPasskeyService passkeyService) // Added
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _loginService = loginService;
        _turnstileService = turnstileService;
        _loginHistoryService = loginHistoryService;
        _notificationService = notificationService;
        _securityPolicyService = securityPolicyService;
        _eventPublisher = eventPublisher;
        _turnstileOptions = turnstileOptions.Value;
        _logger = logger;
        _localizer = localizer;
        _turnstileStateService = turnstileStateService; // Added
        _settingsService = settingsService; // Added
        _passkeyService = passkeyService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled { get; private set; }
    public string TurnstileSiteKey => _turnstileOptions.SiteKey;
    public bool RegistrationEnabled { get; private set; } = true;
    public bool PasskeyEnabled { get; private set; } = true;

    /// <summary>
    /// Calculate if Turnstile should be enabled based on settings and key configuration
    /// </summary>
    private async Task LoadTurnstileStateAsync()
    {
        var dbTurnstileEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Turnstile.Enabled);
        var isEnabledFlag = dbTurnstileEnabled ?? _turnstileOptions.Enabled;
        var hasSiteKey = !string.IsNullOrWhiteSpace(_turnstileOptions.SiteKey);
        var hasSecretKey = !string.IsNullOrWhiteSpace(_turnstileOptions.SecretKey);
        TurnstileEnabled = isEnabledFlag && hasSiteKey && hasSecretKey && _turnstileStateService.IsAvailable;
    }

    public class InputModel
    {
        [Required(ErrorMessage = "RequiredField")]
        [Display(Name = "EmailOrUsernameLabel")]
        public string Login { get; set; } = default!;

        [Required(ErrorMessage = "RequiredField")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = default!;

        [Display(Name = "RememberMe")]
        public bool RememberMe { get; private set; }

        public void SetRememberMe(bool value) => RememberMe = value;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        // If user is already authenticated, redirect away from login page
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }
        
        if (!string.IsNullOrEmpty(returnUrl))
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }

        // Load registration setting
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        
        // Load Passkey enabled state
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        PasskeyEnabled = policy.EnablePasskey;

        // Load Turnstile enabled state
        await LoadTurnstileStateAsync();

        // Clear AMR session on Get
        HttpContext.Session.Remove("AuthenticationMethods");

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        // Load settings needed for UI re-rendering
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        PasskeyEnabled = policy.EnablePasskey;
        await LoadTurnstileStateAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }
        
        // Validate Turnstile if enabled
        if (TurnstileEnabled)
        {
            var turnstileResponse = Request.Form["cf-turnstile-response"].ToString();
            if (string.IsNullOrEmpty(turnstileResponse) || !await _turnstileService.ValidateTokenAsync(turnstileResponse, HttpContext.Connection.RemoteIpAddress?.ToString()))
            {
                ModelState.AddModelError(string.Empty, _localizer["CaptchaValidationFailed"]);
                return Page();
            }
        }

        var result = await _loginService.AuthenticateAsync(Input.Login, Input.Password);

        switch (result.Status)
        {
            case LoginStatus.Success:
            case LoginStatus.LegacySuccess:
                // Check for abnormal login
                var loginHistory = new LoginHistory
                {
                    UserId = result.User!.Id,
                    LoginTime = DateTime.UtcNow,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    IsSuccessful = true,
                    RiskScore = 0,
                    IsFlaggedAbnormal = false
                };

                var isAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(loginHistory);
                if (isAbnormal)
                {
                    loginHistory.IsFlaggedAbnormal = true;
                    // Record login first so we have the record
                    await _loginHistoryService.RecordLoginAsync(loginHistory);
                    
                    await _notificationService.NotifyAbnormalLoginAsync(result.User!.Id.ToString(), loginHistory);

                    // Check if we should block abnormal logins
                    var currentPolicy = await _securityPolicyService.GetCurrentPolicyAsync();
                    if (currentPolicy.BlockAbnormalLogin)
                    {
                        LogAbnormalLoginBlocked(result.User!.UserName, loginHistory.IpAddress);
                        ModelState.AddModelError(string.Empty, _localizer["AbnormalLoginBlocked"]);
                        return Page();
                    }
                }
                else
                {
                    // Not abnormal, just record
                    await _loginHistoryService.RecordLoginAsync(loginHistory);
                }

                // Check if user has MFA enabled - redirect to MFA verification page
                // Support both TOTP MFA (TwoFactorEnabled) and Email MFA (EmailMfaEnabled)
                if (result.User!.TwoFactorEnabled || result.User!.EmailMfaEnabled)
                {
                    // Store user ID for 2FA verification
                    // Identity's GetTwoFactorAuthenticationUserAsync expects ClaimTypes.NameIdentifier
                    var identity = new System.Security.Claims.ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
                    identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, result.User.Id.ToString()));
                    await HttpContext.SignInAsync(
                        IdentityConstants.TwoFactorUserIdScheme,
                        new System.Security.Claims.ClaimsPrincipal(identity));
                    
                    LogMfaRequired(result.User.UserName);
                    
                    // Direct redirect to appropriate MFA page
                    if (result.User.TwoFactorEnabled)
                    {
                        return RedirectToPage("./LoginTotp", new { returnUrl, rememberMe = Input.RememberMe });
                    }
                    else if (result.User.EmailMfaEnabled)
                    {
                        return RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe = Input.RememberMe });
                    }
                    else
                    {
                        // Fallback (should ideally not happen if condition check was true)
                        return RedirectToPage("./LoginMfa", new { returnUrl, rememberMe = Input.RememberMe });
                    }
                }

                // Add AMR to session
                AddAmrToSession(AuthConstants.Amr.Password);

                // Check for mandatory MFA enrollment
                if (!result.User!.TwoFactorEnabled && !result.User!.EmailMfaEnabled)
                {
                    var currentPolicy = await _securityPolicyService.GetCurrentPolicyAsync();
                    if (currentPolicy.EnforceMandatoryMfaEnrollment)
                    {
                        var passkeys = await _passkeyService.GetUserPasskeysAsync(result.User.Id);
                        if (passkeys.Count == 0)
                        {
                            // User has NO MFA enabled and NO Passkeys registered
                            // Check grace period
                            var gracePeriodExpired = false;
                            if (result.User.MfaRequirementNotifiedAt == null)
                            {
                                result.User.MfaRequirementNotifiedAt = DateTime.UtcNow;
                                await _userManager.UpdateAsync(result.User);
                            }
                            else
                            {
                                var expiry = result.User.MfaRequirementNotifiedAt.Value.AddDays(currentPolicy.MfaEnforcementGracePeriodDays);
                                if (DateTime.UtcNow > expiry)
                                {
                                    gracePeriodExpired = true;
                                }
                            }

                            // Store user ID for 2FA setup access using partial authentication
                            var identity = new System.Security.Claims.ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
                            identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, result.User.Id.ToString()));
                            await HttpContext.SignInAsync(
                                IdentityConstants.TwoFactorUserIdScheme,
                                new System.Security.Claims.ClaimsPrincipal(identity));

                            // Redirect to MFA Setup
                            return RedirectToPage("./MfaSetup", new { returnUrl, gracePeriodExpired });
                        }
                    }
                }

                // Sign in user (role claims are automatically added by Identity)
                await _signInManager.SignInAsync(result.User!, isPersistent: Input.RememberMe);
                LogUserSignedIn(result.User!.UserName);
                
                // Publish audit event for successful login
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: result.User!.Id.ToString(),
                    userName: result.User!.UserName ?? Input.Login,
                    isSuccessful: true,
                    failureReason: null,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                // Set localization cookie if user has a preferred locale
                var preferredLocale = result.User!.Locale;
                
                // Fallback to Person if user has no locale but is linked to a Person
                // (Person is now loaded by LoginService)
                if (string.IsNullOrEmpty(preferredLocale) && result.User.Person != null)
                {
                    preferredLocale = result.User.Person.Locale;
                }

                if (!string.IsNullOrEmpty(preferredLocale))
                {
                    Response.Cookies.Append(
                        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new Microsoft.AspNetCore.Localization.RequestCulture(preferredLocale)),
                        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
                    );
                }

                // Always redirect to returnUrl (default is ~/ index page)
                // Users will navigate to Admin/ApplicationManager portals via menu
                return LocalRedirect(returnUrl);


            case LoginStatus.LockedOut:
                LogUserLockedOut(Input.Login);
                
                // Publish audit event for locked out login attempt
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: string.Empty,
                    userName: Input.Login,
                    isSuccessful: false,
                    failureReason: "Account locked out",
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                ModelState.AddModelError(string.Empty, _localizer["UserAccountLockedOut"]);
                return Page();

            case LoginStatus.UserInactive:
                LogUserInactive(Input.Login);
                
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: string.Empty,
                    userName: Input.Login,
                    isSuccessful: false,
                    failureReason: "User account deactivated",
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                ModelState.AddModelError(string.Empty, _localizer["UserAccountDeactivated"]);
                return Page();

            case LoginStatus.PersonInactive:
                LogPersonInactive(Input.Login, result.Message);
                
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: string.Empty,
                    userName: Input.Login,
                    isSuccessful: false,
                    failureReason: result.Message ?? "Person inactive",
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                ModelState.AddModelError(string.Empty, _localizer["PersonNotActive"]);
                return Page();

            case LoginStatus.InvalidCredentials:
            default:
                LogInvalidCredentials(Input.Login);
                
                // Publish audit event for failed login attempt
                await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                    userId: string.Empty,
                    userName: Input.Login,
                    isSuccessful: false,
                    failureReason: "Invalid credentials",
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: Request.Headers["User-Agent"].ToString()
                ));
                
                ModelState.AddModelError(string.Empty, _localizer["InvalidLoginAttempt"]);
                return Page();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Abnormal login blocked for user '{UserName}' from IP {IpAddress}")]
    partial void LogAbnormalLoginBlocked(string? userName, string? ipAddress);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' signed in successfully.")]
    partial void LogUserSignedIn(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' requires MFA verification.")]
    partial void LogMfaRequired(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for user '{Login}': Account is locked out.")]
    partial void LogUserLockedOut(string login);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for user '{Login}': User account is deactivated.")]
    partial void LogUserInactive(string login);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for user '{Login}': Person inactive - {Reason}.")]
    partial void LogPersonInactive(string login, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for user '{Login}': Invalid credentials.")]
    partial void LogInvalidCredentials(string login);

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
