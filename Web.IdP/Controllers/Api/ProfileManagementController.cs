using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options; // Added
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Core.Application.Interfaces;
using Core.Application.Utilities;
using Core.Application.Options; // Added
using Web.IdP.Helpers;

namespace Web.IdP.Controllers.Api;

/// <summary>
/// API controller for managing user profile information
/// </summary>
[Authorize]
[ApiController]
[Route("api/profile")]
[AutoValidateAntiforgeryToken]
public class ProfileManagementController : ControllerBase
{
    private readonly Web.IdP.Services.ICurrentUserLifecycleEligibility _lifecycleEligibility;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IPasskeyService _passkeyService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ProfileManagementController> _logger;
    private readonly ExternalLoginOptions _externalLoginOptions; // Added

    public ProfileManagementController(
        Web.IdP.Services.ICurrentUserLifecycleEligibility lifecycleEligibility,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext dbContext,
        ISecurityPolicyService securityPolicyService,
        IPasskeyService passkeyService,
        IAuditService auditService,
        ILogger<ProfileManagementController> logger,
        IOptions<ExternalLoginOptions> externalLoginOptions) // Added
    {
        _lifecycleEligibility = lifecycleEligibility;
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _securityPolicyService = securityPolicyService;
        _passkeyService = passkeyService;
        _auditService = auditService;
        _logger = logger;
        _externalLoginOptions = externalLoginOptions.Value;
    }

    /// <summary>
    /// GET api/profile - Get current user's profile information
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetProfile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            _logger.LogWarning("GetProfile called but user not found");
            return NotFound(new { error = "User not found" });
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        var hasLocalPassword = await _userManager.HasPasswordAsync(user);
        var externalLogins = await _userManager.GetLoginsAsync(user);

        // Determine available providers for linking
        var allSchemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        // Count existing logins per provider
        var loginCountPerProvider = externalLogins
            .GroupBy(l => l.LoginProvider)
            .ToDictionary(g => g.Key, g => g.Count());
        
        var availableProviders = allSchemes
            .Where(s => s.Name != Core.Domain.Constants.AuthConstants.Providers.Legacy) // Exclude Legacy
            .Where(s => 
            {
                // If MaxLoginsPerProvider is 0, allow unlimited
                if (_externalLoginOptions.MaxLoginsPerProvider == 0)
                    return true;
                
                // Check if this provider has reached the limit
                if (loginCountPerProvider.TryGetValue(s.Name, out var count))
                {
                    return count < _externalLoginOptions.MaxLoginsPerProvider;
                }
                
                // Provider not yet linked, allow it
                return true;
            })
            .Select(s => new AvailableProviderDto 
            {
                Scheme = s.Name,
                DisplayName = s.DisplayName ?? s.Name
            })
            .ToList();

        Person? person = null;
        if (user.PersonId.HasValue)
        {
            person = await _dbContext.Persons
                .FirstOrDefaultAsync(p => p.Id == user.PersonId.Value);
        }

        var dto = new ProfileDto
        {
            UserId = user.Id,
            DisplayName = NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName)
                ?? (person != null ? NameFormatter.BuildDisplayName(person.FirstName, person.MiddleName, person.LastName) : null)
                ?? user.UserName ?? string.Empty,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            EmailConfirmed = user.EmailConfirmed,
            
            // Initial map from User
            Locale = user.Locale,
            TimeZone = user.TimeZone,
            
