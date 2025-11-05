using System.ComponentModel.DataAnnotations;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITurnstileService _turnstileService;
    private readonly ILegacyAuthService _legacyAuthService;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        ITurnstileService turnstileService,
        ILegacyAuthService legacyAuthService,
        IJitProvisioningService jitProvisioningService,
        IConfiguration configuration,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _turnstileService = turnstileService;
        _legacyAuthService = legacyAuthService;
        _jitProvisioningService = jitProvisioningService;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled => _configuration.GetValue<bool>("Turnstile:Enabled");
    public string TurnstileSiteKey => _configuration["Turnstile:SiteKey"] ?? string.Empty;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = default!;

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
                    ModelState.AddModelError(string.Empty, "Please complete the CAPTCHA verification.");
                    return Page();
                }

                var isValid = await _turnstileService.ValidateTokenAsync(
                    turnstileResponse,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                if (!isValid)
                {
                    ModelState.AddModelError(string.Empty, "CAPTCHA validation failed. Please try again.");
                    return Page();
                }
            }

            // Phase 2.3: Try local account first, then legacy auth with JIT provisioning
            // First, try local account authentication (for admin and other local users)
            // Try to find user by email first, then by username if not found
            var localUser = await _signInManager.UserManager.FindByEmailAsync(Input.Email) 
                         ?? await _signInManager.UserManager.FindByNameAsync(Input.Email);
            if (localUser != null)
            {
                // Use the actual username for sign-in (PasswordSignInAsync expects username, not email)
                var result = await _signInManager.PasswordSignInAsync(localUser.UserName!, Input.Password, Input.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in with local account.");
                    return LocalRedirect(returnUrl);
                }
            }

            // If local auth failed or user doesn't exist locally, try legacy auth + JIT
            var legacyResult = await _legacyAuthService.ValidateAsync(Input.Email, Input.Password);
            if (!legacyResult.IsAuthenticated)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            var user = await _jitProvisioningService.ProvisionUserAsync(legacyResult);
            await _signInManager.SignInAsync(user, isPersistent: Input.RememberMe);
            _logger.LogInformation("User logged in via legacy auth and JIT provisioning.");
            return LocalRedirect(returnUrl);
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }
}
