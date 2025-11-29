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

    public async Task<(IEnumerable<ScopeSummary> items, int totalCount)> GetScopesAsync(int skip, int take, string? search, string? sort)
    {
        var scopes = new List<ScopeSummary>();
        var scopeExtensions = await _db.ScopeExtensions.ToDictionaryAsync(se => se.ScopeId);
        
        await foreach (var scope in _scopeManager.ListAsync())
        {
            var id = await _scopeManager.GetIdAsync(scope);
            var name = await _scopeManager.GetNameAsync(scope);
            var displayName = await _scopeManager.GetDisplayNameAsync(scope);
            var description = await _scopeManager.GetDescriptionAsync(scope);
            var resources = await _scopeManager.GetResourcesAsync(scope);
            scopeExtensions.TryGetValue(id!, out var extension);
            
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
                Category = extension?.Category
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

    public async Task<ScopeSummary?> GetScopeByIdAsync(string id)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null) return null;
        
        var resources = await _scopeManager.GetResourcesAsync(scope);
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id);
        
        return new ScopeSummary
        {
#pragma warning disable CS8601
            Id = await _scopeManager.GetIdAsync(scope),
            Name = await _scopeManager.GetNameAsync(scope),
            DisplayName = await _scopeManager.GetDisplayNameAsync(scope),
            Description = await _scopeManager.GetDescriptionAsync(scope),
            Resources = resources.ToList(),
            ConsentDisplayNameKey = extension?.ConsentDisplayNameKey,
            ConsentDescriptionKey = extension?.ConsentDescriptionKey,
            IconUrl = extension?.IconUrl,
            IsRequired = extension?.IsRequired ?? false,
            DisplayOrder = extension?.DisplayOrder ?? 0,
            Category = extension?.Category
#pragma warning restore CS8601
        };
    }

    public async Task<ScopeSummary> CreateScopeAsync(CreateScopeRequest request)
    {
        // Check if scope already exists
        var existing = await _scopeManager.FindByNameAsync(request.Name);
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
        
        var scope = await _scopeManager.CreateAsync(descriptor);
        var id = await _scopeManager.GetIdAsync(scope);
        
        // Create ScopeExtension for consent customization if any fields are provided
        if (!string.IsNullOrWhiteSpace(request.ConsentDisplayNameKey) ||
            !string.IsNullOrWhiteSpace(request.ConsentDescriptionKey) ||
            !string.IsNullOrWhiteSpace(request.IconUrl) ||
            request.IsRequired ||
            request.DisplayOrder != 0 ||
            !string.IsNullOrWhiteSpace(request.Category))
        {
            var extension = new ScopeExtension
            {
                ScopeId = id!,
                ConsentDisplayNameKey = request.ConsentDisplayNameKey,
                ConsentDescriptionKey = request.ConsentDescriptionKey,
                IconUrl = request.IconUrl,
                IsRequired = request.IsRequired,
                DisplayOrder = request.DisplayOrder,
                Category = request.Category
            };
            _db.ScopeExtensions.Add(extension);
            await _db.SaveChangesAsync(CancellationToken.None);
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
            Category = request.Category
        };

        await _eventPublisher.PublishAsync(new ScopeCreatedEvent(id!, request.Name));

        return summary;
    }

    public async Task<bool> UpdateScopeAsync(string id, UpdateScopeRequest request)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null) return false;
        
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name ?? await _scopeManager.GetNameAsync(scope),
            DisplayName = request.DisplayName ?? await _scopeManager.GetDisplayNameAsync(scope),
            Description = request.Description ?? await _scopeManager.GetDescriptionAsync(scope)
        };
        
        var existingResources = await _scopeManager.GetResourcesAsync(scope);
        var resources = request.Resources ?? existingResources.ToList();
        foreach (var resource in resources)
        {
            descriptor.Resources.Add(resource);
        }
        
        await _scopeManager.PopulateAsync(scope, descriptor);
        await _scopeManager.UpdateAsync(scope);
        
        // Update or create ScopeExtension
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id);
        
        if (extension == null)
        {
            if (!string.IsNullOrWhiteSpace(request.ConsentDisplayNameKey) ||
                !string.IsNullOrWhiteSpace(request.ConsentDescriptionKey) ||
                !string.IsNullOrWhiteSpace(request.IconUrl) ||
                request.IsRequired == true ||
                request.DisplayOrder != null ||
                !string.IsNullOrWhiteSpace(request.Category))
            {
                extension = new ScopeExtension
                {
                    ScopeId = id!,
                    ConsentDisplayNameKey = request.ConsentDisplayNameKey,
                    ConsentDescriptionKey = request.ConsentDescriptionKey,
                    IconUrl = request.IconUrl,
                    IsRequired = request.IsRequired ?? false,
                    DisplayOrder = request.DisplayOrder ?? 0,
                    Category = request.Category
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
            if (request.Category != null)
                extension.Category = request.Category;
        }
        
        await _db.SaveChangesAsync(CancellationToken.None);

        await _eventPublisher.PublishAsync(new ScopeUpdatedEvent(id, await _scopeManager.GetNameAsync(scope) ?? "", "Scope updated"));

        return true;
    }

    public async Task<bool> DeleteScopeAsync(string id)
    {
        // Note: id is actually the scope name, not a GUID
        var scope = await _scopeManager.FindByNameAsync(id);
        if (scope == null) return false;
        
        // Check if scope is in use by any clients
        var clientsCount = 0;
        await foreach (var app in _applicationManager.ListAsync())
        {
            var permissions = await _applicationManager.GetPermissionsAsync(app);
            if (permissions.Any(p => p == $"{OpenIddictConstants.Permissions.Prefixes.Scope}{id}"))
            {
                clientsCount++;
                break;
            }
        }
        
        if (clientsCount > 0) return false;
        
        try
        {
            var scopeId = await _scopeManager.GetIdAsync(scope);
            var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == scopeId);
            if (extension != null)
            {
                _db.ScopeExtensions.Remove(extension);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            
            await _scopeManager.DeleteAsync(scope);

            await _eventPublisher.PublishAsync(new ScopeDeletedEvent(scopeId!, id));

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string scopeId, string scopeName, IEnumerable<ScopeClaimDto> claims)> GetScopeClaimsAsync(string scopeId)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId);
        if (scope == null)
        {
            throw new KeyNotFoundException($"Scope with ID '{scopeId}' not found.");
        }

        var scopeName = await _scopeManager.GetNameAsync(scope);

        // Get all claims associated with this scope
        var scopeClaims = await _db.ScopeClaims
            .Where(sc => sc.ScopeId == scopeId)
            .Select(sc => new ScopeClaimDto
            {
                Id = sc.Id,
                ScopeId = sc.ScopeId,
                ScopeName = sc.ScopeName,
                ClaimId = sc.UserClaimId,
                ClaimName = sc.UserClaim!.Name,
                ClaimDisplayName = sc.UserClaim.DisplayName,
                ClaimType = sc.UserClaim.ClaimType,
                AlwaysInclude = sc.AlwaysInclude,
                CustomMappingLogic = sc.CustomMappingLogic
            })
            .ToListAsync();

        return (scopeId, scopeName ?? "", scopeClaims);
    }

    public async Task<(string scopeId, string scopeName, IEnumerable<ScopeClaimDto> claims)> UpdateScopeClaimsAsync(string scopeId, UpdateScopeClaimsRequest request)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId);
        if (scope == null)
        {
            throw new KeyNotFoundException($"Scope with ID '{scopeId}' not found.");
        }

        var scopeName = await _scopeManager.GetNameAsync(scope);

        // Remove existing scope claims
        var existingScopeClaims = await _db.ScopeClaims
            .Where(sc => sc.ScopeId == scopeId)
            .ToListAsync();

        _db.ScopeClaims.RemoveRange(existingScopeClaims);

        // Add new scope claims
        if (request.ClaimIds != null && request.ClaimIds.Any())
        {
            foreach (var claimId in request.ClaimIds)
            {
                // Verify claim exists
                var claim = await _db.UserClaims
                    .FirstOrDefaultAsync(c => c.Id == claimId);

                if (claim == null)
                {
                    throw new ArgumentException($"Claim with ID {claimId} not found.");
                }

                var scopeClaim = new ScopeClaim
                {
                    ScopeId = scopeId,
                    ScopeName = scopeName ?? "",
                    UserClaimId = claimId,
                    AlwaysInclude = claim.IsRequired // Always include required claims
                };

                _db.ScopeClaims.Add(scopeClaim);
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None);

        // Return updated claims
        var updatedClaims = await _db.ScopeClaims
            .Where(sc => sc.ScopeId == scopeId)
            .Select(sc => new ScopeClaimDto
            {
                Id = sc.Id,
                ScopeId = sc.ScopeId,
                ScopeName = sc.ScopeName,
                ClaimId = sc.UserClaimId,
                ClaimName = sc.UserClaim!.Name,
                ClaimDisplayName = sc.UserClaim.DisplayName,
                ClaimType = sc.UserClaim.ClaimType,
                AlwaysInclude = sc.AlwaysInclude,
                CustomMappingLogic = sc.CustomMappingLogic
            })
            .ToListAsync();

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
        if (grantedScopes == null || !grantedScopes.Any())
        {
            // Only required scopes are allowed when nothing explicitly granted
            allowedSet = requiredSet.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var grantedSet = grantedScopes
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
}
