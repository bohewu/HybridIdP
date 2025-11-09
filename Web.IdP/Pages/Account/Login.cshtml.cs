using System.ComponentModel.DataAnnotations;
using Core.Application;
using Core.Domain;
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
    private readonly ITurnstileService _turnstileService;
    private readonly ILegacyAuthService _legacyAuthService;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ISecurityPolicyService _securityPolicyService;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ITurnstileService turnstileService,
        ILegacyAuthService legacyAuthService,
        IJitProvisioningService jitProvisioningService,
        IConfiguration configuration,
        ILogger<LoginModel> logger,
        IStringLocalizer<SharedResource> localizer,
        ISecurityPolicyService securityPolicyService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _turnstileService = turnstileService;
        _legacyAuthService = legacyAuthService;
        _localizer = localizer;
        _jitProvisioningService = jitProvisioningService;
        _configuration = configuration;
        _logger = logger;
        _securityPolicyService = securityPolicyService;
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

        if (ModelState.IsValid)
        {
            // Validate Turnstile if enabled
            if (TurnstileEnabled)
            {
                var turnstileResponse = Request.Form["cf-turnstile-response"].ToString();
                if (string.IsNullOrEmpty(turnstileResponse))
                {
                    ModelState.AddModelError(string.Empty, _localizer["CompleteCaptcha"]);
                    return Page();
                }

                var isValid = await _turnstileService.ValidateTokenAsync(
                    turnstileResponse,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                if (!isValid)
                {
                    ModelState.AddModelError(string.Empty, _localizer["CaptchaValidationFailed"]);
                    return Page();
                }
            }

            // Try to find user by email first, then by username if not found
            var user = await _userManager.FindByEmailAsync(Input.Login) 
                         ?? await _userManager.FindByNameAsync(Input.Login);

            if (user != null)
            {
                // User found locally, attempt local authentication ONLY.
                
                // Check if user is locked out
                if (await _userManager.IsLockedOutAsync(user))
                {
                    _logger.LogWarning("User account '{username}' is locked out.", user.UserName);
                    ModelState.AddModelError(string.Empty, _localizer["UserAccountLockedOut"]);
                    return Page();
                }

                // Check password
                if (await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    // Password is correct, reset failed attempts and sign in
                    await _userManager.ResetAccessFailedCountAsync(user);
                    await _signInManager.SignInAsync(user, Input.RememberMe);
                    _logger.LogInformation("User '{username}' logged in with local account.", user.UserName);
                    return LocalRedirect(returnUrl);
                }
                
                // Password is incorrect, handle lockout logic
                _logger.LogWarning("Invalid password attempt for user '{username}'.", user.UserName);
                
                // Get dynamic security policy
                var policy = await _securityPolicyService.GetCurrentPolicyAsync();
                
                // Increment failed access count if lockout is enabled in the policy
                if (policy.MaxFailedAccessAttempts > 0)
                {
                    await _userManager.AccessFailedAsync(user);
                    var accessFailedCount = await _userManager.GetAccessFailedCountAsync(user);
                    if (accessFailedCount >= policy.MaxFailedAccessAttempts)
                    {
                        _logger.LogWarning("User account '{username}' locked out due to too many failed login attempts.", user.UserName);
                        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(policy.LockoutDurationMinutes));
                        ModelState.AddModelError(string.Empty, _localizer["UserAccountLockedOut"]);
                        return Page();
                    }
                }
                
                // After a failed local attempt, always show invalid login and stop. Do not fall through.
                ModelState.AddModelError(string.Empty, _localizer["InvalidLoginAttempt"]);
                return Page();
            }

            // User not found locally, now try legacy auth + JIT
            var legacyResult = await _legacyAuthService.ValidateAsync(Input.Login, Input.Password);
            if (!legacyResult.IsAuthenticated)
            {
                // Generic error for both non-existent user and legacy auth failure
                ModelState.AddModelError(string.Empty, _localizer["InvalidLoginAttempt"]);
                return Page();
            }

            var provisionedUser = await _jitProvisioningService.ProvisionUserAsync(legacyResult);
            await _signInManager.SignInAsync(provisionedUser, isPersistent: Input.RememberMe);
            _logger.LogInformation("User logged in via legacy auth and JIT provisioning.");
            return LocalRedirect(returnUrl);
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }
}
