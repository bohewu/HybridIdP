using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class AccountManagementService : IAccountManagementService
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationDbContext _dbContext; // Need concrete type for Roles/UserRoles
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ISessionService _sessionService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AccountManagementService> _logger;

    public AccountManagementService(
        IApplicationDbContext db,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        ISessionService sessionService,
        IAuditService auditService,
        ILogger<AccountManagementService>? logger = null)
    {
        _db = db;
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _sessionService = sessionService;
        _auditService = auditService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountManagementService>.Instance;
    }

    public async Task<IEnumerable<LinkedAccountDto>> GetMyLinkedAccountsAsync(Guid userId)
    {
        // Find current user's PersonId
        var currentUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (currentUser?.PersonId == null)
        {
            _logger.LogWarning("User {UserId} not found or has no PersonId", userId);
            return Enumerable.Empty<LinkedAccountDto>();
        }

        // Find all users with same PersonId (linked accounts)
        var linkedUsers = await _db.Users
            .AsNoTracking()
            .Where(u => u.PersonId == currentUser.PersonId)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                IsCurrentAccount = u.Id == userId,
                Roles = _dbContext.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(_dbContext.Roles,
                        ur => ur.RoleId,
                        r => r.Id,
                        (ur, r) => r.Name)
                    .ToList()
            })
            .ToListAsync();

        return linkedUsers.Select(u => new LinkedAccountDto
        {
            Id = u.Id,
            UserId = u.Id,
            UserName = u.UserName ?? string.Empty,
            Email = u.Email ?? string.Empty,
            Roles = u.Roles.Where(r => r != null).Cast<string>().ToList(),
            IsCurrentAccount = u.IsCurrentAccount,
            IsActive = false, // Will be determined by session status
            LastLoginDate = null // TODO: Implement from LoginHistory if needed
        });
    }

    public async Task<IEnumerable<AvailableRoleDto>> GetMyAvailableRolesAsync(Guid userId)
    {
        // Get user's assigned roles
        var userRoles = await (from ur in _dbContext.UserRoles
                               join r in _dbContext.Roles on ur.RoleId equals r.Id
                               where ur.UserId == userId
                               select new AvailableRoleDto
                               {
                                   RoleId = r.Id,
                                   RoleName = r.Name ?? string.Empty,
                                   Description = r.Description,
                                   IsActive = false, // Will be determined by active session
                                   RequiresPasswordConfirmation = r.NormalizedName == "ADMIN"
                               })
                              .ToListAsync();

        // Check if any role is currently active in an active session
        // Note: UserSession doesn't have IsActive property, we'll check for active authorization instead
        var activeSession = await _db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedUtc)
            .FirstOrDefaultAsync();

        if (activeSession != null)
        {
            var activeRole = userRoles.FirstOrDefault(r => r.RoleId == activeSession.ActiveRoleId);
            if (activeRole != null)
            {
                activeRole.IsActive = true;
            }
        }

        return userRoles;
    }

    public async Task<bool> SwitchRoleAsync(Guid userId, string sessionAuthorizationId, Guid roleId, string? password = null)
    {
        try
        {
            // Verify user has the target role
            var hasRole = await _dbContext.UserRoles
                .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

            if (!hasRole)
            {
                _logger.LogWarning("User {UserId} attempted to switch to unassigned role {RoleId}", userId, roleId);
                return false;
            }

            // Get role information to check if password is required
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
            if (role == null)
            {
                _logger.LogWarning("Role {RoleId} not found", roleId);
                return false;
            }

            // Admin role requires password confirmation
            if (role.NormalizedName == "ADMIN")
            {
                if (string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("User {UserId} attempted to switch to Admin role without password", userId);
                    return false;
                }

                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found in UserManager", userId);
                    return false;
                }

                var passwordValid = await _userManager.CheckPasswordAsync(user, password);
                if (!passwordValid)
                {
                    _logger.LogWarning("User {UserId} provided incorrect password for Admin role switch", userId);
                    return false;
                }
            }

            // Find the session to update
            var session = await _db.UserSessions
                .FirstOrDefaultAsync(s => s.AuthorizationId == sessionAuthorizationId && s.UserId == userId);

            if (session == null)
            {
                _logger.LogWarning("Session with AuthorizationId {AuthorizationId} not found for user {UserId}", 
                    sessionAuthorizationId, userId);
                return false;
            }

            var oldRoleId = session.ActiveRoleId;

            // Update session with new role
            session.ActiveRoleId = roleId;
            session.LastRoleSwitchUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(CancellationToken.None);

            // Log the role switch for audit
            await _auditService.LogRoleSwitchAsync(
                userId,
                oldRoleId,
                roleId,
                sessionAuthorizationId,
                _signInManager.Context?.Connection?.RemoteIpAddress?.ToString() ?? "unknown",
                _signInManager.Context?.Request?.Headers["User-Agent"].ToString() ?? "unknown");

            _logger.LogInformation("User {UserId} switched from role {OldRoleId} to {NewRoleId}", 
                userId, oldRoleId, roleId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching role for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> SwitchToAccountAsync(Guid currentUserId, Guid targetAccountId, string reason)
    {
        try
        {
            // Get both users
            var currentUser = await _userManager.FindByIdAsync(currentUserId.ToString());
            var targetUser = await _userManager.FindByIdAsync(targetAccountId.ToString());

            if (currentUser == null || targetUser == null)
            {
                _logger.LogWarning("User not found: CurrentUserId={CurrentUserId}, TargetAccountId={TargetAccountId}",
                    currentUserId, targetAccountId);
                return false;
            }

            // Verify both users belong to the same Person (security check)
            if (currentUser.PersonId != targetUser.PersonId || currentUser.PersonId == null)
            {
                _logger.LogWarning(
                    "User {CurrentUserId} attempted to switch to account {TargetAccountId} with different PersonId. " +
                    "Current PersonId: {CurrentPersonId}, Target PersonId: {TargetPersonId}",
                    currentUserId, targetAccountId, currentUser.PersonId, targetUser.PersonId);
                return false;
            }

            // Sign out current user and sign in as target user
            await _signInManager.SignOutAsync();
            await _signInManager.SignInAsync(targetUser, isPersistent: true);

            // Log the account switch for audit
            await _auditService.LogAccountSwitchAsync(
                currentUserId,
                targetAccountId,
                reason,
                _signInManager.Context?.Connection?.RemoteIpAddress?.ToString() ?? "unknown",
                _signInManager.Context?.Request?.Headers["User-Agent"].ToString() ?? "unknown");

            _logger.LogInformation("User {CurrentUserId} switched to account {TargetAccountId}. Reason: {Reason}",
                currentUserId, targetAccountId, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching account from {CurrentUserId} to {TargetAccountId}",
                currentUserId, targetAccountId);
            return false;
        }
    }
}
