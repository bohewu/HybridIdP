using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services;

public class ScopeService : IScopeService
{
    /// <summary>
    /// Standard OIDC scopes with locked standard claims.
    /// Custom claims may be added, but standard claim mappings remain fixed.
    /// </summary>
    private static readonly HashSet<string> StandardOidcScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Phone,
        OpenIddictConstants.Scopes.Address,
        OpenIddictConstants.Scopes.OfflineAccess
    };

    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IApplicationDbContext _db;
    private readonly IDomainEventPublisher _eventPublisher;

    public ScopeService(IOpenIddictScopeManager scopeManager, IOpenIddictApplicationManager applicationManager, IApplicationDbContext db, IDomainEventPublisher eventPublisher)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _db = db;
        _eventPublisher = eventPublisher;
    }

    public async Task<(IEnumerable<ScopeSummary> items, int totalCount)> GetScopesAsync(int skip, int take, string? search, string? sort, Guid? ownerFilterId = null, Guid? viewerPersonId = null, CancellationToken cancellationToken = default)
    {
        var scopes = new List<ScopeSummary>();
        var scopeExtensions = await _db.ScopeExtensions.ToDictionaryAsync(se => se.ScopeId, cancellationToken);
        
        // Get owned scope IDs if filtering by owner
        HashSet<string>? ownedScopeIds = null;
        if (ownerFilterId.HasValue)
        {
            ownedScopeIds = (await _db.ScopeOwnerships
                .Where(so => so.CreatedByPersonId == ownerFilterId.Value)
                .Select(so => so.ScopeId)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }
        
        // Get IDs of scopes owned by the viewer (if applicable) for IsReadOnly calculation
        HashSet<string> viewerOwnedScopeIds = new();
        if (viewerPersonId.HasValue)
        {
            viewerOwnedScopeIds = (await _db.ScopeOwnerships
                .Where(so => so.CreatedByPersonId == viewerPersonId.Value)
                .Select(so => so.ScopeId)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        await foreach (var scope in _scopeManager.ListAsync().WithCancellation(cancellationToken))
        {
            var id = await _scopeManager.GetIdAsync(scope);
            
            // Skip if filtering by owner and this scope is not owned by the filter target
            if (ownedScopeIds != null && !ownedScopeIds.Contains(id!))
            {
                continue;
            }
            
            var name = await _scopeManager.GetNameAsync(scope);
            var displayName = await _scopeManager.GetDisplayNameAsync(scope);
            var description = await _scopeManager.GetDescriptionAsync(scope);
            var resources = await _scopeManager.GetResourcesAsync(scope);
            scopeExtensions.TryGetValue(id!, out var extension);
            
            // Calculate ReadOnly status
            // If viewer is provided: ReadOnly if NOT owned by viewer
            // (Note: StandardOidcScopes checks are handled in Update/Delete actions, but UI can also use IsReadOnly)
            // If viewerPersonId is null (e.g. Admin or System), we assume full access (IsReadOnly=false) relative to ownership.
            bool isReadOnly = false;
            if (viewerPersonId.HasValue)
            {
                 // If viewer is specified, they can only edit what they own.
                 isReadOnly = !viewerOwnedScopeIds.Contains(id!);
            }

            scopes.Add(new ScopeSummary
            {
                Id = id!,
                Name = name!,
                DisplayName = displayName,
                Description = description,
                Resources = resources.ToList(),
                ConsentDisplayNameKey = extension?.ConsentDisplayNameKey,
                ConsentDescriptionKey = extension?.ConsentDescriptionKey,
                IconUrl = extension?.IconUrl,
                IsRequired = extension?.IsRequired ?? false,
                DisplayOrder = extension?.DisplayOrder ?? 0,
                Category = extension?.Category,
                IsPublic = extension?.IsPublic ?? false,
                IsReadOnly = isReadOnly
            });
        }
        
        // Filtering
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            scopes = scopes.Where(x =>
                (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.DisplayName) && x.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        
        // Sorting
        string sortField = "name";
        bool sortAsc = true;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var parts = sort.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0) sortField = parts[0].ToLowerInvariant();
            if (parts.Length > 1) sortAsc = !string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase);
        }
        
        Func<ScopeSummary, object?> keySelector = sortField switch
        {
            "displayname" => x => x.DisplayName,
            "description" => x => x.Description,
            _ => x => x.Name
        };
        
        scopes = (sortAsc ? scopes.OrderBy(keySelector) : scopes.OrderByDescending(keySelector)).ToList();
        var totalCount = scopes.Count;
        
        // Paging safety
        if (skip < 0) skip = 0;
        if (take <= 0) take = 25;
        var items = scopes.Skip(skip).Take(take).ToList();
        
        return (items, totalCount);
    }

    public async Task<ScopeSummary?> GetScopeByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var scope = await _scopeManager.FindByIdAsync(id, cancellationToken);
        if (scope == null) return null;
        
        var resources = await _scopeManager.GetResourcesAsync(scope, cancellationToken);
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id, cancellationToken);
        
        return new ScopeSummary
        {
#pragma warning disable CS8601
            Id = await _scopeManager.GetIdAsync(scope, cancellationToken),
            Name = await _scopeManager.GetNameAsync(scope, cancellationToken),
            DisplayName = await _scopeManager.GetDisplayNameAsync(scope, cancellationToken),
            Description = await _scopeManager.GetDescriptionAsync(scope, cancellationToken),
            Resources = resources.ToList(),
            ConsentDisplayNameKey = extension?.ConsentDisplayNameKey,
            ConsentDescriptionKey = extension?.ConsentDescriptionKey,
            IconUrl = extension?.IconUrl,
            IsRequired = extension?.IsRequired ?? false,
            DisplayOrder = extension?.DisplayOrder ?? 0,
            Category = extension?.Category,
            IsPublic = extension?.IsPublic ?? false
#pragma warning restore CS8601
        };
    }

    public async Task<ScopeSummary> CreateScopeAsync(CreateScopeRequest request, Guid? creatorPersonId = null, CancellationToken cancellationToken = default)
    {
        // Check if scope already exists
        var existing = await _scopeManager.FindByNameAsync(request.Name, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"Scope '{request.Name}' already exists.");
        }
        
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description
        };
        
        if (request.Resources != null)
        {
            foreach (var resource in request.Resources)
            {
                descriptor.Resources.Add(resource);
            }
        }
        else
        {
            descriptor.Resources.Add(AuthConstants.Resources.ResourceServer);
        }
        
        var scope = await _scopeManager.CreateAsync(descriptor, cancellationToken);
        var id = await _scopeManager.GetIdAsync(scope, cancellationToken);
        
        // Create ScopeExtension for consent customization if any fields are provided
        if (!string.IsNullOrWhiteSpace(request.ConsentDisplayNameKey) ||
            !string.IsNullOrWhiteSpace(request.ConsentDescriptionKey) ||
            !string.IsNullOrWhiteSpace(request.IconUrl) ||
            request.IsRequired ||
            request.DisplayOrder != 0 ||
            !string.IsNullOrWhiteSpace(request.Category) ||
            request.IsPublic)
        {
            var extension = new ScopeExtension
            {
                ScopeId = id!,
                ConsentDisplayNameKey = request.ConsentDisplayNameKey,
                ConsentDescriptionKey = request.ConsentDescriptionKey,
                IconUrl = request.IconUrl,
                IsRequired = request.IsRequired,
                DisplayOrder = request.DisplayOrder,
                Category = request.Category,
                IsPublic = request.IsPublic
            };
            _db.ScopeExtensions.Add(extension);
            await _db.SaveChangesAsync(cancellationToken);
        }
        
        // Create ownership record if creator info is provided
        if (creatorPersonId.HasValue)
        {
            var ownership = new ScopeOwnership
            {
                Id = Guid.NewGuid(),
                ScopeId = id!,
                CreatedByPersonId = creatorPersonId.Value,
                CreatedAt = DateTime.UtcNow
            };
            _db.ScopeOwnerships.Add(ownership);
            await _db.SaveChangesAsync(cancellationToken);
        }
        
        var summary = new ScopeSummary
        {
            Id = id!,
            Name = request.Name,
            DisplayName = descriptor.DisplayName,
            Description = descriptor.Description,
            Resources = descriptor.Resources.ToList(),
            ConsentDisplayNameKey = request.ConsentDisplayNameKey,
            ConsentDescriptionKey = request.ConsentDescriptionKey,
            IconUrl = request.IconUrl,
            IsRequired = request.IsRequired,
            DisplayOrder = request.DisplayOrder,
            Category = request.Category,
            IsPublic = request.IsPublic
        };

        await _eventPublisher.PublishAsync(new ScopeCreatedEvent(id!, request.Name));

        return summary;
    }

    public async Task<bool> UpdateScopeAsync(string id, UpdateScopeRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await _scopeManager.FindByIdAsync(id, cancellationToken);
        if (scope == null) return false;

        var scopeName = await _scopeManager.GetNameAsync(scope, cancellationToken);
        
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name ?? scopeName,
            DisplayName = request.DisplayName ?? await _scopeManager.GetDisplayNameAsync(scope, cancellationToken),
            Description = request.Description ?? await _scopeManager.GetDescriptionAsync(scope, cancellationToken)
        };
        
        var existingResources = await _scopeManager.GetResourcesAsync(scope, cancellationToken);
        var resources = request.Resources ?? existingResources.ToList();
        foreach (var resource in resources)
        {
            descriptor.Resources.Add(resource);
        }
        
        await _scopeManager.PopulateAsync(scope, descriptor, cancellationToken);
        await _scopeManager.UpdateAsync(scope, cancellationToken);
        
        // Update or create ScopeExtension
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id, cancellationToken);
        
        if (extension == null)
        {
            if (!string.IsNullOrWhiteSpace(request.ConsentDisplayNameKey) ||
                !string.IsNullOrWhiteSpace(request.ConsentDescriptionKey) ||
                !string.IsNullOrWhiteSpace(request.IconUrl) ||
                request.IsRequired == true ||
                request.DisplayOrder != null ||
                !string.IsNullOrWhiteSpace(request.Category) ||
                request.IsPublic == true)
            {
                extension = new ScopeExtension
                {
                    ScopeId = id!,
                    ConsentDisplayNameKey = request.ConsentDisplayNameKey,
                    ConsentDescriptionKey = request.ConsentDescriptionKey,
                    IconUrl = request.IconUrl,
                    IsRequired = request.IsRequired ?? false,
                    DisplayOrder = request.DisplayOrder ?? 0,
                    Category = request.Category,
                    IsPublic = request.IsPublic ?? false
                };
                _db.ScopeExtensions.Add(extension);
            }
        }
        else
        {
            if (request.ConsentDisplayNameKey != null)
                extension.ConsentDisplayNameKey = request.ConsentDisplayNameKey;
            if (request.ConsentDescriptionKey != null)
                extension.ConsentDescriptionKey = request.ConsentDescriptionKey;
            if (request.IconUrl != null)
                extension.IconUrl = request.IconUrl;
            if (request.IsRequired.HasValue)
                extension.IsRequired = request.IsRequired.Value;
            if (request.DisplayOrder.HasValue)
                extension.DisplayOrder = request.DisplayOrder.Value;
            // Category: allow setting to null/empty to clear it
            extension.Category = request.Category;
            if (request.IsPublic.HasValue)
                extension.IsPublic = request.IsPublic.Value;
        }
        
        await _db.SaveChangesAsync(cancellationToken);

        await _eventPublisher.PublishAsync(new ScopeUpdatedEvent(id, await _scopeManager.GetNameAsync(scope, cancellationToken) ?? "", "Scope updated"));

        return true;
    }

    public async Task<bool> DeleteScopeAsync(string id, CancellationToken cancellationToken = default)
    {
        // Note: id is actually the scope name, not a GUID
        var scope = await _scopeManager.FindByNameAsync(id, cancellationToken);
        if (scope == null) return false;
        
        // Check if scope is in use by any clients
        var clientsCount = 0;
        await foreach (var app in _applicationManager.ListAsync().WithCancellation(cancellationToken))
        {
            var permissions = await _applicationManager.GetPermissionsAsync(app, cancellationToken);
            if (permissions.Any(p => p == $"{OpenIddictConstants.Permissions.Prefixes.Scope}{id}"))
            {
                clientsCount++;
                break;
            }
        }
        
        if (clientsCount > 0) return false;
        
        try
        {
            var scopeId = await _scopeManager.GetIdAsync(scope, cancellationToken);
            var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == scopeId, cancellationToken);
            if (extension != null)
            {
                _db.ScopeExtensions.Remove(extension);
                await _db.SaveChangesAsync(cancellationToken);
            }
            
            await _scopeManager.DeleteAsync(scope, cancellationToken);

            await _eventPublisher.PublishAsync(new ScopeDeletedEvent(scopeId!, id));

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string scopeId, string scopeName, IEnumerable<ScopeClaimDto> claims)> GetScopeClaimsAsync(string scopeId, CancellationToken cancellationToken = default)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId, cancellationToken);
        if (scope == null)
        {
            throw new KeyNotFoundException($"Scope with ID '{scopeId}' not found.");
        }

        var scopeName = await _scopeManager.GetNameAsync(scope, cancellationToken);

        // Get all claims associated with this scope
        var scopeClaims = await _db.ScopeClaims
            .Where(sc => sc.ScopeId == scopeId)
            .Select(sc => new ScopeClaimDto
            {
                Id = sc.Id,
                ScopeId = sc.ScopeId,
                ScopeName = sc.ScopeName,
                ClaimId = sc.ClaimDefinitionId,
                ClaimName = sc.ClaimDefinition!.Name,
                ClaimDisplayName = sc.ClaimDefinition.DisplayName,
                ClaimType = sc.ClaimDefinition.ClaimType,
                AlwaysInclude = sc.AlwaysInclude,
                CustomMappingLogic = sc.CustomMappingLogic
            })
            .ToListAsync(cancellationToken);

        return (scopeId, scopeName ?? "", scopeClaims);
    }

    public async Task<(string scopeId, string scopeName, IEnumerable<ScopeClaimDto> claims)> UpdateScopeClaimsAsync(string scopeId, UpdateScopeClaimsRequest request, CancellationToken cancellationToken = default)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId, cancellationToken);
        if (scope == null)
        {
            throw new KeyNotFoundException($"Scope with ID '{scopeId}' not found.");
        }

        var scopeName = await _scopeManager.GetNameAsync(scope, cancellationToken);

        var requestedClaimIds = (request.ClaimIds ?? new List<int>())
            .Distinct()
            .ToList();

        // Remove existing scope claims
        var existingScopeClaims = await _db.ScopeClaims
            .Where(sc => sc.ScopeId == scopeId)
            .Include(sc => sc.ClaimDefinition)
            .ToListAsync(cancellationToken);

        var claimsById = requestedClaimIds.Count == 0
            ? new Dictionary<int, ClaimDefinition>()
            : await _db.ClaimDefinitions
                .Where(c => requestedClaimIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        // Verify all requested claims exist
        var missingClaimId = requestedClaimIds.FirstOrDefault(id => !claimsById.ContainsKey(id));
        if (missingClaimId != 0)
        {
            throw new ArgumentException($"Claim with ID {missingClaimId} not found.");
        }

        // Standard OIDC scopes keep their current standard claims locked, but allow custom claims.
        if (StandardOidcScopes.Contains(scopeName!))
        {
            var lockedStandardClaimIds = existingScopeClaims
                .Where(sc => sc.ClaimDefinition?.IsStandard == true)
                .Select(sc => sc.ClaimDefinitionId)
                .Distinct()
                .ToHashSet();

            var removedLockedClaimIds = lockedStandardClaimIds
                .Except(requestedClaimIds)
                .ToList();

            if (removedLockedClaimIds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot remove standard OIDC claims from scope '{scopeName}'. " +
                    "Only custom claims can be added or removed.");
            }

            var disallowedStandardClaimAdds = claimsById.Values
                .Where(c => c.IsStandard && !lockedStandardClaimIds.Contains(c.Id))
                .Select(c => c.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (disallowedStandardClaimAdds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot add standard OIDC claims to scope '{scopeName}'. " +
                    "Only the existing standard claims are allowed. " +
                    $"Disallowed claims: {string.Join(", ", disallowedStandardClaimAdds)}.");
            }
        }

        _db.ScopeClaims.RemoveRange(existingScopeClaims);

        // Add new scope claims
        if (requestedClaimIds.Count > 0)
        {
            foreach (var claimId in requestedClaimIds)
            {
                var claim = claimsById[claimId];

                var scopeClaim = new ScopeClaim
                {
                    ScopeId = scopeId,
                    ScopeName = scopeName ?? "",
                    ClaimDefinitionId = claimId,
                    AlwaysInclude = claim.IsRequired // Always include required claims
                };

                _db.ScopeClaims.Add(scopeClaim);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Return updated claims
        var updatedClaims = await _db.ScopeClaims
            .Where(sc => sc.ScopeId == scopeId)
            .Select(sc => new ScopeClaimDto
            {
                Id = sc.Id,
                ScopeId = sc.ScopeId,
                ScopeName = sc.ScopeName,
                ClaimId = sc.ClaimDefinitionId,
                ClaimName = sc.ClaimDefinition!.Name,
                ClaimDisplayName = sc.ClaimDefinition.DisplayName,
                ClaimType = sc.ClaimDefinition.ClaimType,
                AlwaysInclude = sc.AlwaysInclude,
                CustomMappingLogic = sc.CustomMappingLogic
            })
            .ToListAsync(cancellationToken);

        await _eventPublisher.PublishAsync(new ScopeClaimChangedEvent(scopeId, scopeName ?? "", "Scope claims updated"));

        return (scopeId, scopeName ?? "", updatedClaims);
    }

    public ScopeClassificationResult ClassifyScopes(IEnumerable<string> requestedScopes, IEnumerable<ScopeSummary> availableScopes, IEnumerable<string>? grantedScopes)
    {
        var requestedScopesList = requestedScopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList()
                                  ?? new List<string>();
        var requestedSet = requestedScopesList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requiredSet = availableScopes
            .Where(s => s.IsRequired && requestedSet.Contains(s.Name))
            .Select(s => s.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<string> allowedSet;
        var grantedScopesList = grantedScopes?.ToList();

        if (grantedScopesList == null || grantedScopesList.Count == 0)
        {
            // Only required scopes are allowed when nothing explicitly granted
            allowedSet = requiredSet.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var grantedSet = grantedScopesList
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Start with granted scopes that were actually requested
            allowedSet = requestedSet.Where(grantedSet.Contains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Always include required ones
            foreach (var req in requiredSet)
            {
                allowedSet.Add(req);
            }
        }

        // Final allowed limited to requested scopes only (already enforced above)
        var rejectedSet = requestedSet.Where(r => !allowedSet.Contains(r)).ToList();

        // Preserve original requested order for deterministic output
        var allowedOrdered = requestedScopesList.Where(s => allowedSet.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var requiredOrdered = requestedScopesList.Where(s => requiredSet.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rejectedOrdered = requestedScopesList.Where(s => rejectedSet.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new ScopeClassificationResult
        {
            Allowed = allowedOrdered,
            Required = requiredOrdered,
            Rejected = rejectedOrdered,
            IsPartialGrant = rejectedOrdered.Count > 0
        };
    }

    public async Task<bool> IsScopeOwnedByPersonAsync(string scopeId, Guid personId, CancellationToken cancellationToken = default)
    {
        return await _db.ScopeOwnerships
            .AnyAsync(so => so.ScopeId == scopeId && so.CreatedByPersonId == personId, cancellationToken);
    }
}
