using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Web.IdP.Api.Admin;

[ApiController]
[Route("api/admin/security/policies")]
[Authorize(Roles = "Admin")] // Overall admin protection
public class SecurityPolicyController : ControllerBase
{
    private readonly ISecurityPolicyService _securityPolicyService;

    public SecurityPolicyController(ISecurityPolicyService securityPolicyService)
    {
        _securityPolicyService = securityPolicyService;
    }

    [HttpGet]
    [HasPermission(Permissions.Settings.Read)]
    public async Task<IActionResult> GetPolicy()
    {
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        // Map entity to DTO
        var dto = new SecurityPolicyDto
        {
            MinPasswordLength = policy.MinPasswordLength,
            RequireUppercase = policy.RequireUppercase,
            RequireLowercase = policy.RequireLowercase,
            RequireDigit = policy.RequireDigit,
            RequireNonAlphanumeric = policy.RequireNonAlphanumeric,
            PasswordHistoryCount = policy.PasswordHistoryCount,
            PasswordExpirationDays = policy.PasswordExpirationDays,
            MinPasswordAgeDays = policy.MinPasswordAgeDays,
            MaxFailedAccessAttempts = policy.MaxFailedAccessAttempts,
            LockoutDurationMinutes = policy.LockoutDurationMinutes
        };
        
        return Ok(dto);
    }

    [HttpPut]
    [HasPermission(Permissions.Settings.Update)]
    public async Task<IActionResult> UpdatePolicy([FromBody] SecurityPolicyDto policyDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var updatedBy = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
        await _securityPolicyService.UpdatePolicyAsync(policyDto, updatedBy);

        return NoContent(); // 204 No Content is appropriate for a successful update
    }
}
