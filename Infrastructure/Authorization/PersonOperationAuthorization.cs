using System.Security.Claims;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Infrastructure.Authorization;

/// <summary>Preflights the role and ownership effects of Person association and transfer operations.</summary>
public sealed class PersonOperationAuthorization(
    ApplicationDbContext context,
    IAdministrativeAuthorizationBoundary boundary,
    IAuthorizationService authorization,
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    IOptions<PrivilegedRoleProtectionOptions> roleOptions)
{
    public async Task RequireAssociationAsync(Guid? personId, ApplicationUser user,
        IReadOnlyCollection<string> relatedRoles, IReadOnlyCollection<string> addedRoles,
        CancellationToken cancellationToken)
    {
        var authority = await RequirePersonnelAuthorityAsync();
        // Association grants durable membership even before a Person has roles or assets.
        await RequirePermissionAsync(authority, Permissions.Users.Update);
        if (relatedRoles.Count > 0)
        {
            await RequirePermissionAsync(authority, Permissions.Roles.Update);
            if (PrivilegedRoleAssignmentPolicy.ContainsProtectedRole(relatedRoles, roleOptions.Value) &&
                roleOptions.Value.RequireOperatorMfaForPrivilegedRoleAssignment &&
                (authority.IsBearer || !PrivilegedRoleAssignmentPolicy.HasCompletedMfa(authority.Principal, roleOptions.Value)))
                throw new UnauthorizedAccessException();
        }
        if (!await PrivilegedRoleAssignmentPolicy.CanReceiveAsync(user, addedRoles, roleOptions.Value, context, cancellationToken))
            throw new UnauthorizedAccessException();
        if (personId.HasValue)
            await RequireAssetAuthorityAsync(authority, personId.Value, cancellationToken);
    }

    public async Task RequireTransferAsync(Guid fromPersonId, IReadOnlyCollection<Guid?> clientIds,
        IReadOnlyCollection<string> scopeIds, bool hasApiResources, CancellationToken cancellationToken)
    {
        var authority = await RequirePersonnelAuthorityAsync();
        await RequireAssetAuthorityAsync(authority, fromPersonId, clientIds, scopeIds, hasApiResources, cancellationToken);
    }

    private async Task<AdministrativeAuthority> RequirePersonnelAuthorityAsync()
    {
        var authority = await boundary.ResolveAsync();
        if (authority?.Principal.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException();
        await RequirePermissionAsync(authority, Permissions.Persons.Update);
        return authority;
    }

    private async Task RequirePermissionAsync(AdministrativeAuthority authority, string permission)
    {
        // Reuse the authenticated administrative boundary, including a bearer's live approval ceiling.
        if (authority.IsBearer)
        {
            if (!authority.Permissions.Contains(permission)) throw new UnauthorizedAccessException();
        }
        else if (!(await authorization.AuthorizeAsync(authority.Principal, null, permission)).Succeeded)
            throw new UnauthorizedAccessException();
    }

    private async Task RequireAssetAuthorityAsync(AdministrativeAuthority authority, Guid personId,
        CancellationToken cancellationToken)
    {
        var clientIds = await context.ClientOwnerships.Where(owner => owner.CreatedByPersonId == personId)
            .Select(owner => owner.ApplicationId).ToListAsync(cancellationToken);
        var scopeIds = await context.ScopeOwnerships.Where(owner => owner.CreatedByPersonId == personId)
            .Select(owner => owner.ScopeId).ToListAsync(cancellationToken);
        var hasApiResources = await context.ApiResources.AnyAsync(resource => resource.OwnerPersonId == personId, cancellationToken);
        await RequireAssetAuthorityAsync(authority, personId, clientIds, scopeIds, hasApiResources, cancellationToken);
    }

    private async Task RequireAssetAuthorityAsync(AdministrativeAuthority authority, Guid personId,
        IReadOnlyCollection<Guid?> clientIds, IReadOnlyCollection<string> scopeIds, bool hasApiResources,
        CancellationToken cancellationToken)
    {
        if (clientIds.Count == 0 && scopeIds.Count == 0 && !hasApiResources) return;

        if (clientIds.Count > 0) await RequirePermissionAsync(authority, Permissions.Clients.Update);
        if (scopeIds.Count > 0) await RequirePermissionAsync(authority, Permissions.Scopes.Update);
        if (hasApiResources) await RequirePermissionAsync(authority, Permissions.ApiResources.Update);

        if (!authority.IsBearer && AuthorizationRoleClaimResolver.IsInIdpRole(authority.Principal, AuthConstants.Roles.Admin))
            return;

        // Resolve ownership from the current account, not a caller-supplied Person claim or audit ID.
        var subject = authority.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                      authority.Principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (authority.IsBearer || !Guid.TryParse(subject, out var actorId) ||
            !await context.Users.AnyAsync(actor => actor.Id == actorId && actor.PersonId == personId, cancellationToken))
            throw new UnauthorizedAccessException();

        foreach (var clientId in clientIds)
        {
            // Unbound legacy ownership rows cannot establish delegated application authority.
            if (!clientId.HasValue) throw new UnauthorizedAccessException();
            var application = await applications.FindByIdAsync(clientId.Value.ToString(), cancellationToken);
            if (application == null || AdministrativeClientGrant.ReadPermissions(
                    await applications.GetPropertiesAsync(application, cancellationToken)).Count > 0)
                throw new UnauthorizedAccessException();
        }
        foreach (var scopeId in scopeIds)
        {
            var scope = await scopes.FindByIdAsync(scopeId, cancellationToken);
            var name = scope == null ? null : await scopes.GetNameAsync(scope, cancellationToken);
            if (name == null || IsStandardScope(name)) throw new UnauthorizedAccessException();
        }
    }

    private static bool IsStandardScope(string name) =>
        new[] { OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email, OpenIddictConstants.Scopes.Phone,
            OpenIddictConstants.Scopes.Address, OpenIddictConstants.Scopes.OfflineAccess }
        .Contains(name, StringComparer.OrdinalIgnoreCase);
}
