using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public class ClientAllowedScopesService : IClientAllowedScopesService
{
    private readonly IOpenIddictApplicationManager _applicationManager;

    public ClientAllowedScopesService(IOpenIddictApplicationManager applicationManager)
    {
        _applicationManager = applicationManager;
    }

    public async Task<IReadOnlyList<string>> GetAllowedScopesAsync(Guid clientId)
    {
        var application = await _applicationManager.FindByIdAsync(clientId.ToString());
        if (application == null)
        {
            return Array.Empty<string>();
        }

        var permissions = await _applicationManager.GetPermissionsAsync(application);
        var scopePrefix = OpenIddictConstants.Permissions.Prefixes.Scope;
        
        var scopes = permissions
            .Where(p => p.StartsWith(scopePrefix))
            .Select(p => p.Substring(scopePrefix.Length))
            .ToList();

        return scopes.AsReadOnly();
    }

    public async Task SetAllowedScopesAsync(Guid clientId, IEnumerable<string> scopes)
    {
        var application = await _applicationManager.FindByIdAsync(clientId.ToString());
        if (application == null)
        {
            throw new InvalidOperationException($"Client with ID '{clientId}' not found.");
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);

        // Get existing permissions that are not scope permissions
        var scopePrefix = OpenIddictConstants.Permissions.Prefixes.Scope;
        var nonScopePermissions = descriptor.Permissions
            .Where(p => !p.StartsWith(scopePrefix))
            .ToList();

        // Clear all permissions and re-add non-scope permissions
        descriptor.Permissions.Clear();
        foreach (var permission in nonScopePermissions)
        {
            descriptor.Permissions.Add(permission);
        }

        // Add new scope permissions
        foreach (var scope in scopes)
        {
            descriptor.Permissions.Add($"{scopePrefix}{scope}");
        }

        await _applicationManager.UpdateAsync(application, descriptor);
    }

    public async Task<bool> IsScopeAllowedAsync(Guid clientId, string scope)
    {
        var allowedScopes = await GetAllowedScopesAsync(clientId);
        return allowedScopes.Contains(scope);
    }

    public async Task<IReadOnlyList<string>> ValidateRequestedScopesAsync(Guid clientId, IEnumerable<string> requestedScopes)
    {
        var allowedScopes = await GetAllowedScopesAsync(clientId);
        var validScopes = requestedScopes
            .Where(s => allowedScopes.Contains(s))
            .ToList();

        return validScopes.AsReadOnly();
    }
}
