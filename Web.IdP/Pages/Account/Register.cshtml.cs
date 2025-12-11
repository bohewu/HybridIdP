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
    private readonly ISettingsService _settingsService; // Added

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITurnstileService turnstileService,
        IOptions<TurnstileOptions> turnstileOptions, // Changed
        ILogger<RegisterModel> logger,
        IApplicationDbContext context,
        IAuditService auditService,
        ISettingsService settingsService) // Added
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _turnstileService = turnstileService;
        _turnstileOptions = turnstileOptions.Value; // Changed
        _logger = logger;
        _context = context;
        _auditService = auditService;
        _settingsService = settingsService; // Added
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }
    
    public bool TurnstileEnabled => _turnstileOptions.Enabled; // Changed
    public string TurnstileSiteKey => _turnstileOptions.SiteKey; // Changed
    public bool RegistrationEnabled { get; private set; } = true;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = default!;

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = default!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = default!;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        // Check if registration is enabled
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        
        if (!RegistrationEnabled)
        {
            TempData["ErrorMessage"] = "Registration is currently disabled.";
            return RedirectToPage("./Login", new { returnUrl });
        }
        
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        
        // Block registration if disabled
        var isEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        if (!isEnabled)
        {
            return Forbid();
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
