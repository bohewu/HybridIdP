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
