using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api;

/// <summary>
/// Admin API controller for management operations.
/// All endpoints require the Admin role.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class AdminController : ControllerBase
{
    /// <summary>
    /// Health check endpoint to verify admin API is accessible and authorization is working.
    /// </summary>
    /// <returns>OK with a simple status message.</returns>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            message = "Admin API is accessible",
            timestamp = DateTime.UtcNow,
            user = User.Identity?.Name
        });
    }
}
