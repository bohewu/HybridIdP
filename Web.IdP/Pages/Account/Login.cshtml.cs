using System.ComponentModel.DataAnnotations;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Web.IdP.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoginService _loginService;
    private readonly ITurnstileService _turnstileService;
    private readonly ILoginHistoryService _loginHistoryService;
    private readonly INotificationService _notificationService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILoginService loginService,
        ITurnstileService turnstileService,
        ILoginHistoryService loginHistoryService,
        INotificationService notificationService,
        ISecurityPolicyService securityPolicyService,
        IConfiguration configuration,
        ILogger<LoginModel> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _loginService = loginService;
        _turnstileService = turnstileService;
        _loginHistoryService = loginHistoryService;
        _notificationService = notificationService;
        _securityPolicyService = securityPolicyService;
        _configuration = configuration;
        _logger = logger;
        _localizer = localizer;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled => _configuration.GetValue<bool>("Turnstile:Enabled");
    public string TurnstileSiteKey => _configuration["Turnstile:SiteKey"] ?? string.Empty;

    public class InputModel
    {
        [Required]
        [Display(Name = "EmailOrUsernameLabel")]
        public string Login { get; set; } = default!;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = default!;

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(returnUrl))
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }

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
                        _logger.LogWarning("Abnormal login blocked for user '{UserName}' from IP {IpAddress}", result.User!.UserName, loginHistory.IpAddress);
                        ModelState.AddModelError(string.Empty, _localizer["AbnormalLoginBlocked"]);
                        return Page();
                    }
                }

                // Phase 11.2: Check if user has multiple roles - redirect to SelectRole if needed
                var userRoles = await _userManager.GetRolesAsync(result.User!);
                
                if (userRoles.Count > 1)
                {
                    // User has multiple roles - redirect to role selection page
                    _logger.LogInformation("User '{UserName}' has {RoleCount} roles. Redirecting to role selection.", 
                        result.User!.UserName, userRoles.Count);
                    return RedirectToPage("/Account/SelectRole", new { userId = result.User!.Id, returnUrl });
                }
                else if (userRoles.Count == 1)
                {
                    // Single role - sign in directly
                    await _signInManager.SignInAsync(result.User!, isPersistent: Input.RememberMe);
                    _logger.LogInformation("User '{UserName}' signed in successfully with single role '{Role}'.", 
                        result.User!.UserName, userRoles[0]);
                    return LocalRedirect(returnUrl);
                }
                else
                {
                    // No roles assigned - deny access
                    _logger.LogWarning("User '{UserName}' has no assigned roles. Access denied.", result.User!.UserName);
                    ModelState.AddModelError(string.Empty, _localizer["NoRolesAssigned"]);
                    return Page();
                }

            case LoginStatus.LockedOut:
                _logger.LogWarning("Login failed for user '{Login}': Account is locked out.", Input.Login);
                ModelState.AddModelError(string.Empty, _localizer["UserAccountLockedOut"]);
                return Page();

            case LoginStatus.InvalidCredentials:
            default:
                _logger.LogWarning("Login failed for user '{Login}': Invalid credentials.", Input.Login);
                ModelState.AddModelError(string.Empty, _localizer["InvalidLoginAttempt"]);
                return Page();
        }
    }
}
