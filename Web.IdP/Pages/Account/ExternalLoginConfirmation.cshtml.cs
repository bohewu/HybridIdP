using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Infrastructure.Identity;
using Web.IdP.Services;
using Web.IdP.Options;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Infrastructure.Services;
using Core.Domain.Constants;
using Core.Application.Interfaces;
using Core.Application;
using Core.Domain; // Explicitly include Core.Domain for ApplicationUser

namespace Web.IdP.Pages.Account;

[AllowAnonymous]
public class ExternalLoginConfirmationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly ILoginService _loginService;
    private readonly ISettingsService _settingsService;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ExternalLoginConfirmationModel> _logger;
    private readonly LoginNoticesOptions _loginNoticesOptions; // Added field

    public ExternalLoginConfirmationModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJitProvisioningService jitProvisioningService,
        ILoginService loginService,
        ISettingsService settingsService,
        IStringLocalizer<SharedResource> localizer,
        ILogger<ExternalLoginConfirmationModel> logger,
        IOptions<LoginNoticesOptions> loginNoticesOptions) // Added IOptions parameter
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jitProvisioningService = jitProvisioningService;
        _loginService = loginService;
        _settingsService = settingsService;
        _localizer = localizer;
        _logger = logger;
        _loginNoticesOptions = loginNoticesOptions.Value; // Initialized field
    }

    public LoginNoticesOptions LoginNotices => _loginNoticesOptions;

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string ReturnUrl { get; set; } = string.Empty;
    public string LoginProvider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    
    public bool ShowRegistrationButton { get; set; }

    public class InputModel
    {
        // For Linking
        [Required(ErrorMessage = "RequiredField")]
        [Display(Name = "EmailOrUsernameLabel")]
        public string Login { get; set; } = string.Empty;

        [Required(ErrorMessage = "RequiredField")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        LoginProvider = info.LoginProvider;
        DisplayName = info.Principal.Identity?.Name ?? "Unknown";
        Email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "Unknown";

        ShowRegistrationButton = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;

        return Page();
    }

    // ACTION: Link to Existing Account
    public async Task<IActionResult> OnPostLinkAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }
        
        LoginProvider = info.LoginProvider; // Restore for view
        DisplayName = info.Principal.Identity?.Name ?? "Unknown";
        Email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "Unknown";
        ShowRegistrationButton = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 1. Authenticate local credentials
        var result = await _loginService.AuthenticateAsync(Input.Login, Input.Password);
        if (result.Status != Core.Application.DTOs.LoginStatus.Success && 
            result.Status != Core.Application.DTOs.LoginStatus.LegacySuccess)
        {
            ModelState.AddModelError(string.Empty, _localizer["InvalidCredentials"]);
            return Page();
        }

        var user = result.User!;
        // 2. Link the external login
        var addResult = await _userManager.AddLoginAsync(user, info);
        if (addResult.Succeeded)
        {
            _logger.LogInformation("User {UserId} linked {Provider} account.", user.Id, info.LoginProvider);
            
            // Extract AMR claims from external provider and sign in with them
            var externalAmrClaims = info.Principal.FindAll(AuthConstants.ClaimTypes.Amr)
                .Select(c => c.Value)
                .ToList();
            
            var amrClaims = new List<Claim>
            {
                new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.External)
            };
            
            foreach (var amr in externalAmrClaims)
            {
                amrClaims.Add(new Claim(AuthConstants.ClaimTypes.Amr, amr));
            }
            
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);
            return LocalRedirect(ReturnUrl);
        }

        foreach (var error in addResult.Errors)
        {
             ModelState.AddModelError(string.Empty, error.Description);
        }
        return Page();
    }

    // ACTION: Create New Account (JIT)
    public async Task<IActionResult> OnPostCreateAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
        
        // Security check: Is registration enabled?
        var registrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
        if (!registrationEnabled)
        {
             return RedirectToPage("./Login"); // Should not happen if UI is correct
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // Prepare ExternalAuthResult DTO for JitService
        // Note: JitService expects a structured object.
        var externalAuth = new ExternalAuthResult
        {
            Provider = info.LoginProvider,
            ProviderKey = info.ProviderKey,
            Email = info.Principal.FindFirstValue(ClaimTypes.Email),
            DisplayName = info.Principal.Identity?.Name,       
            FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName),
            LastName = info.Principal.FindFirstValue(ClaimTypes.Surname),
            // Map other claims if available (e.g. from scopes)
            // Department, JobTitle etc. usually come from specific OIDC claims
            // For now, we pass standard claims.
        };

        try 
        {
             var user = await _jitProvisioningService.ProvisionExternalUserAsync(externalAuth);
             
             // Extract AMR claims from external provider and sign in with them
             var externalAmrClaims = info.Principal.FindAll(AuthConstants.ClaimTypes.Amr)
                 .Select(c => c.Value)
                 .ToList();
             
             var amrClaims = new List<Claim>
             {
                 new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.External)
             };
             
             foreach (var amr in externalAmrClaims)
             {
                 amrClaims.Add(new Claim(AuthConstants.ClaimTypes.Amr, amr));
             }
             
             await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);
             return LocalRedirect(ReturnUrl);
         }
        catch (Exception ex)
        {
             _logger.LogError(ex, "JIT Provisioning failed for {Provider}", info.LoginProvider);
             ModelState.AddModelError(string.Empty, "Error creating account: " + ex.Message);
             
             // Restore properties for View
             LoginProvider = info.LoginProvider;
             DisplayName = info.Principal.Identity?.Name ?? "Unknown";
             Email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "Unknown";
             ShowRegistrationButton = registrationEnabled;
             return Page();
        }
    }
}
