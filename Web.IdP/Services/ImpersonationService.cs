using System.Security.Claims;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Services;

public class ImpersonationService : IImpersonationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserClaimsPrincipalFactory<ApplicationUser> _userClaimsPrincipalFactory;

    public ImpersonationService(
        UserManager<ApplicationUser> userManager,
        IUserClaimsPrincipalFactory<ApplicationUser> userClaimsPrincipalFactory)
    {
        _userManager = userManager;
        _userClaimsPrincipalFactory = userClaimsPrincipalFactory;
    }

    public async Task<(bool Success, ClaimsPrincipal? Principal, string? Error)> StartImpersonationAsync(Guid currentUserId, Guid targetUserId)
    {
        // 1. Prevent self-impersonation
        if (currentUserId == targetUserId)
        {
            return (false, null, "Cannot impersonate yourself");
        }

        // 2. Fetch admin (actor)
        var adminUser = await _userManager.FindByIdAsync(currentUserId.ToString());
        if (adminUser == null)
        {
            return (false, null, "Current user not found");
        }

        // 3. Fetch target user
        var targetUser = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (targetUser == null)
        {
            return (false, null, "Target user not found");
        }

        // 4. Security check: Prevent impersonating other admins
        if (await _userManager.IsInRoleAsync(targetUser, AuthConstants.Roles.Admin))
        {
            return (false, null, "Cannot impersonate another administrator");
        }

        // 5. Create principal for target user
        var principal = await _userClaimsPrincipalFactory.CreateAsync(targetUser);
        var identity = (ClaimsIdentity)principal.Identity!;

        // 6. Create Actor identity (The Admin)
        var actorIdentity = new ClaimsIdentity();
        actorIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, adminUser.Id.ToString()));
        actorIdentity.AddClaim(new Claim("sub", adminUser.Id.ToString()));
        if (!string.IsNullOrEmpty(adminUser.UserName))
        {
            actorIdentity.AddClaim(new Claim(ClaimTypes.Name, adminUser.UserName));
            actorIdentity.AddClaim(new Claim("name", adminUser.UserName));
        }

        // Attach actor
        identity.Actor = actorIdentity;

        return (true, principal, null);
    }

    public async Task<(bool Success, ClaimsPrincipal? Principal, string? Error)> RevertImpersonationAsync(ClaimsPrincipal currentPrincipal)
    {
        var currentIdentity = currentPrincipal.Identity as ClaimsIdentity;
        if (currentIdentity == null)
        {
            return (false, null, "Not authenticated");
        }

        // 1. Check if actually impersonating
        if (currentIdentity.Actor == null)
        {
            return (false, null, "Not currently impersonating");
        }

        var actor = currentIdentity.Actor;
        var originalUserSub = actor.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                              ?? actor.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(originalUserSub))
        {
            return (false, null, "Original user identifier not found");
        }

        // 2. Fetch original admin user
        var originalUser = await _userManager.FindByIdAsync(originalUserSub);
        if (originalUser == null)
        {
             // Determine if we should treat this as a forced logout (Success but null principal?)
             // Pattern: fail, let controller handle signout
             return (false, null, "Original user not found");
        }

        // 3. Create principal for original user
        var principal = await _userClaimsPrincipalFactory.CreateAsync(originalUser);

        return (true, principal, null);
    }
}
