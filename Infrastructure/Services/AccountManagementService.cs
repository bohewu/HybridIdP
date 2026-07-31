using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public partial class AccountManagementService : IAccountManagementService
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationDbContext _dbContext; // Need concrete type for Roles/UserRoles
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ISessionService _sessionService;
    private readonly IAuditService _auditService;
    private readonly ILoginService _loginService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IPasskeyService _passkeyService;
    private readonly ILogger<AccountManagementService> _logger;
    private readonly TimeProvider _timeProvider;

    public AccountManagementService(
        IApplicationDbContext db,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        ISessionService sessionService,
        IAuditService auditService,
        ILoginService loginService,
        ISecurityPolicyService securityPolicyService,
        IPasskeyService passkeyService,
        ILogger<AccountManagementService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _sessionService = sessionService;
        _auditService = auditService;
        _loginService = loginService;
        _securityPolicyService = securityPolicyService;
        _passkeyService = passkeyService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountManagementService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IEnumerable<LinkedAccountDto>> GetMyLinkedAccountsAsync(Guid userId)
    {
        // Find current user's PersonId
        var currentUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (currentUser?.PersonId == null)
        {
            LogUserNotFoundOrNoPersonId(userId);
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
                u.IsActive,
                u.LastLoginDate,
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
            IsActive = u.IsActive,
            LastLoginDate = u.LastLoginDate
        });
    }

    public async Task<bool> SwitchToAccountAsync(
        Guid currentUserId,
        Guid targetAccountId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get both users
            var currentUser = await _userManager.FindByIdAsync(currentUserId.ToString());
            var targetUser = await _userManager.FindByIdAsync(targetAccountId.ToString());

            if (currentUser == null || targetUser == null)
            {
                LogUserNotFoundForSwitch(currentUserId, targetAccountId);
                return false;
            }

            // Verify both users belong to the same Person (security check)
            if (currentUser.PersonId != targetUser.PersonId || currentUser.PersonId == null)
            {
                LogAccountSwitchPersonMismatch(
                    currentUserId, targetAccountId, currentUser.PersonId, targetUser.PersonId);
                return false;
            }

            if (!await IsEligibleForAccountSwitchAsync(
                    currentUser,
                    targetUser,
                    cancellationToken))
            {
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

            LogAccountSwitched(currentUserId, targetAccountId, reason);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogAccountSwitchError(ex, currentUserId, targetAccountId);
            return false;
        }
    }

    private async Task<bool> IsEligibleForAccountSwitchAsync(
        ApplicationUser currentUser,
        ApplicationUser targetUser,
        CancellationToken cancellationToken)
    {
        foreach (var user in new[] { currentUser, targetUser })
        {
            var eligibility =
                await _loginService.ValidateExternalUserSignInAsync(user, cancellationToken);
            if (!eligibility.IsSuccess)
            {
                LogAccountSwitchEligibilityBlocked(user.Id, eligibility.Status.ToString());
                return false;
            }

            if (!await _signInManager.CanSignInAsync(user))
            {
                LogAccountSwitchIdentityPolicyBlocked(user.Id);
                return false;
            }
        }

        var eitherAccountRequiresMfa =
            currentUser.TwoFactorEnabled ||
            currentUser.EmailMfaEnabled ||
            targetUser.TwoFactorEnabled ||
            targetUser.EmailMfaEnabled;
        if (eitherAccountRequiresMfa && !HasCompletedMfaInCurrentSession())
        {
            LogAccountSwitchMfaRequired(currentUser.Id, targetUser.Id);
            return false;
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (!policy.EnforceMandatoryMfaEnrollment ||
            targetUser.TwoFactorEnabled ||
            targetUser.EmailMfaEnabled)
        {
            return true;
        }

        var passkeys =
            await _passkeyService.GetUserPasskeysAsync(targetUser.Id, cancellationToken);
        if (passkeys.Count > 0)
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (targetUser.MfaRequirementNotifiedAt == null)
        {
            targetUser.MfaRequirementNotifiedAt = now;
            var updateResult = await _userManager.UpdateAsync(targetUser);
            if (!updateResult.Succeeded)
            {
                LogAccountSwitchMfaNotificationUpdateFailed(targetUser.Id);
                return false;
            }
        }

        var enforcementTime = targetUser.MfaRequirementNotifiedAt.Value
            .AddDays(policy.MfaEnforcementGracePeriodDays);
        if (now >= enforcementTime)
        {
            LogAccountSwitchEnrollmentRequired(targetUser.Id);
            return false;
        }

        return true;
    }

    private bool HasCompletedMfaInCurrentSession()
    {
        var principal = _signInManager.Context?.User;
        return principal?.Claims.Any(claim =>
            (claim.Type == AuthConstants.ClaimTypes.Amr ||
             claim.Type == AuthConstants.ClaimTypes.AuthenticationMethod) &&
            string.Equals(
                claim.Value,
                AuthConstants.Amr.Mfa,
                StringComparison.OrdinalIgnoreCase)) == true;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} not found or has no PersonId")]
    partial void LogUserNotFoundOrNoPersonId(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User not found: CurrentUserId={CurrentUserId}, TargetAccountId={TargetAccountId}")]
    partial void LogUserNotFoundForSwitch(Guid currentUserId, Guid targetAccountId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {CurrentUserId} attempted to switch to account {TargetAccountId} with different PersonId. Current PersonId: {CurrentPersonId}, Target PersonId: {TargetPersonId}")]
    partial void LogAccountSwitchPersonMismatch(Guid currentUserId, Guid targetAccountId, Guid? currentPersonId, Guid? targetPersonId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account switch blocked because user {UserId} failed sign-in eligibility with status {Status}")]
    partial void LogAccountSwitchEligibilityBlocked(Guid userId, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account switch blocked because Identity policy does not allow user {UserId} to sign in")]
    partial void LogAccountSwitchIdentityPolicyBlocked(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account switch from {CurrentUserId} to {TargetAccountId} requires MFA in the current session")]
    partial void LogAccountSwitchMfaRequired(Guid currentUserId, Guid targetAccountId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account switch blocked because target user {TargetUserId} must complete mandatory MFA enrollment")]
    partial void LogAccountSwitchEnrollmentRequired(Guid targetUserId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Account switch blocked because the MFA notification state for target user {TargetUserId} could not be persisted")]
    partial void LogAccountSwitchMfaNotificationUpdateFailed(Guid targetUserId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {CurrentUserId} switched to account {TargetAccountId}. Reason: {Reason}")]
    partial void LogAccountSwitched(Guid currentUserId, Guid targetAccountId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error switching account from {CurrentUserId} to {TargetAccountId}")]
    partial void LogAccountSwitchError(Exception ex, Guid currentUserId, Guid targetAccountId);
}
