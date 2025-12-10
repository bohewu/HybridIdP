using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Dashboard endpoints split from AdminController as part of refactor toward single-responsibility controllers.
/// Route kept identical: api/admin/dashboard/stats
/// </summary>
[ApiController]
[Route("api/admin/dashboard")] // preserve existing URL segment used by frontend
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<ApplicationUser> userManager)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _userManager = userManager;
    }

    /// <summary>
    /// Get dashboard statistics including total counts of clients, scopes, and users.
    /// </summary>
    /// <returns>DashboardStatsDto</returns>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var totalClients = 0;
        await foreach (var _ in _applicationManager.ListAsync())
        {
            totalClients++;
        }

        var totalScopes = 0;
        await foreach (var _ in _scopeManager.ListAsync())
        {
            totalScopes++;
        }

        var totalUsers = _userManager.Users.Count();

        return Ok(new DashboardStatsDto
        {
            TotalClients = totalClients,
            TotalScopes = totalScopes,
            TotalUsers = totalUsers
        });
    }
}
