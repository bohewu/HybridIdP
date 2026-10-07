using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Permissions = Core.Domain.Constants.Permissions;

namespace Infrastructure.Authorization;

/// <summary>Resource usage approval, independent of catalog visibility and OIDC/M2M classification.</summary>
public sealed class ApiScopeUsagePolicy(
    IApplicationDbContext db,
    IOpenIddictScopeManager scopes,
    IOpenIddictApplicationManager applications,
    IAdministrativeAuthorizationBoundary boundary,
    IAuthorizationService authorization) : IApiScopeUsagePolicy
{
    public const string ApprovalsProperty = "IdpApiScopeUsageApprovals";
    private static readonly HashSet<string> IdentityScopes = new(StringComparer.Ordinal)
    {
        "openid", "profile", "email", "phone", "address", "offline_access", "roles"
    };
    private static readonly HashSet<string> AdministrativeScopes = Permissions.GetAll().ToHashSet(StringComparer.Ordinal);

    // Stored only in the immutable application's server-controlled properties. No DTO accepts this property.
    public sealed record Approval(string ScopeId, string ScopeName, int? ResourceId, Guid? OwnerPersonId,
        Guid ApprovedByUserId, DateTime ApprovedAtUtc, string[] UnknownResources);

    public async Task<ApiUsageActor> GetActorAsync(CancellationToken cancellationToken = default)
    {
        var authority = await boundary.ResolveAsync();
        if (authority?.Principal.Identity?.IsAuthenticated != true)
            return new(null, null, false, false);
        if (authority.IsBearer) return new(null, null, false, true);
        var subject = authority.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                      authority.Principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (!Guid.TryParse(subject, out var userId)) return new(null, null, false, false);
        var user = await db.Users.Include(u => u.Person).AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null || !user.IsActive || user.IsDeleted ||
            (user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow) ||
            (user.PersonId.HasValue && user.Person?.CanAuthenticate() != true))
            return new(null, null, false, false);
        return new(user.Id, user.PersonId,
            AuthorizationRoleClaimResolver.IsInIdpRole(authority.Principal, AuthConstants.Roles.Admin), false);
    }

    private async Task RequirePermissionAsync(string permission)
    {
        var authority = await boundary.ResolveAsync();
        if (authority == null || (authority.IsBearer
                ? !authority.Permissions.Contains(permission)
                : !(await authorization.AuthorizeAsync(authority.Principal, null, permission)).Succeeded))
            throw new UnauthorizedAccessException("API scope usage approval is required.");
    }

    public async Task RequireResourceAuthorityAsync(ApiResource resource, string permission, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(permission);
        var actor = await GetActorAsync(cancellationToken);
        if (!actor.IsAdmin && (!actor.PersonId.HasValue || resource.OwnerPersonId != actor.PersonId))
            throw new UnauthorizedAccessException("Only the resource owner or an IdP administrator can change its usage policy.");
    }

    public async Task RequireScopeMappingAuthorityAsync(IEnumerable<string> scopeIds, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        foreach (var id in scopeIds.Distinct(StringComparer.Ordinal))
        {
            var scope = await scopes.FindByIdAsync(id, cancellationToken);
            if (scope == null) throw new InvalidOperationException("An associated scope does not exist.");
            if (!actor.IsAdmin && (!actor.PersonId.HasValue ||
                !await db.ScopeOwnerships.AnyAsync(s => s.ScopeId == id && s.CreatedByPersonId == actor.PersonId, cancellationToken)))
                throw new UnauthorizedAccessException("Only the scope owner or an IdP administrator can associate this scope.");
        }
    }

    private sealed record ScopePolicy(string Id, string Name, List<ApiResource> Resources, Guid? ScopeOwner, string[] UnknownResources, bool IsPublic);

    private async Task<ScopePolicy?> FindScopeAsync(string name, CancellationToken cancellationToken)
    {
        var scope = await scopes.FindByNameAsync(name, cancellationToken);
        if (scope == null) return IdentityScopes.Contains(name) || AdministrativeScopes.Contains(name)
            ? new("", name, [], null, [], IdentityScopes.Contains(name)) : null;
        var id = await scopes.GetIdAsync(scope, cancellationToken);
        if (string.IsNullOrEmpty(id)) return null;
        var names = (await scopes.GetResourcesAsync(scope, cancellationToken))
            .Where(n => n != AuthConstants.Resources.ResourceServer).ToArray();
        var resources = await db.ApiResources.AsNoTracking()
            .Where(r => names.Contains(r.Name) || r.Scopes.Any(s => s.ScopeId == id)).ToListAsync(cancellationToken);
        var owner = await db.ScopeOwnerships.Where(s => s.ScopeId == id)
            .Select(s => (Guid?)s.CreatedByPersonId).SingleOrDefaultAsync(cancellationToken);
        var isPublic = await db.ScopeExtensions.AnyAsync(s => s.ScopeId == id && s.IsPublic, cancellationToken);
        return new(id, name, resources, owner, names.Except(resources.Select(r => r.Name), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), isPublic);
    }

    private static List<Approval> ReadApprovals(IEnumerable<KeyValuePair<string, JsonElement>> properties)
    {
        var value = properties.FirstOrDefault(p => p.Key == ApprovalsProperty).Value;
        if (value.ValueKind != JsonValueKind.Array) return [];
        try { return value.Deserialize<List<Approval>>()?.Where(a => a != null && a.ApprovedByUserId != Guid.Empty && a.ApprovedAtUtc != default).ToList() ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool Approved(List<Approval> approvals, ScopePolicy scope, ApiResource? resource) =>
        approvals.Any(a => a.ScopeId == scope.Id && a.ScopeName == scope.Name && a.ResourceId == resource?.Id &&
            a.OwnerPersonId == (resource == null ? scope.ScopeOwner : resource.OwnerPersonId) &&
            (resource != null || (a.UnknownResources ?? []).SequenceEqual(scope.UnknownResources, StringComparer.Ordinal)));

    private static bool CanApprove(ApiUsageActor actor, Guid? owner) =>
        actor.UserId.HasValue && (actor.IsAdmin || (owner.HasValue && actor.PersonId == owner));

    private static void AddApproval(List<Approval> approvals, ScopePolicy scope, ApiResource? resource, ApiUsageActor actor)
    {
        approvals.RemoveAll(a => a.ScopeId == scope.Id && a.ResourceId == resource?.Id);
        approvals.Add(new(scope.Id, scope.Name, resource?.Id, resource == null ? scope.ScopeOwner : resource.OwnerPersonId,
            actor.UserId!.Value, DateTime.UtcNow, scope.UnknownResources));
    }

    public async Task PrepareClientScopesAsync(OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        var approvals = ReadApprovals(descriptor.Properties);
        var adminCeiling = AdministrativeClientGrant.ReadPermissions(descriptor.Properties.ToDictionary(p => p.Key, p => p.Value));
        var isM2M = descriptor.Permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        foreach (var name in descriptor.Permissions.Where(p => p.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.Ordinal))
                     .Select(p => p[OpenIddictConstants.Permissions.Prefixes.Scope.Length..]))
        {
            if (AdministrativeScopes.Contains(name))
            {
                if (!adminCeiling.Contains(name)) throw new UnauthorizedAccessException("This administrative scope has no server-provisioned approval.");
            }
            var policy = await FindScopeAsync(name, cancellationToken);
            if (policy == null || (isM2M && (policy.IsPublic || IdentityScopes.Contains(name))))
                throw new UnauthorizedAccessException("This scope is unavailable for the client.");
            if (policy.Resources.Count == 0 || policy.UnknownResources.Length > 0)
            {
                if (!(policy.UnknownResources.Length == 0 && (IdentityScopes.Contains(name) || AdministrativeScopes.Contains(name))) && !Approved(approvals, policy, null))
                {
                    if (!CanApprove(actor, policy.ScopeOwner) || (policy.UnknownResources.Length > 0 && !actor.IsAdmin))
                        throw new UnauthorizedAccessException("API scope usage approval is required.");
                    await RequirePermissionAsync(Permissions.Scopes.Update);
                    AddApproval(approvals, policy, null, actor);
                }
            }
            foreach (var resource in policy.Resources)
            {
                if (resource.IsUsageOpen || Approved(approvals, policy, resource)) continue;
                if (!CanApprove(actor, resource.OwnerPersonId)) throw new UnauthorizedAccessException("API scope usage approval is required.");
                await RequirePermissionAsync(Permissions.ApiResources.Update);
                AddApproval(approvals, policy, resource, actor);
            }
        }
        // No receipt is written until every requested scope/resource has passed preflight.
        if (approvals.Count > 0) descriptor.Properties[ApprovalsProperty] = JsonSerializer.SerializeToElement(approvals);
    }

    public async Task<bool> CanUseScopesAsync(object application, IEnumerable<string> requestedScopes, CancellationToken cancellationToken = default)
    {
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        var approvals = ReadApprovals(properties);
        var adminCeiling = AdministrativeClientGrant.ReadPermissions(properties);
        var isM2M = permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        foreach (var name in requestedScopes.Distinct(StringComparer.Ordinal))
        {
            if (!permissions.Contains(OpenIddictConstants.Permissions.Prefixes.Scope + name, StringComparer.Ordinal)) return false;
            if (AdministrativeScopes.Contains(name))
            {
                if (!adminCeiling.Contains(name)) return false;
            }
            var policy = await FindScopeAsync(name, cancellationToken);
            if (policy == null || (isM2M && (policy.IsPublic || IdentityScopes.Contains(name)))) return false;
            if (((policy.Resources.Count == 0 && !IdentityScopes.Contains(name) && !AdministrativeScopes.Contains(name)) || policy.UnknownResources.Length > 0) && !Approved(approvals, policy, null)) return false;
            if (policy.Resources.Any(r => !r.IsUsageOpen && !Approved(approvals, policy, r))) return false;
        }
        return true;
    }

    public async Task ApproveAsync(Guid applicationId, string scopeId, int? resourceId, CancellationToken cancellationToken = default)
    {
        var scope = await scopes.FindByIdAsync(scopeId, cancellationToken) ?? throw new KeyNotFoundException("Scope not found.");
        var name = await scopes.GetNameAsync(scope, cancellationToken) ?? throw new InvalidOperationException("Scope name missing.");
        var policy = await FindScopeAsync(name, cancellationToken) ?? throw new InvalidOperationException("Scope not found.");
        var actor = await GetActorAsync(cancellationToken);
        if (AdministrativeScopes.Contains(name) && !actor.IsAdmin) throw new UnauthorizedAccessException();
        ApiResource? resource = null;
        if (resourceId.HasValue)
        {
            resource = policy.Resources.SingleOrDefault(r => r.Id == resourceId) ?? throw new InvalidOperationException("Scope is not associated with this resource.");
            await RequireResourceAuthorityAsync(resource, Permissions.ApiResources.Update, cancellationToken);
        }
        else
        {
            if ((policy.Resources.Count != 0 && policy.UnknownResources.Length == 0) ||
                (policy.UnknownResources.Length > 0 && !actor.IsAdmin) || !CanApprove(actor, policy.ScopeOwner)) throw new UnauthorizedAccessException();
            await RequirePermissionAsync(Permissions.Scopes.Update);
        }
        if (!CanApprove(actor, resource == null ? policy.ScopeOwner : resource.OwnerPersonId)) throw new UnauthorizedAccessException();
        var application = await applications.FindByIdAsync(applicationId.ToString(), cancellationToken) ?? throw new KeyNotFoundException("Client not found.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);
        // Delegated owners cannot modify server-provisioned administrative clients.
        if (!actor.IsAdmin && AdministrativeClientGrant.ReadPermissions(descriptor.Properties.ToDictionary(p => p.Key, p => p.Value)).Count > 0)
            throw new UnauthorizedAccessException();
        var approvals = ReadApprovals(descriptor.Properties);
        AddApproval(approvals, policy, resource, actor);
        descriptor.Properties[ApprovalsProperty] = JsonSerializer.SerializeToElement(approvals);
        await applications.UpdateAsync(application, descriptor, cancellationToken);
    }

    public async Task<bool> CanViewScopeAsync(string scopeId, CancellationToken cancellationToken = default)
    {
        var scope = await scopes.FindByIdAsync(scopeId, cancellationToken);
        if (scope == null) return false;
        var name = await scopes.GetNameAsync(scope, cancellationToken);
        if (name == null) return false;
        var policy = await FindScopeAsync(name, cancellationToken);
        if (policy == null) return false;
        var actor = await GetActorAsync(cancellationToken);
        if (actor.IsAdmin || (actor.PersonId.HasValue && actor.PersonId == policy.ScopeOwner)) return true;
        if (policy.UnknownResources.Length > 0) return false;
        if (policy.Resources.Count == 0) return IdentityScopes.Contains(name) || (actor.PersonId.HasValue && actor.PersonId == policy.ScopeOwner);
        return policy.Resources.All(r => r.IsCatalogVisible || (actor.PersonId.HasValue && actor.PersonId == r.OwnerPersonId));
    }
}
