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
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options; // Added
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using Core.Application.Interfaces;
using System.Text.Json;
using Web.IdP.Helpers;

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
    private readonly IUserManagementService _userManagementService;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly ILocalizationService _localizationService;
    private readonly Web.IdP.Options.LoginNoticesOptions _loginNoticesOptions;

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
        ILocalizationService localizationService,
        IOptions<Web.IdP.Options.LoginNoticesOptions> loginNoticesOptions,
        ITurnstileStateService turnstileStateService,
        ISettingsService settingsService,
        IPasskeyService passkeyService,
        IUserManagementService userManagementService,
        IOpenIddictApplicationManager applicationManager) // Added
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
        _localizationService = localizationService;
        _loginNoticesOptions = loginNoticesOptions.Value;
        _turnstileStateService = turnstileStateService; // Added
        _settingsService = settingsService; // Added
        _passkeyService = passkeyService;
        _userManagementService = userManagementService;
        _applicationManager = applicationManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled { get; private set; }
    public string TurnstileSiteKey { get; private set; } = string.Empty;
    public bool RegistrationEnabled { get; private set; } = true;
    public bool PasskeyEnabled { get; private set; } = true;
    public string? CustomForgotPasswordUrl { get; private set; }

    /// <summary>
    /// Calculate if Turnstile should be enabled based on settings and key configuration
    /// </summary>
    private async Task LoadTurnstileStateAsync(string? returnUrl)
    {
        var dbTurnstileEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Turnstile.Enabled);
        var globalTurnstileEnabled = dbTurnstileEnabled ?? _turnstileOptions.Enabled;
        var clientTurnstileEnabled = await IsTurnstileEnabledForClientAsync(returnUrl);
        
        var dbSiteKey = await _settingsService.GetValueAsync<string?>(SettingKeys.Turnstile.SiteKey);
        TurnstileSiteKey = !string.IsNullOrEmpty(dbSiteKey) ? dbSiteKey : _turnstileOptions.SiteKey;
        
        var dbSecretKey = await _settingsService.GetValueAsync<string?>(SettingKeys.Turnstile.SecretKey);
        var hasSecretKey = !string.IsNullOrEmpty(dbSecretKey) || !string.IsNullOrWhiteSpace(_turnstileOptions.SecretKey);
        
        var hasSiteKey = !string.IsNullOrWhiteSpace(TurnstileSiteKey);
        
        TurnstileEnabled = globalTurnstileEnabled && clientTurnstileEnabled && hasSiteKey && hasSecretKey && _turnstileStateService.IsAvailable;
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

    public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        ExternalLogins = await GetAvailableExternalLoginsAsync(returnUrl);

        // If user is already authenticated, redirect away from login page
        if (User.Identity?.IsAuthenticated == true)
        {
            return this.SafeRedirect(returnUrl, "~/");
        }
        
        if (!string.IsNullOrEmpty(returnUrl))
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }

        if (!string.IsNullOrEmpty(remoteError))
        {
            ModelState.AddModelError(string.Empty, _localizer[$"ExternalLoginFailure_{remoteError}"] ?? _localizer["ExternalLoginFailure"] ?? "External login failed.");
        }

        // Load registration setting
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        
        // Load Passkey enabled state
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        PasskeyEnabled = policy.EnablePasskey;
        CustomForgotPasswordUrl = policy.CustomForgotPasswordUrl;

        // Load Turnstile enabled state
        await LoadTurnstileStateAsync(returnUrl);

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
        CustomForgotPasswordUrl = policy.CustomForgotPasswordUrl;
        await LoadTurnstileStateAsync(returnUrl);

        ExternalLogins = await GetAvailableExternalLoginsAsync(returnUrl);

        await ApplyDynamicLoginRequiredMessageAsync();

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
                            if (result.User.MfaRequirementNotifiedAt == null)
                            {
                                result.User.MfaRequirementNotifiedAt = DateTime.UtcNow;
                                await _userManager.UpdateAsync(result.User);
                            }
                            // Expiry is now checked entirely in MfaSetup page

                            // 1. Issue AMR: pwd claim (Password authentication)
                            // We use a list to support multiple values as per spec (though here it's just one initially)
                            var amrList = new List<string> { Core.Domain.Constants.AuthConstants.Amr.Password };
                            await HttpContext.Session.LoadAsync();
                            HttpContext.Session.SetString("AuthenticationMethods", System.Text.Json.JsonSerializer.Serialize(amrList));
                            
                            // 2. Check Mandatory MFA Policy
                            // If policy is enforced and user has NO MFA, force them to setup
                            if (currentPolicy.EnforceMandatoryMfaEnrollment)
                            {
                                // Check if grace period is active or expired
                                var isGracePeriodActive = false;
                                if (result.User.MfaRequirementNotifiedAt != null)
                                {
                                     var expiry = result.User.MfaRequirementNotifiedAt.Value.AddDays(currentPolicy.MfaEnforcementGracePeriodDays);
                                     if (DateTime.UtcNow < expiry)
                                     {
                                         isGracePeriodActive = true;
                                     }
                                }
                                else
                                {
                                    // First time notification - active
                                    isGracePeriodActive = true; 
                                }

                                // If grace period expired, force setup
                                if (!isGracePeriodActive)
                                {
                                    // Store user ID for 2FA setup access using partial authentication
                                    var identity = new System.Security.Claims.ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
                                    identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, result.User.Id.ToString()));
                                    await HttpContext.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new System.Security.Claims.ClaimsPrincipal(identity));

                                    return RedirectToPage("./MfaSetup", new { returnUrl });
                                }
                            }
                            
                            // Normal flow or Grace Period Active
                            // Issue cookie with amr claim
                            var claims = new List<System.Security.Claims.Claim>
                            {
                                new System.Security.Claims.Claim("amr", Core.Domain.Constants.AuthConstants.Amr.Password)
                            };
                            
                            // Note: SignInAsync below merges these claims into the principal
                            await _signInManager.SignInWithClaimsAsync(result.User, Input.RememberMe, claims);
                            await _userManagementService.UpdateLastLoginAsync(result.User.Id);
                            return this.SafeRedirect(returnUrl);
                        }
                    }
                }

                // Add AMR to session and issue cookie with amr: pwd
                AddAmrToSession(AuthConstants.Amr.Password);
                var amrClaimsList = new List<System.Security.Claims.Claim>
                {
                    new System.Security.Claims.Claim("amr", AuthConstants.Amr.Password)
                };

                // Sign in user (role claims are automatically added by Identity)
                await _signInManager.SignInWithClaimsAsync(result.User!, isPersistent: Input.RememberMe, amrClaimsList);
                await _userManagementService.UpdateLastLoginAsync(result.User!.Id);
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
                // (LoginService copies Person.Locale to User.Locale if needed)
                var preferredLocale = result.User!.Locale;

                if (!string.IsNullOrEmpty(preferredLocale))
                {
                    Response.Cookies.Append(
                        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new Microsoft.AspNetCore.Localization.RequestCulture(preferredLocale)),
                        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), Secure = true, SameSite = SameSiteMode.Lax }
                    );
                }

                // Always redirect to returnUrl (default is ~/ index page)
                // Users will navigate to Admin/ApplicationManager portals via menu
                return this.SafeRedirect(returnUrl);


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
            : JsonSerializer.Deserialize<List<string>>(currentAmrJson!) ?? new List<string>();
        
        if (!amrList.Contains(amr))
        {
            amrList.Add(amr);
            HttpContext.Session.SetString("AuthenticationMethods", JsonSerializer.Serialize(amrList));
        }
    }

    public async Task<IActionResult> OnPostExternalLogin(string provider, string? returnUrl = null)
    {
        returnUrl ??= Request.Form["returnUrl"].FirstOrDefault();
        returnUrl ??= Request.Query["returnUrl"].FirstOrDefault();
        returnUrl ??= Request.Query["ReturnUrl"].FirstOrDefault();

        var availableExternalLogins = await GetAvailableExternalLoginsAsync(returnUrl);
        var providerAvailable = availableExternalLogins.Any(scheme =>
            string.Equals(scheme.Name, provider, StringComparison.Ordinal));

        if (!providerAvailable)
        {
            return RedirectToPage("./Login", new { returnUrl, remoteError = "ProviderNotAvailable" });
        }

        // Request a redirect to the external login provider.
        var redirectUrl = Url.Page("./ExternalLoginCallback", pageHandler: null, values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    private async Task ApplyDynamicLoginRequiredMessageAsync()
    {
        const string loginFieldKey = $"{nameof(Input)}.{nameof(InputModel.Login)}";

        if (!ModelState.TryGetValue(loginFieldKey, out var loginFieldState) ||
            loginFieldState.Errors.Count == 0 ||
            !string.IsNullOrWhiteSpace(Input?.Login))
        {
            return;
        }

        var displayName = await ResolveLoginDisplayNameAsync();
        var requiredFieldTemplate = _localizer["RequiredField"].Value;

        loginFieldState.Errors.Clear();
        ModelState.AddModelError(loginFieldKey, FormatRequiredFieldMessage(requiredFieldTemplate, displayName));
    }

    private async Task<string> ResolveLoginDisplayNameAsync()
    {
        var culture = HttpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.Culture.Name ?? "en-US";

        var configuredOverride = await DynamicLocalizedTextResolver.ResolveAsync(
            _loginNoticesOptions.EmailOrUsername,
            culture,
            _localizationService);

        if (!string.IsNullOrWhiteSpace(configuredOverride))
        {
            return configuredOverride;
        }

        return _localizer["EmailOrUsername"].Value;
    }

    private static string FormatRequiredFieldMessage(string requiredFieldTemplate, string displayName)
    {
        if (string.IsNullOrWhiteSpace(requiredFieldTemplate) ||
            string.Equals(requiredFieldTemplate, "RequiredField", StringComparison.Ordinal))
        {
            return $"{displayName} is required.";
        }

        try
        {
            return string.Format(requiredFieldTemplate, displayName);
        }
        catch (FormatException)
        {
            return requiredFieldTemplate;
        }
    }

    private async Task<IList<AuthenticationScheme>> GetAvailableExternalLoginsAsync(string? returnUrl)
    {
        if (await IsExternalProvidersDisabledAsync(returnUrl))
        {
            return new List<AuthenticationScheme>();
        }

        return (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    private async Task<bool> IsExternalProvidersDisabledAsync(string? returnUrl)
    {
        var clientId = ResolveClientIdFromLoginContext(returnUrl);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var application = await _applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
        {
            return false;
        }

        var properties = await _applicationManager.GetPropertiesAsync(application);
        if (!properties.TryGetValue(AuthConstants.Properties.DisableExternalProviders, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private async Task<bool> IsTurnstileEnabledForClientAsync(string? returnUrl)
    {
        var clientId = ResolveClientIdFromLoginContext(returnUrl);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var application = await _applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
        {
            return false;
        }

        var properties = await _applicationManager.GetPropertiesAsync(application);
        if (!properties.TryGetValue(AuthConstants.Properties.EnableTurnstile, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private string? ResolveClientIdFromLoginContext(string? returnUrl)
    {
        var clientIdFromReturnUrl = TryGetClientIdFromReturnUrl(returnUrl);
        if (!string.IsNullOrWhiteSpace(clientIdFromReturnUrl))
        {
            return clientIdFromReturnUrl;
        }

        var queryClientId = Request.Query["client_id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryClientId))
        {
            return queryClientId;
        }

        var formClientId = Request.HasFormContentType ? Request.Form["client_id"].FirstOrDefault() : null;
        return string.IsNullOrWhiteSpace(formClientId) ? null : formClientId;
    }

    private string? TryGetClientIdFromReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            return null;
        }

        var queryStartIndex = returnUrl.IndexOf('?');
        if (queryStartIndex < 0 || queryStartIndex == returnUrl.Length - 1)
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(returnUrl[queryStartIndex..]);
        if (!query.TryGetValue("client_id", out var clientIds))
        {
            return null;
        }

        var clientId = clientIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(clientId) ? null : clientId;
    }
}
