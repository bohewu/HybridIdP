using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options; // Added
using Core.Application.Options; // Added
using Core.Domain.Constants; // Added

using Microsoft.Extensions.Localization; // Added

namespace Web.IdP.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITurnstileService _turnstileService;
    private readonly TurnstileOptions _turnstileOptions; // Changed
    private readonly ILogger<RegisterModel> _logger;
    private readonly IApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly ISettingsService _settingsService;
    private readonly ITurnstileStateService _turnstileStateService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITurnstileService turnstileService,
        IOptions<TurnstileOptions> turnstileOptions,
        ILogger<RegisterModel> logger,
        IApplicationDbContext context,
        IAuditService auditService,
        ISettingsService settingsService,
        ITurnstileStateService turnstileStateService,
        ISecurityPolicyService securityPolicyService,
        IStringLocalizer<SharedResource> localizer)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _turnstileService = turnstileService;
        _turnstileOptions = turnstileOptions.Value;
        _logger = logger;
        _context = context;
        _auditService = auditService;
        _settingsService = settingsService;
        _turnstileStateService = turnstileStateService;
        _securityPolicyService = securityPolicyService;
        _localizer = localizer;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled { get; private set; }
    public string TurnstileSiteKey => _turnstileOptions.SiteKey;
    public bool RegistrationEnabled { get; private set; } = true;
    public SecurityPolicy? CurrentPolicy { get; private set; }
    
    public string ComplexityRequirementKey => (CurrentPolicy?.RequireNonAlphanumeric ?? true) 
        ? "Complexity_WithSymbols" 
        : "Complexity_NoSymbols";

    public class InputModel
    {
        [Required(ErrorMessage = "Validation_Required")]
        [EmailAddress(ErrorMessage = "Validation_Email")]
        [Display(Name = "Email")]
        public string Email { get; set; } = default!;

        [Required(ErrorMessage = "Validation_Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = default!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Required(ErrorMessage = "Validation_Required")]
        [Compare("Password", ErrorMessage = "Validation_PasswordMismatch")]
        public string ConfirmPassword { get; set; } = default!;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        // Redirect if already logged in
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }
        
        // Load Security Policy
        CurrentPolicy = await _securityPolicyService.GetCurrentPolicyAsync();

        // Check if registration is enabled
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        
        if (!RegistrationEnabled)
        {
            TempData["ErrorMessage"] = "Registration is currently disabled.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        // Load Turnstile enabled setting (DB overrides appsettings)
        var dbTurnstileEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Turnstile.Enabled);
        TurnstileEnabled = (dbTurnstileEnabled ?? _turnstileOptions.Enabled) && _turnstileStateService.IsAvailable;
        
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        
        // Load policy for view (in case of error redisplay) and validation
        CurrentPolicy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        // Block registration if disabled
        var isEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        if (!isEnabled)
        {
            return Forbid();
        }
        
        // Manual validation for Dynamic Password Policy
        if (ModelState.IsValid) // Check basic attributes first
        {
            var minLength = CurrentPolicy?.MinPasswordLength ?? 8;
            if (Input.Password.Length < minLength)
            {
                ModelState.AddModelError(string.Empty, _localizer["Validation_PasswordLength", minLength]);
                return Page();
            }
        }
        
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

            // Phase 10.5: Create Person entity first
            var person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = Input.Email.Split('@')[0], // Default from email
                CreatedAt = DateTime.UtcNow
            };
            _context.Persons.Add(person);
            await _context.SaveChangesAsync(CancellationToken.None);

            var user = new ApplicationUser
            {
                UserName = Input.Email,
                Email = Input.Email,
                EmailConfirmed = false, // Will be confirmed via email in Phase 5
                PersonId = person.Id  // Phase 10.5: Link to Person
            };

            var result = await _userManager.CreateAsync(user, Input.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new account with password.");

                // Phase 10.5: Audit the self-registration with Person creation
                var auditDetails = JsonSerializer.Serialize(new
                {
                    PersonId = person.Id,
                    ApplicationUserId = user.Id,
                    Email = user.Email,
                    FirstName = person.FirstName,
                    RegisteredAt = DateTime.UtcNow
                });
                await _auditService.LogEventAsync(
                    "SelfRegistrationPersonCreated",
                    user.Id.ToString(),
                    auditDetails,
                    HttpContext.Connection.RemoteIpAddress?.ToString(),
                    HttpContext.Request.Headers["User-Agent"].ToString());

                // Assign default User role
                await _userManager.AddToRoleAsync(user, "User");

                // Automatically sign in the user after registration
                await _signInManager.SignInAsync(user, isPersistent: false);
                
                return LocalRedirect(returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }
}
