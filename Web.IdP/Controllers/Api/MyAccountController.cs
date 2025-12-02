using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Web.IdP.Controllers.Api;

/// <summary>
/// Phase 11.3: My Account API
/// REST API endpoints for account and role management
/// </summary>
[Authorize]
[ApiController]
[Route("api/my")]
public class MyAccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IAccountManagementService _accountManagementService;
    private readonly ISessionService _sessionService;
    private readonly ILogger<MyAccountController> _logger;

    public MyAccountController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IAccountManagementService accountManagementService,
        ISessionService sessionService,
        ILogger<MyAccountController> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _accountManagementService = accountManagementService;
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/my/accounts
    /// Returns all accounts linked to the same Person as the current user
    /// </summary>
    [HttpGet("accounts")]
    [ProducesResponseType(typeof(IEnumerable<LinkedAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyLinkedAccounts()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var accounts = await _accountManagementService.GetMyLinkedAccountsAsync(userId);
        return Ok(accounts);
    }

    /// <summary>
    /// GET /api/my/roles
    /// Returns all roles assigned to the current user with active status
    /// </summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IEnumerable<AvailableRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyAvailableRoles()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var roles = await _accountManagementService.GetMyAvailableRolesAsync(userId);
        return Ok(roles);
    }

    /// <summary>
    /// POST /api/my/switch-role
    /// Switch to a different role in the current session
    /// Requires password for Admin role
    /// </summary>
    [HttpPost("switch-role")]
    [ProducesResponseType(typeof(SwitchRoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SwitchRole([FromBody] SwitchRoleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        // Get current session's authorization ID
        var sessionAuthorizationId = User.FindFirstValue("session_id");
        if (string.IsNullOrEmpty(sessionAuthorizationId))
        {
            _logger.LogWarning("User {UserId} attempted role switch without session ID", userId);
            return BadRequest(new { error = "No active session found" });
        }

        var result = await _accountManagementService.SwitchRoleAsync(
            userId,
            sessionAuthorizationId,
            request.RoleId,
            request.Password);

        if (!result)
        {
            return BadRequest(new SwitchRoleResponse
            {
                Success = false,
                Error = "Failed to switch role. Check if you have permission and provided correct password for Admin role."
            });
        }

        // Phase 11.4: Update active_role claim in the current authentication session
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user != null)
        {
            var role = await _roleManager.FindByIdAsync(request.RoleId.ToString());
            
            if (role != null)
            {
                var identity = (ClaimsIdentity)User.Identity!;
                
                // Remove old active_role claim and add new one
                var existingActiveRoleClaim = identity.FindFirst("active_role");
                if (existingActiveRoleClaim != null)
                {
                    identity.RemoveClaim(existingActiveRoleClaim);
                }
                identity.AddClaim(new Claim("active_role", role.Name!));

                // Update authentication cookie
                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = true });

                _logger.LogInformation("Updated active_role claim to {RoleName} for user {UserId}", role.Name, userId);
            }
        }

        return Ok(new SwitchRoleResponse
        {
            Success = true,
            NewRoleId = request.RoleId
        });
    }

    /// <summary>
    /// POST /api/my/switch-account
    /// Switch to a different account linked to the same Person
    /// Will sign out and sign in as the target account
    /// </summary>
    [HttpPost("switch-account")]
    [ProducesResponseType(typeof(SwitchAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SwitchAccount([FromBody] SwitchAccountRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Unauthorized();
        }

        // Verify target account exists
        var targetUser = await _userManager.FindByIdAsync(request.TargetAccountId.ToString());
        if (targetUser == null)
        {
            return NotFound(new { error = "Target account not found" });
        }

        var result = await _accountManagementService.SwitchToAccountAsync(
            currentUserId,
            request.TargetAccountId,
            request.Reason ?? "User requested account switch");

        if (!result)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new SwitchAccountResponse
            {
                Success = false,
                Error = "Failed to switch account. You can only switch to accounts belonging to the same Person."
            });
        }

        return Ok(new SwitchAccountResponse
        {
            Success = true,
            NewAccountId = request.TargetAccountId,
            NewAccountEmail = targetUser.Email
        });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Unable to extract user ID from claims");
            return Guid.Empty;
        }
        return userId;
    }
}

#region Request/Response Models

public class SwitchRoleRequest
{
    public Guid RoleId { get; set; }
    public string? Password { get; set; } // Required for Admin role
}

public class SwitchRoleResponse
{
    public bool Success { get; set; }
    public Guid? NewRoleId { get; set; }
    public string? Error { get; set; }
}

public class SwitchAccountRequest
{
    public Guid TargetAccountId { get; set; }
    public string? Reason { get; set; }
}

public class SwitchAccountResponse
{
    public bool Success { get; set; }
    public Guid? NewAccountId { get; set; }
    public string? NewAccountEmail { get; set; }
    public string? Error { get; set; }
}

#endregion
