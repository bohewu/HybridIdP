using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Application.Interfaces;

namespace Web.IdP.Controllers.Api;

/// <summary>
/// API controller for managing user profile information
/// </summary>
[Authorize]
[ApiController]
[Route("api/profile")]
public class ProfileManagementController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IPasskeyService _passkeyService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ProfileManagementController> _logger;

    public ProfileManagementController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        ISecurityPolicyService securityPolicyService,
        IPasskeyService passkeyService,
        IAuditService auditService,
        ILogger<ProfileManagementController> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _securityPolicyService = securityPolicyService;
        _passkeyService = passkeyService;
        _auditService = auditService;
        _logger = logger;
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

        var dto = new ProfileDto
        {
            UserId = user.Id,
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
            ExternalLogins = externalLogins.Select(l => new ExternalLoginDto
            {
                LoginProvider = l.LoginProvider,
                ProviderDisplayName = l.ProviderDisplayName
            }).ToList()
        };

        // Load linked Person if exists
        if (user.PersonId.HasValue)
        {
            var person = await _dbContext.Persons
                .FirstOrDefaultAsync(p => p.Id == user.PersonId.Value);

            if (person != null)
            {
                dto.Person = new PersonProfileDto
                {
                    PersonId = person.Id,
                    FullName = $"{person.FirstName} {person.LastName}".Trim(),
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
        }

        _logger.LogInformation("Profile retrieved for user {UserId}", user.Id);
        return Ok(dto);
    }

    /// <summary>
    /// PUT api/profile - Update user's profile (Person table fields)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
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
            userAgent: null
        );

        // Limit cookie update to valid locales only? 
        // For now, if we saved it to the DB, we trust it or checking basic specific ones.
        if (!string.IsNullOrEmpty(request.Locale))
        {
            var culture = request.Locale;
            Response.Cookies.Append(
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
                new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), Secure = true, SameSite = SameSiteMode.Lax }
            );
        }

        return Ok(new { message = "Profile updated successfully" });
    }

    /// <summary>
    /// POST api/profile/change-password - Change current user's password
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
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

        // Attempt to change password (will use DynamicPasswordValidator)
        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
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

        // Audit log
        await _auditService.LogEventAsync(
            eventType: "Profile.ChangePassword",
            userId: user.Id.ToString(),
            details: "User successfully changed their password",
            ipAddress: null,
            userAgent: null
        );

        _logger.LogInformation("User {UserId} ({UserName}) successfully changed their password",
            user.Id, user.UserName);

        return Ok(new { message = "Password changed successfully" });
    }
}
