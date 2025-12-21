using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Events;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Web.IdP.Pages.Account;

public partial class LoginEmailOtpModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaService _mfaService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ILogger<LoginEmailOtpModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LoginEmailOtpModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMfaService mfaService,
        IDomainEventPublisher eventPublisher,
        ILogger<LoginEmailOtpModel> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _mfaService = mfaService;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _localizer = localizer;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }
    
    public string? MaskedEmail { get; private set; }
    
    /// <summary>
    /// Indicates if user also has TOTP enabled (for showing switch link).
    /// </summary>
    public bool TwoFactorEnabled { get; private set; }

    public class InputModel
    {
        [Display(Name = "EmailVerificationCode")]
        [Required(ErrorMessage = "RequiredField")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "EmailCodeLength")]
        public string? EmailCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, bool rememberMe = false)
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login");
        }
        
        // Verify user actually has Email MFA enabled
        if (!user.EmailMfaEnabled)
        {
            if (user.TwoFactorEnabled)
            {
                return RedirectToPage("./LoginTotp", new { returnUrl, rememberMe });
            }
            return RedirectToPage("./Login");
        }

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        TwoFactorEnabled = user.TwoFactorEnabled;
        
        MaskEmail(user.Email);
        
        return Page();
    }
    
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return NotFound();
        }
        
        if (!user.EmailMfaEnabled)
        {
            return Forbid();
        }

        try 
        {
            var (success, remainingSeconds) = await _mfaService.SendEmailMfaCodeAsync(user);
            
            if (success)
            {
                return new JsonResult(new { success = true, remainingSeconds });
            }
            else
            {
                // Rate limited
                return StatusCode(429, new { success = false, remainingSeconds });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email MFA code for user {UserId}", user.Id);
            return StatusCode(500, new { error = "Failed to send code" });
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var returnUrl = ReturnUrl ?? Url.Content("~/");

        var user = await GetTwoFactorUserAsync();
        if (user == null)
        {
            return RedirectToPage("./Login");
        }

        if (!ModelState.IsValid)
        {
            TwoFactorEnabled = user.TwoFactorEnabled;
            MaskEmail(user.Email);
            return Page();
        }

        // Verify Email Code
        var isValid = await _mfaService.VerifyEmailMfaCodeAsync(user, Input.EmailCode!);
        
        if (isValid)
        {
            AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.Mfa);
            AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.Otp);

            // Create the principal with the new claims for the cookie
             var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim("amr", Core.Domain.Constants.AuthConstants.Amr.Mfa),
                new System.Security.Claims.Claim("amr", Core.Domain.Constants.AuthConstants.Amr.Otp)
            };

            await _signInManager.SignInWithClaimsAsync(user, RememberMe, claims);
            _logger.LogInformation("User logged in with Email MFA.");
            
            await _eventPublisher.PublishAsync(new LoginAttemptEvent(
                userId: user.Id.ToString(),
                userName: user.UserName ?? string.Empty,
                isSuccessful: true,
                failureReason: null,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers["User-Agent"].ToString()
            ));
            
            return LocalRedirect(returnUrl);
        }

        await _userManager.AccessFailedAsync(user);
        if (await _userManager.IsLockedOutAsync(user))
        {
            return RedirectToPage("./Lockout");
        }

        ModelState.AddModelError(nameof(Input.EmailCode), _localizer["InvalidMfaCode"]);
        TwoFactorEnabled = user.TwoFactorEnabled;
        MaskEmail(user.Email);
        return Page();
    }
    
    private void MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) 
        {
            MaskedEmail = "Unknown";
            return;
        }
        
        var parts = email.Split('@');
        if (parts.Length != 2) 
        {
            MaskedEmail = email;
            return;
        }
        
        var name = parts[0];
        var domain = parts[1];
        
        if (name.Length <= 2)
        {
            MaskedEmail = $"{name[0]}***@{domain}";
        }
        else
        {
            MaskedEmail = $"{name.Substring(0, 2)}***@{domain}";
        }
    }
    
    private async Task<ApplicationUser?> GetTwoFactorUserAsync()
    {
        // Try standard Identity method first
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        
        // Fallback: manually look up user from cookie if Identity method fails (Guid key issue)
        if (user == null)
        {
            var twoFactorPrincipal = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
            if (twoFactorPrincipal.Succeeded && twoFactorPrincipal.Principal != null)
            {
                var userIdClaim = twoFactorPrincipal.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    user = await _userManager.FindByIdAsync(userId.ToString());
                }
            }
        }
        
        return user;
    }

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
