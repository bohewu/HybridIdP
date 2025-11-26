using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Controllers;

/// <summary>
/// Test controller for demonstrating scope-based authorization
/// This controller is used for integration testing and validation
/// </summary>
[ApiController]
[Route("api/test/[controller]")]
public class ScopeProtectedController : ControllerBase
{
    /// <summary>
    /// Public endpoint - no authorization required
    /// </summary>
    [HttpGet("public")]
    public IActionResult GetPublic()
    {
        return Ok(new { message = "This is a public endpoint", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Protected endpoint requiring authentication but no specific scope
    /// </summary>
    [Authorize]
    [HttpGet("authenticated")]
    public IActionResult GetAuthenticated()
    {
        return Ok(new 
        { 
            message = "This endpoint requires authentication only",
            user = User.Identity?.Name,
            timestamp = DateTime.UtcNow 
        });
    }

    /// <summary>
    /// Protected endpoint requiring "api:company:read" scope
    /// </summary>
    [Authorize(Policy = "RequireScope:api:company:read")]
    [HttpGet("company")]
    public IActionResult GetCompanyData()
    {
        return Ok(new 
        { 
            message = "Company data access granted",
            scope = "api:company:read",
            data = new { companyName = "Acme Corp", employees = 100 },
            timestamp = DateTime.UtcNow 
        });
    }

    /// <summary>
    /// Protected endpoint requiring "api:company:write" scope
    /// </summary>
    [Authorize(Policy = "RequireScope:api:company:write")]
    [HttpPost("company")]
    public IActionResult UpdateCompanyData([FromBody] object data)
    {
        return Ok(new 
        { 
            message = "Company data updated successfully",
            scope = "api:company:write",
            timestamp = DateTime.UtcNow 
        });
    }

    /// <summary>
    /// Protected endpoint requiring "api:admin" scope
    /// </summary>
    [Authorize(Policy = "RequireScope:api:admin")]
    [HttpGet("admin")]
    public IActionResult GetAdminData()
    {
        return Ok(new 
        { 
            message = "Admin access granted",
            scope = "api:admin",
            timestamp = DateTime.UtcNow 
        });
    }

    /// <summary>
    /// Protected endpoint requiring "openid" scope
    /// </summary>
    [Authorize(Policy = "RequireScope:openid")]
    [HttpGet("openid")]
    public IActionResult GetOpenIdData()
    {
        return Ok(new 
        { 
            message = "OpenID access granted",
            scope = "openid",
            user = User.Identity?.Name,
            claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
            timestamp = DateTime.UtcNow 
        });
    }
}