            HasLocalPassword = hasLocalPassword,
            AllowPasswordChange = policy.AllowSelfPasswordChange && hasLocalPassword,
            TwoFactorEnabled = user.TwoFactorEnabled,
            EmailMfaEnabled = user.EmailMfaEnabled,
            PasskeyEnabled = (await _passkeyService.GetUserPasskeysAsync(user.Id)).Count > 0,
            ExternalLogins = externalLogins
                .Where(l => l.LoginProvider != Core.Domain.Constants.AuthConstants.Providers.Legacy) // Hide legacy provider from UI
                .Select(l => new ExternalLoginDto
                {
                    LoginProvider = l.LoginProvider,
                    ProviderKey = l.ProviderKey,
                    ProviderDisplayName = l.ProviderDisplayName
                }).ToList(),
            AvailableProviders = availableProviders
        };

        if (person != null)
        {
            dto.Person = new PersonProfileDto
            {
                PersonId = person.Id,
                FullName = NameFormatter.BuildDisplayName(person.FirstName, person.MiddleName, person.LastName)
                    ?? string.Empty,
                EmployeeId = person.EmployeeId,
                Department = person.Department,
                JobTitle = person.JobTitle,
                PhoneNumber = person.PhoneNumber,
                Locale = person.Locale,
                TimeZone = person.TimeZone
            };

            // Fallback logic: If User definition is missing, use Person's
            if (string.IsNullOrEmpty(dto.Locale)) dto.Locale = person.Locale;
            if (string.IsNullOrEmpty(dto.TimeZone)) dto.TimeZone = person.TimeZone;
            
            // Note: PhoneNumber is not yet on Root DTO, so we rely on Person.PhoneNumber
        }

        _logger.LogInformation("Profile retrieved for user {UserId}", user.Id);
        return Ok(dto);
    }

    /// <summary>
    /// PUT api/profile - Update user's profile (Person table fields)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        // 1. Update ApplicationUser (Always)
        var userChanged = false;
        if (user.PhoneNumber != request.PhoneNumber) 
        {
            user.PhoneNumber = request.PhoneNumber;
            userChanged = true;
        }
        if (user.Locale != request.Locale)
        {
            user.Locale = request.Locale;
            userChanged = true;
        }
        if (user.TimeZone != request.TimeZone)
        {
            user.TimeZone = request.TimeZone;
            userChanged = true;
        }

        if (userChanged)
        {
            await _userManager.UpdateAsync(user);
        }

        // 2. Update Person (If linked)
        if (user.PersonId.HasValue)
        {
            var person = await _dbContext.Persons
                .FirstOrDefaultAsync(p => p.Id == user.PersonId.Value);

            if (person != null)
            {
                // Sync to Person
                person.PhoneNumber = request.PhoneNumber;
                person.Locale = request.Locale;
                person.TimeZone = request.TimeZone;
                person.ModifiedBy = user.Id;
                person.ModifiedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("User {UserId} updated profile and synced to Person {PersonId}", user.Id, person.Id);
            }
        }
        else
        {
            _logger.LogInformation("User {UserId} updated profile (User only, no linked Person)", user.Id);
        }

        // Audit log
        await _auditService.LogEventAsync(
            eventType: "Profile.Update",
            userId: user.Id.ToString(),
            details: $"User updated profile: PhoneNumber={request.PhoneNumber}, Locale={request.Locale}, TimeZone={request.TimeZone}",
            ipAddress: null,
            userAgent: null,
            cancellationToken: cancellationToken
        );

        // Limit cookie update to valid locales only? 
        // For now, if we saved it to the DB, we trust it or checking basic specific ones.
        if (!string.IsNullOrEmpty(request.Locale))
        {
            var culture = request.Locale;
            Response.Cookies.Append(
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), Secure = true, SameSite = SameSiteMode.Lax }
            );
        }

        return Ok(new { message = "Profile updated successfully" });
    }

    /// <summary>
    /// POST api/profile/change-password - Change current user's password
    /// </summary>
    [HttpPost("change-password")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        // Check if SecurityPolicy allows self password change
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (!policy.AllowSelfPasswordChange)
        {
            _logger.LogWarning("User {UserId} ({UserName}) attempted password change but feature is disabled by policy",
                user.Id, user.UserName);
            return StatusCode(403, new { error = "Password change is currently disabled by system policy" });
        }

        // Check if user has a local password (not external login)
        var hasLocalPassword = await _userManager.HasPasswordAsync(user);
        if (!hasLocalPassword)
        {
            _logger.LogWarning("User {UserId} ({UserName}) attempted password change but has no local password (external login)",
                user.Id, user.UserName);
            return BadRequest(new { error = "Cannot change password for external login accounts" });
        }

        if (await _userManager.IsLockedOutAsync(user))
            return StatusCode(429, new { error = "accountLocked" });

        IdentityResult result;
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var previousHash = user.PasswordHash;
            // Validate against the existing password date and history before changing either.
            result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (result.Succeeded)
            {
                if (policy.PasswordHistoryCount > 0 && !string.IsNullOrWhiteSpace(previousHash))
                {
                    List<string> history;
                    try
                    {
                        history = JsonSerializer.Deserialize<List<string>>(user.PasswordHistory) ?? [];
                    }
                    catch (JsonException)
                    {
                        history = [];
                    }
                    history.Insert(0, previousHash);
                    user.PasswordHistory = JsonSerializer.Serialize(
                        history.Distinct(StringComparer.Ordinal).Take(policy.PasswordHistoryCount).ToList());
                }
                user.LastPasswordChangeDate = DateTime.UtcNow;
                result = await _userManager.UpdateAsync(user);
            }

            if (result.Succeeded)
            {
                await _auditService.LogEventAsync(
                    eventType: "Profile.ChangePassword",
                    userId: user.Id.ToString(),
                    details: "User successfully changed their password",
                    ipAddress: null,
                    userAgent: null,
                    cancellationToken: cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
                await PasswordConfirmation.RecordFailureAsync(_userManager, user, policy);
            _logger.LogWarning("Password change failed for user {UserId}: {Errors}",
                user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));

            return BadRequest(new
            {
                errors = result.Errors.Select(e => new
                {
                    code = e.Code,
                    description = e.Description
                })
            });
        }

        _logger.LogInformation("User {UserId} ({UserName}) successfully changed their password",
            user.Id, user.UserName);

        return Ok(new { message = "Password changed successfully" });
    }

    /// <summary>
    /// POST api/profile/remove-login - Remove an external login
    /// </summary>
    [HttpPost("remove-login")]
    public async Task<IActionResult> RemoveLogin([FromBody] RemoveLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (User.HasClaim(claim => claim.Type == AuthConstants.Claims.ImpersonatorId) ||
            User.Identities.Any(identity => identity.Actor != null))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "External login removal is unavailable during impersonation." });
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authentication.Succeeded ||
            authentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id.ToString() ||
            string.IsNullOrEmpty(user.SecurityStamp) ||
            authentication.Principal.FindFirstValue(_userManager.Options.ClaimsIdentity.SecurityStampClaimType) != user.SecurityStamp ||
            !Web.IdP.Helpers.AuthorizationAuthenticationSession.HasCurrentAssuranceVersion(authentication.Principal))
            return Unauthorized();

        var result = await _userManager.RemoveLoginAsync(user, request.LoginProvider, request.ProviderKey);
        if (!result.Succeeded)
        {
             _logger.LogWarning("RemoveLogin failed for user {UserId}: {Errors}",
                user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
            return BadRequest(new { error = "Failed to remove login" });
        }

        // Re-sign in the user to update the security stamp and cookies
        if (!await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken))
        {
            return Unauthorized();
        }
        Web.IdP.Helpers.AuthorizationAuthenticationSession.PreserveAssurance(HttpContext, authentication.Principal!);
        await _signInManager.RefreshSignInAsync(user);
        
        // Audit log
        await _auditService.LogEventAsync(
            eventType: "Profile.RemoveLogin",
            userId: user.Id.ToString(),
            details: $"User removed external login: {request.LoginProvider}",
            ipAddress: null,
            userAgent: null,
            cancellationToken: cancellationToken
        );

        return Ok(new { message = "Login removed successfully" });
    }
}

