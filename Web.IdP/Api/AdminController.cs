using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api;

/// <summary>
/// Legacy admin API controller.
/// Most endpoints have been refactored into dedicated controllers:
/// - DashboardController: Dashboard stats
/// - PermissionsController: Current user permissions
/// - ClaimsController: Claim definitions CRUD
/// - ScopeClaimsController: Scope-to-claims mapping
/// - ScopesController: OIDC scopes CRUD
/// - ClientsController: OIDC clients CRUD
/// - RolesController: Roles CRUD and permissions
/// - UsersController: Users CRUD and role assignment
/// 
/// This controller is retained only for the health check endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
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
            message = "Admin API is accessible and refactored into focused controllers",
            timestamp = DateTime.UtcNow,
            user = User.Identity?.Name
        });
    }
}