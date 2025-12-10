using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public class ClientAllowedScopesService : IClientAllowedScopesService
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly ApplicationDbContext _db;

    public ClientAllowedScopesService(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        ApplicationDbContext db)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _db = db;
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

    public async Task<IReadOnlyList<string>> GetRequiredScopesAsync(Guid clientId)
    {
        var clientIdString = clientId.ToString();
        
        // Get client-specific required scope IDs from database
        var requiredScopeIds = await _db.ClientRequiredScopes
            .Where(crs => crs.ClientId == clientIdString)
            .Select(crs => crs.ScopeId)
            .ToListAsync();

        if (requiredScopeIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Convert scope IDs to scope names using OpenIddict scope manager
        var scopeNames = new List<string>();
        await foreach (var scope in _scopeManager.ListAsync())
        {
            var scopeId = await _scopeManager.GetIdAsync(scope);
            if (scopeId != null && requiredScopeIds.Contains(scopeId))
            {
                var scopeName = await _scopeManager.GetNameAsync(scope);
                if (scopeName != null)
                {
                    scopeNames.Add(scopeName);
                }
            }
        }

        return scopeNames.AsReadOnly();
    }

    public async Task SetRequiredScopesAsync(Guid clientId, IEnumerable<string> scopeNames)
    {
        var clientIdString = clientId.ToString();
        var scopeNameList = scopeNames.ToList();

        // Validate client exists
        var application = await _applicationManager.FindByIdAsync(clientIdString);
        if (application == null)
        {
            throw new InvalidOperationException($"Client with ID '{clientId}' not found.");
        }

        // Validate all required scopes are in allowed scopes
        var allowedScopes = await GetAllowedScopesAsync(clientId);
        var allowedScopeSet = allowedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalidScopes = scopeNameList.Where(s => !allowedScopeSet.Contains(s)).ToList();
        
        if (invalidScopes.Count > 0)
        {
            throw new InvalidOperationException(
                $"The following required scopes are not in the client's allowed scopes: {string.Join(", ", invalidScopes)}");
        }

        // Convert scope names to scope IDs
        var scopeIds = new List<string>();
        foreach (var scopeName in scopeNameList)
        {
            var scope = await _scopeManager.FindByNameAsync(scopeName);
            if (scope == null)
            {
                throw new InvalidOperationException($"Scope '{scopeName}' not found.");
            }
            var scopeId = await _scopeManager.GetIdAsync(scope);
            if (scopeId != null)
            {
                scopeIds.Add(scopeId);
            }
        }

        // Remove existing required scopes for this client
        var existingRequiredScopes = await _db.ClientRequiredScopes
            .Where(crs => crs.ClientId == clientIdString)
            .ToListAsync();
        _db.ClientRequiredScopes.RemoveRange(existingRequiredScopes);

        // Add new required scopes
        foreach (var scopeId in scopeIds)
        {
            _db.ClientRequiredScopes.Add(new ClientRequiredScope
            {
                ClientId = clientIdString,
                ScopeId = scopeId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsScopeRequiredAsync(Guid clientId, string scopeName)
    {
        var requiredScopes = await GetRequiredScopesAsync(clientId);
        return requiredScopes.Contains(scopeName, StringComparer.OrdinalIgnoreCase);
    }
}
