using Core.Application;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using System.Security.Claims;
using Web.IdP.Helpers;

namespace Web.IdP.Services;

public sealed record AdministrativeMfaResetResult(int StatusCode, string? Error = null, string? LoginUrl = null);

public sealed class AdministrativeMfaResetService(
    IAdministrativeAuthorizationBoundary boundary,
    Microsoft.AspNetCore.Authorization.IAuthorizationService authorization,
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    IAuditService audit,
    IOptions<PrivilegedRoleProtectionOptions> protection,
    ICurrentUserLifecycleEligibility lifecycle,
    IMigrationIssuanceGuard migration)
{
    public async Task<AdministrativeMfaResetResult> BeginAsync(HttpContext http, Guid targetId, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        if (actor == null || !await CanResetTargetAsync(actor.Value.Principal, actor.Value.User, targetId))
            return new(403, "accessDenied");
        var nonce = await AccountSecurityOperationSession.BeginAsync(http, actor.Value.User,
            AccountSecurityOperationSession.MfaResetPurpose, targetId.ToString(), ct);
        if (nonce == null) return new(403, "mfaRequired");
        var returnUrl = "/Admin/Users?resetMfaTarget=" + targetId;
        return new(200, LoginUrl: await AccountSecurityOperationSession.GetLoginUrlAsync(http, actor.Value.User, returnUrl));
    }

    public async Task<AdministrativeMfaResetResult> ResetAsync(HttpContext http, Guid targetId, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return new(400, "resetReasonRequired");
        var actor = await GetActorAsync(ct);
        if (actor == null || !await CanResetTargetAsync(actor.Value.Principal, actor.Value.User, targetId))
            return new(403, "accessDenied");
        if (!await AccountSecurityOperationSession.IsAuthorizedAsync(http, actor.Value.User,
                AccountSecurityOperationSession.MfaResetPurpose, targetId.ToString()))
            return new(403, "freshAuthenticationRequired");
        var target = await users.FindByIdAsync(targetId.ToString());
        if (target == null) return new(404, "userNotFound");
        AccountSecurityOperationSession.Consume(http);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        EnsureSucceeded(await users.UpdateSecurityStampAsync(target));
        EnsureSucceeded(await users.SetTwoFactorEnabledAsync(target, false));
        EnsureSucceeded(await users.ResetAuthenticatorKeyAsync(target));
        target.EmailMfaEnabled = false;
        target.EmailMfaCode = null;
        target.EmailMfaCodeExpiry = null;
        target.EmailMfaVerificationAttempts = 0;
        EnsureSucceeded(await users.UpdateAsync(target));

        var sessions = await db.UserSessions.Where(session => session.UserId == targetId && session.RevokedUtc == null).ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.RevokedUtc = DateTime.UtcNow;
            session.RevocationReason = "AdministrativeMfaReset";
        }
        var tokenRecords = new Dictionary<string, object>(StringComparer.Ordinal);
        await foreach (var token in tokens.FindBySubjectAsync(targetId.ToString(), ct))
            await AddTokenAsync(tokenRecords, token, ct);
        await foreach (var grant in authorizations.FindBySubjectAsync(targetId.ToString(), ct))
        {
            if (!await authorizations.TryRevokeAsync(grant, ct)) throw new InvalidOperationException("Authorization revocation failed.");
            var grantId = await authorizations.GetIdAsync(grant, ct);
            if (string.IsNullOrWhiteSpace(grantId)) throw new InvalidOperationException("Authorization identifier is missing.");
            await foreach (var token in tokens.FindByAuthorizationIdAsync(grantId, ct))
                await AddTokenAsync(tokenRecords, token, ct);
        }
        foreach (var token in tokenRecords.Values)
            if (!await tokens.TryRevokeAsync(token, ct)) throw new InvalidOperationException("Token revocation failed.");
        await db.SaveChangesAsync(ct);
        await audit.LogAdministrativeEventAsync("UserMfaReset", "User", targetId.ToString(), reason.Trim(), ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return new(200);
    }

    private async Task<(ClaimsPrincipal Principal, ApplicationUser User)?> GetActorAsync(CancellationToken ct)
    {
        var authority = await boundary.ResolveAsync();
        if (authority == null || authority.IsBearer ||
            authority.Principal.FindFirst(AuthConstants.Claims.ImpersonatorId) != null ||
            !(await authorization.AuthorizeAsync(authority.Principal, null, Permissions.Users.ResetMfa)).Succeeded)
            return null;
        var id = authority.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = id == null ? null : await users.FindByIdAsync(id);
        if (actor == null || !actor.IsActive || actor.IsDeleted || actor.RequiresPasswordChange ||
            string.IsNullOrEmpty(actor.SecurityStamp) ||
            authority.Principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) != actor.SecurityStamp ||
            await users.IsLockedOutAsync(actor) || !await lifecycle.IsEligibleAsync(actor.Id, ct) ||
            !await migration.CanIssueAsync(actor.Id, ct)) return null;
        return (authority.Principal, actor);
    }

    private async Task<bool> CanResetTargetAsync(ClaimsPrincipal principal, ApplicationUser actor, Guid targetId)
    {
        var target = await users.FindByIdAsync(targetId.ToString());
        if (target == null) return true; // ResetAsync returns the normal not-found response.
        var protectedRoles = protection.Value.ProtectedRoles.Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!(await users.GetRolesAsync(target)).Any(protectedRoles.Contains)) return true;
        var activeRole = principal.FindFirstValue("active_role");
        return AuthorizationRoleClaimResolver.IsInIdpRole(principal, AuthConstants.Roles.Admin) &&
            (string.IsNullOrEmpty(activeRole) || string.Equals(activeRole, AuthConstants.Roles.Admin, StringComparison.OrdinalIgnoreCase)) &&
            await users.IsInRoleAsync(actor, AuthConstants.Roles.Admin);
    }

    private async Task AddTokenAsync(Dictionary<string, object> records, object token, CancellationToken ct)
    {
        var id = await tokens.GetIdAsync(token, ct);
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Token identifier is missing.");
        records.TryAdd(id, token);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("MFA reset could not be persisted.");
    }
}
