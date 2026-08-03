using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
using Core.Domain;

namespace Web.IdP.Pages.Account;

[AllowAnonymous]
public class ExternalLoginConfirmationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly ILoginService _loginService;
    private readonly ISettingsService _settingsService;
    private readonly IBrandingService _brandingService;
    private readonly IUserManagementService _userManagementService;
    private readonly ILoginHistoryService _loginHistoryService;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ExternalLoginConfirmationModel> _logger;
    private readonly LoginNoticesOptions _loginNoticesOptions; 
    private readonly IExternalSignInCoordinator _externalSignInCoordinator;

    public ExternalLoginConfirmationModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJitProvisioningService jitProvisioningService,
        ILoginService loginService,
        ISettingsService settingsService,
        IBrandingService brandingService,
        IUserManagementService userManagementService,
        ILoginHistoryService loginHistoryService,
        IStringLocalizer<SharedResource> localizer,
        ILogger<ExternalLoginConfirmationModel> logger,
        IOptions<LoginNoticesOptions> loginNoticesOptions,
        IExternalSignInCoordinator externalSignInCoordinator)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jitProvisioningService = jitProvisioningService;
        _loginService = loginService;
        _settingsService = settingsService;
        _brandingService = brandingService;
        _userManagementService = userManagementService;
        _loginHistoryService = loginHistoryService;
        _localizer = localizer;
        _logger = logger;
        _loginNoticesOptions = loginNoticesOptions.Value;
        _externalSignInCoordinator = externalSignInCoordinator;
    }

    public LoginNoticesOptions LoginNotices => _loginNoticesOptions;

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string ReturnUrl { get; set; } = string.Empty;
    public string LoginProvider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    
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

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
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
        AppName = await _brandingService.GetAppNameAsync();

        ShowRegistrationButton = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled, cancellationToken) ?? true;

        return Page();
    }

    // ACTION: Link to Existing Account
    public async Task<IActionResult> OnPostLinkAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
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
        AppName = await _brandingService.GetAppNameAsync();
        ShowRegistrationButton = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled, cancellationToken) ?? true;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 1. Authenticate local credentials
        var result = await _loginService.AuthenticateAsync(Input.Login, Input.Password, cancellationToken);
        if (result.Status != Core.Application.DTOs.LoginStatus.Success && 
            result.Status != Core.Application.DTOs.LoginStatus.LegacySuccess)
        {
            ModelState.AddModelError(string.Empty, _localizer["InvalidCredentials"]);
            return Page();
        }

        var user = result.User!;

        // 2. Check MaxLoginsPerProvider limit
        var canLink = await _loginService.CanLinkExternalLoginAsync(user, info.LoginProvider, cancellationToken);
        if (!canLink.Succeeded)
        {
             ModelState.AddModelError(string.Empty, _localizer["ProviderLimitReached"] ?? "You have reached the maximum number of linked accounts.");
             return Page();
        }

        // 3. Link the external login
        var addResult = await _userManager.AddLoginAsync(user, info);
        if (addResult.Succeeded)
        {
            _logger.LogInformation("User {UserId} linked {Provider} account.", user.Id, info.LoginProvider);

            var completion = await _externalSignInCoordinator.CompleteAsync(
                HttpContext,
                user,
                cancellationToken);
            if (completion.Status == ExternalSignInCompletionStatus.Blocked)
            {
                await _userManager.RemoveLoginAsync(user, info.LoginProvider, info.ProviderKey);
                return HandleExternalSignInIncomplete(completion, ReturnUrl);
            }

            if (!completion.IsSucceeded)
            {
                return HandleExternalSignInIncomplete(completion, ReturnUrl);
            }

            await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
            await RecordSuccessfulLoginAsync(user.Id);
            return LocalRedirect(ReturnUrl);
        }

        foreach (var error in addResult.Errors)
        {
             ModelState.AddModelError(string.Empty, error.Description);
        }
        return Page();
    }

    // ACTION: Create New Account (JIT)
    public async Task<IActionResult> OnPostCreateAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
        
        // Security check: Is registration enabled?
        var registrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled, cancellationToken) ?? true;
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
            EmailVerified = ExternalEmailAssurance.IsVerified(info),
            DisplayName = info.Principal.Identity?.Name,       
            FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName),
            LastName = info.Principal.FindFirstValue(ClaimTypes.Surname),
            // Map other claims if available (e.g. from scopes)
            // Department, JobTitle etc. usually come from specific OIDC claims
            // For now, we pass standard claims.
        };

        try 
        {
            var user = await _jitProvisioningService.ProvisionExternalUserAsync(externalAuth, cancellationToken);

            var completion = await _externalSignInCoordinator.CompleteAsync(
                HttpContext,
                user,
                cancellationToken);
            if (!completion.IsSucceeded)
            {
                return HandleExternalSignInIncomplete(completion, ReturnUrl);
            }

            await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
            await RecordSuccessfulLoginAsync(user.Id);
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

    private IActionResult HandleExternalSignInIncomplete(
        ExternalSignInCompletionResult completion,
        string returnUrl)
    {
        return completion.Status switch
        {
            ExternalSignInCompletionStatus.TotpRequired =>
                RedirectToPage("./LoginTotp", new { returnUrl, rememberMe = false }),
            ExternalSignInCompletionStatus.EmailOtpRequired =>
                RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe = false }),
            ExternalSignInCompletionStatus.MfaEnrollmentRequired =>
                RedirectToPage("./MfaSetup", new { returnUrl }),
            ExternalSignInCompletionStatus.Blocked when completion.Denial?.Status == LoginStatus.LockedOut =>
                RedirectToPage("./Lockout"),
            ExternalSignInCompletionStatus.Blocked when completion.Denial?.Status == LoginStatus.UserInactive =>
                RedirectToPage("./Login", new { error = "UserInactive", message = completion.Denial.Message }),
            ExternalSignInCompletionStatus.Blocked when completion.Denial?.Status == LoginStatus.PersonInactive =>
                RedirectToPage("./Login", new { error = "PersonInactive", message = completion.Denial.Message }),
            _ => RedirectToPage("./Login", new { ReturnUrl = returnUrl, error = "ExternalLoginFailure" })
        };
    }

    private async Task RecordSuccessfulLoginAsync(Guid userId)
    {
        try
        {
            var loginHistory = new LoginHistory
            {
                UserId = userId,
                LoginTime = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                IsSuccessful = true,
                RiskScore = 0,
                IsFlaggedAbnormal = false
            };

            loginHistory.IsFlaggedAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(loginHistory);
            await _loginHistoryService.RecordLoginAsync(loginHistory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record external login history for user {UserId}", userId);
        }
    }
}
