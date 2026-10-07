using System.Security.Claims;
using Core.Application;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Authorization;

public static class PrivilegedRoleAssignmentPolicy
{
    public const string TargetMfaError = "Target user must enable MFA before being assigned privileged roles.";

    public static bool ContainsProtectedRole(IEnumerable<string> roles, PrivilegedRoleProtectionOptions options)
    {
        var protectedRoles = (options.ProtectedRoles ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles.Any(role => !string.IsNullOrWhiteSpace(role) && protectedRoles.Contains(role.Trim()));
    }

    public static bool HasCompletedMfa(ClaimsPrincipal principal, PrivilegedRoleProtectionOptions options)
    {
        var methods = principal.Claims
            .Where(claim => claim.Type == AuthConstants.ClaimTypes.Amr ||
                            claim.Type == AuthConstants.ClaimTypes.AuthenticationMethod)
            .Select(claim => claim.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (methods.Contains(AuthConstants.Amr.HardwareKey) && !options.CountPasskeyAsMfa &&
            !methods.Contains(AuthConstants.Amr.Otp))
            return false;
        return methods.Contains(AuthConstants.Amr.Mfa) ||
               (options.CountPasskeyAsMfa && methods.Contains(AuthConstants.Amr.HardwareKey));
    }

    public static async Task<bool> CanReceiveAsync(ApplicationUser user, IEnumerable<string> addedRoles,
        PrivilegedRoleProtectionOptions options, IApplicationDbContext context, CancellationToken cancellationToken)
    {
        if (!options.RequireTargetMfaForPrivilegedRoleAssignment || !ContainsProtectedRole(addedRoles, options))
            return true;
        return user.TwoFactorEnabled || user.EmailMfaEnabled ||
               (options.CountPasskeyAsMfa && await context.UserCredentials
                   .AnyAsync(credential => credential.UserId == user.Id && credential.DisabledAtUtc == null, cancellationToken));
    }
}
