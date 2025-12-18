using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public partial class ClaimsService : IClaimsService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<ClaimsService>? _logger;

    public ClaimsService(IApplicationDbContext db, ILogger<ClaimsService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(IEnumerable<ClaimDefinitionDto> items, int totalCount)> GetClaimsAsync(
        int skip,
        int take,
        string? search,
        string sortBy,
        string sortDirection)
    {
        // TODO: Consider query optimization - eager loading .Include(c => c.ScopeClaims) may be inefficient for large datasets.
        // Current approach loads all ScopeClaims for count calculation. For better performance with large datasets, consider:
        // 1. Projection-only queries (Select without Include) to reduce data transfer
        // 2. Separate query for ScopeCount using GroupBy aggregation
        // 3. Deferred loading with explicit Load() only when ScopeCount is needed
        // 4. Database-side computed column or indexed view for frequently accessed counts
        var query = _db.UserClaims
            .Include(c => c.ScopeClaims)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                c.Name.Contains(search) ||
                c.DisplayName.Contains(search) ||
                (c.Description != null && c.Description.Contains(search)) ||
                c.ClaimType.Contains(search));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        var sortByLower = (sortBy ?? "name").ToLowerInvariant();
        var isDesc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortByLower switch
        {
            "displayname" => isDesc
                ? query.OrderByDescending(c => c.DisplayName)
                : query.OrderBy(c => c.DisplayName),
            "claimtype" => isDesc
                ? query.OrderByDescending(c => c.ClaimType)
                : query.OrderBy(c => c.ClaimType),
            "type" => isDesc
                ? query.OrderByDescending(c => c.IsStandard)
                : query.OrderBy(c => c.IsStandard),
            _ => isDesc
                ? query.OrderByDescending(c => c.Name)
                : query.OrderBy(c => c.Name)
        };

        // Apply pagination and project to DTO
        var claims = await query
            .Skip(skip)
            .Take(take)
            .Select(c => new ClaimDefinitionDto
            {
                Id = c.Id,
                Name = c.Name,
                DisplayName = c.DisplayName,
                Description = c.Description,
                ClaimType = c.ClaimType,
                UserPropertyPath = c.UserPropertyPath,
                DataType = c.DataType,
                IsStandard = c.IsStandard,
                IsRequired = c.IsRequired,
                ScopeCount = c.ScopeClaims.Count
            })
            .ToListAsync();

        return (claims, totalCount);
    }

    public async Task<ClaimDefinitionDto?> GetClaimByIdAsync(int id)
    {
        var claim = await _db.UserClaims
            .Include(c => c.ScopeClaims)
            .Where(c => c.Id == id)
            .Select(c => new ClaimDefinitionDto
            {
                Id = c.Id,
                Name = c.Name,
                DisplayName = c.DisplayName,
                Description = c.Description,
                ClaimType = c.ClaimType,
                UserPropertyPath = c.UserPropertyPath,
                DataType = c.DataType,
                IsStandard = c.IsStandard,
                IsRequired = c.IsRequired,
                ScopeCount = c.ScopeClaims.Count
            })
            .FirstOrDefaultAsync();

        return claim;
    }

    public async Task<ClaimDefinitionDto> CreateClaimAsync(CreateClaimRequest request)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required.", nameof(request.Name));
        }

        if (string.IsNullOrWhiteSpace(request.ClaimType))
        {
            throw new ArgumentException("ClaimType is required.", nameof(request.ClaimType));
        }

        // Check for duplicate name
        var existingClaim = await _db.UserClaims
            .FirstOrDefaultAsync(c => c.Name == request.Name);

        if (existingClaim != null)
        {
            throw new InvalidOperationException($"A claim with name '{request.Name}' already exists.");
        }

        // Create new claim with defaults
        var claim = new UserClaim
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description,
            ClaimType = request.ClaimType,
            UserPropertyPath = request.UserPropertyPath ?? request.Name,
            DataType = request.DataType ?? "String",
            IsStandard = false, // Custom claims are always non-standard
            IsRequired = request.IsRequired ?? false
        };

        _db.UserClaims.Add(claim);
        await _db.SaveChangesAsync(CancellationToken.None);

        if (_logger != null)
        {
            LogClaimCreated(_logger, claim.Name, claim.Id);
        }

        return new ClaimDefinitionDto
        {
            Id = claim.Id,
            Name = claim.Name,
            DisplayName = claim.DisplayName,
            Description = claim.Description,
            ClaimType = claim.ClaimType,
            UserPropertyPath = claim.UserPropertyPath,
            DataType = claim.DataType,
            IsStandard = claim.IsStandard,
            IsRequired = claim.IsRequired,
            ScopeCount = 0
        };
    }

    public async Task<ClaimDefinitionDto> UpdateClaimAsync(int id, UpdateClaimRequest request)
    {
        var claim = await _db.UserClaims
            .Include(c => c.ScopeClaims)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim == null)
        {
            throw new KeyNotFoundException($"Claim with ID {id} not found.");
        }

        // Standard claims protection: Only DisplayName and Description can be updated
        if (claim.IsStandard)
        {
            // Check if trying to update protected fields
            if (!string.IsNullOrWhiteSpace(request.ClaimType) ||
                !string.IsNullOrWhiteSpace(request.UserPropertyPath) ||
                !string.IsNullOrWhiteSpace(request.DataType) ||
                request.IsRequired.HasValue)
            {
                throw new InvalidOperationException(
                    "Cannot modify core properties (ClaimType, UserPropertyPath, DataType, IsRequired) of standard OIDC claims. " +
                    "Only DisplayName and Description can be updated.");
            }

            // Update only allowed fields for standard claims
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                claim.DisplayName = request.DisplayName;
            }

            if (request.Description != null)
            {
                claim.Description = request.Description;
            }
        }
        else
        {
            // Custom claims: All fields can be updated
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                claim.DisplayName = request.DisplayName;
            }

            if (request.Description != null)
            {
                claim.Description = request.Description;
            }

            if (!string.IsNullOrWhiteSpace(request.ClaimType))
            {
                claim.ClaimType = request.ClaimType;
            }

            if (!string.IsNullOrWhiteSpace(request.UserPropertyPath))
            {
                claim.UserPropertyPath = request.UserPropertyPath;
            }

            if (!string.IsNullOrWhiteSpace(request.DataType))
            {
                claim.DataType = request.DataType;
            }

            if (request.IsRequired.HasValue)
            {
                claim.IsRequired = request.IsRequired.Value;
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None);

        if (_logger != null)
        {
            LogClaimUpdated(_logger, claim.Name, claim.Id);
        }

        return new ClaimDefinitionDto
        {
            Id = claim.Id,
            Name = claim.Name,
            DisplayName = claim.DisplayName,
            Description = claim.Description,
            ClaimType = claim.ClaimType,
            UserPropertyPath = claim.UserPropertyPath,
            DataType = claim.DataType,
            IsStandard = claim.IsStandard,
            IsRequired = claim.IsRequired,
            ScopeCount = claim.ScopeClaims.Count
        };
    }

    public async Task DeleteClaimAsync(int id)
    {
        var claim = await _db.UserClaims
            .Include(c => c.ScopeClaims)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim == null)
        {
            throw new KeyNotFoundException($"Claim with ID {id} not found.");
        }

        // Prevent deletion of standard claims
        if (claim.IsStandard)
        {
            throw new InvalidOperationException("Cannot delete standard OIDC claims.");
        }

        // Check if claim is used by any scopes
        if (claim.ScopeClaims.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete claim '{claim.Name}' because it is used by {claim.ScopeClaims.Count} scope(s). " +
                "Remove the claim from all scopes before deleting.");
        }

        _db.UserClaims.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);

        if (_logger != null)
        {
            LogClaimDeleted(_logger, claim.Name, id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created custom claim '{ClaimName}' with ID {ClaimId}")]
    static partial void LogClaimCreated(ILogger logger, string claimName, int claimId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated claim '{ClaimName}' (ID: {ClaimId})")]
    static partial void LogClaimUpdated(ILogger logger, string claimName, int claimId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted claim '{ClaimName}' (ID: {ClaimId})")]
    static partial void LogClaimDeleted(ILogger logger, string claimName, int claimId);
}
