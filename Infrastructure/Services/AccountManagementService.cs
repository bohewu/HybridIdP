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
            var ipAddress = GetClientIpAddress();
            var userAgent = GetClientUserAgent();
            await _auditService.LogAccountSwitchAsync(
                currentUserId,
                targetAccountId,
                reason,
                ipAddress,
                userAgent);

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

    private string GetClientIpAddress()
    {
        try
        {
            return _signInManager.Context?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private string GetClientUserAgent()
    {
        try
        {
            return _signInManager.Context?.Request?.Headers["User-Agent"].ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
