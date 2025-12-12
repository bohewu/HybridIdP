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

namespace Web.IdP.Controllers;

/// <summary>
/// API controller for managing user profile information
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileManagementController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ProfileManagementController> _logger;

    public ProfileManagementController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        ISecurityPolicyService securityPolicyService,
        IAuditService auditService,
        ILogger<ProfileManagementController> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _securityPolicyService = securityPolicyService;
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
            HasLocalPassword = hasLocalPassword,
            AllowPasswordChange = policy.AllowSelfPasswordChange && hasLocalPassword,
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

        // Only allow updates if user is linked to a Person
        if (!user.PersonId.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to update profile but is not linked to a Person", user.Id);
            return BadRequest(new { error = "User is not linked to a Person entity" });
        }

        var person = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.Id == user.PersonId.Value);

        if (person == null)
        {
            _logger.LogWarning("Person {PersonId} not found for user {UserId}", user.PersonId.Value, user.Id);
            return NotFound(new { error = "Person not found" });
        }

        // Update editable Person fields
        person.PhoneNumber = request.PhoneNumber;
        person.Locale = request.Locale;
        person.TimeZone = request.TimeZone;
        person.ModifiedBy = user.Id;
        person.ModifiedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // Audit log
        await _auditService.LogEventAsync(
            eventType: "Profile.Update",
            userId: user.Id.ToString(),
            details: $"User updated profile: PhoneNumber={request.PhoneNumber}, Locale={request.Locale}, TimeZone={request.TimeZone}",
            ipAddress: null,
            userAgent: null
        );

        _logger.LogInformation("User {UserId} updated profile (Person: {PersonId})", user.Id, person.Id);

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
