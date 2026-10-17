using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenIddict.Abstractions;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Dashboard endpoints split from AdminController as part of refactor toward single-responsibility controllers.
/// Route kept identical: api/admin/dashboard/stats
/// </summary>
[ApiController]
[Route("api/admin/dashboard")] // preserve existing URL segment used by frontend
[ApiAuthorize]
[ValidateCsrfForCookies]
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
    /// Get sanitized status for the configured database and Redis dependencies.
    /// </summary>
    [HttpGet("health")]
    [HasPermission(Permissions.Monitoring.Read)]
    [EnableRateLimiting("dependency-readiness")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<DashboardHealthDto>> GetHealthAsync(
        [FromServices] HealthCheckService healthCheckService,
        CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Name is "sqlserver" or "redis" or "Database (PostgreSQL)",
            cancellationToken);

        var entries = report.Entries.ToDictionary(
            entry => entry.Key == "Database (PostgreSQL)" ? "postgresql" : entry.Key,
            entry => new DashboardDependencyHealthDto(entry.Value.Status.ToString()));

        return Ok(new DashboardHealthDto(report.Status.ToString(), DateTimeOffset.UtcNow, entries));
    }

    /// <summary>
    /// Get dashboard statistics including total counts of clients, scopes, and users.
    /// </summary>
    /// <returns>DashboardStatsDto</returns>
    [HttpGet("stats")]
    [HasPermission(Permissions.Monitoring.Read)]
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
