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
        ISettingsService settingsService) // Added
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
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled => _turnstileOptions.Enabled && _turnstileStateService.IsAvailable; // Changed
    public string TurnstileSiteKey => _turnstileOptions.SiteKey; // Changed
    public bool RegistrationEnabled { get; private set; } = true;

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
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(returnUrl))
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }

        // Load registration setting
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

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
                // Record login for abnormal detection
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

                await _loginHistoryService.RecordLoginAsync(loginHistory);

                // Check for abnormal login
                var isAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(loginHistory);
                if (isAbnormal)
                {
                    loginHistory.IsFlaggedAbnormal = true;
                    await _notificationService.NotifyAbnormalLoginAsync(result.User!.Id.ToString(), loginHistory);

                    // Check if we should block abnormal logins
                    var policy = await _securityPolicyService.GetCurrentPolicyAsync();
                    if (policy.BlockAbnormalLogin)
                    {
                        LogAbnormalLoginBlocked(result.User!.UserName, loginHistory.IpAddress);
                        ModelState.AddModelError(string.Empty, _localizer["AbnormalLoginBlocked"]);
                        return Page();
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for user '{Login}': Account is locked out.")]
    partial void LogUserLockedOut(string login);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login failed for user '{Login}': Invalid credentials.")]
    partial void LogInvalidCredentials(string login);
}
