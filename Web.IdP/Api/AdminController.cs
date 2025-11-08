using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using System.Linq;
using Infrastructure;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using DomainPermissions = Core.Domain.Constants.Permissions;

namespace Web.IdP.Api;

/// <summary>
/// Admin API controller for management operations.
/// All endpoints require specific permissions (enforced via HasPermission attribute).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // Require authentication, permissions checked per-endpoint
public class AdminController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IUserManagementService _userManagementService;
    private readonly IRoleManagementService _roleManagementService;

    public AdminController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        IUserManagementService userManagementService,
        IRoleManagementService roleManagementService)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _userManager = userManager;
        _context = context;
        _userManagementService = userManagementService;
        _roleManagementService = roleManagementService;
    }

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

    // Moved to DashboardController: GET api/admin/dashboard/stats

    #region OIDC Clients
    // Moved to ClientsController: GET/GET{id}/POST/PUT{id}/DELETE{id} api/admin/clients
    #endregion

    #region OIDC Scopes
    // Moved to ScopesController: GET/POST/PUT/DELETE api/admin/scopes
    #endregion

    #region User Claims Management
    // Moved to ClaimsController: GET/POST/PUT/DELETE api/admin/claims
    #endregion

    #region Scope-to-Claims Mapping
    // Moved to ScopeClaimsController: GET/PUT api/admin/scopes/{scopeId}/claims
    #endregion

    #region DTOs
    // All DTOs moved to Core.Application.DTOs:
    // - DashboardStatsDto
    // - ClaimDefinitionDto, CreateClaimRequest, UpdateClaimRequest (ClaimDtos.cs)
    // - ScopeClaimDto, UpdateScopeClaimsRequest (ScopeClaimDtos.cs)
    // - ScopeSummary, CreateScopeRequest, UpdateScopeRequest (ScopeDtos.cs)
    // - ClientSummary, CreateClientRequest, UpdateClientRequest (ClientDtos.cs)
    #endregion

    #region User Management
    // Moved to UsersController: GET/GET{id}/POST/PUT{id}/DELETE{id} api/admin/users
    // Moved to UsersController: POST api/admin/users/{id}/deactivate
    // Moved to UsersController: POST api/admin/users/{id}/reactivate
    // Moved to UsersController: PUT api/admin/users/{id}/roles
    #endregion

    #region Role Management
    // Moved to RolesController: GET/GET{id}/POST/PUT{id}/DELETE{id} api/admin/roles
    // Moved to RolesController: GET api/admin/roles/permissions
    #endregion
}