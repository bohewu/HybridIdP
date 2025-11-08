using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Web.IdP.Api;

/// <summary>
/// Claims management endpoints split from AdminController.
/// Routes preserved: api/admin/claims/*
/// </summary>
[ApiController]
[Route("api/admin/claims")]
[Authorize]
public class ClaimsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ClaimsController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all user claim definitions with filtering, sorting, and pagination.
    /// </summary>
    [HasPermission(Permissions.Claims.Read)]
    [HttpGet]
    public async Task<ActionResult> GetClaims(
        [FromQuery] string? search = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDirection = "asc")
    {
        var query = _context.Set<Core.Domain.Entities.UserClaim>()
            .Include(c => c.ScopeClaims)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(searchLower) ||
                c.DisplayName.ToLower().Contains(searchLower) ||
                (c.Description != null && c.Description.ToLower().Contains(searchLower)) ||
                c.ClaimType.ToLower().Contains(searchLower));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = sortBy.ToLower() switch
        {
            "displayname" => sortDirection.ToLower() == "desc"
                ? query.OrderByDescending(c => c.DisplayName)
                : query.OrderBy(c => c.DisplayName),
            "claimtype" => sortDirection.ToLower() == "desc"
                ? query.OrderByDescending(c => c.ClaimType)
                : query.OrderBy(c => c.ClaimType),
            "type" => sortDirection.ToLower() == "desc"
                ? query.OrderByDescending(c => c.IsStandard)
                : query.OrderBy(c => c.IsStandard),
            _ => sortDirection.ToLower() == "desc"
                ? query.OrderByDescending(c => c.Name)
                : query.OrderBy(c => c.Name)
        };

        // Apply pagination
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

        return Ok(new
        {
            items = claims,
            totalCount = totalCount,
            skip = skip,
            take = take
        });
    }

    /// <summary>
    /// Get a specific user claim definition by ID.
    /// </summary>
    [HasPermission(Permissions.Claims.Read)]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDefinitionDto>> Get(int id)
    {
        var claim = await _context.Set<Core.Domain.Entities.UserClaim>()
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

        if (claim == null)
        {
            return NotFound(new { message = $"Claim with ID {id} not found." });
        }

        return Ok(claim);
    }

    /// <summary>
    /// Create a new user claim definition.
    /// </summary>
    [HasPermission(Permissions.Claims.Create)]
    [HttpPost]
    public async Task<ActionResult<ClaimDefinitionDto>> Create([FromBody] CreateClaimRequest request)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ClaimType))
        {
            return BadRequest(new { message = "Name and ClaimType are required." });
        }

        // Check if claim name already exists
        var existingClaim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .FirstOrDefaultAsync(c => c.Name == request.Name);

        if (existingClaim != null)
        {
            return BadRequest(new { message = $"A claim with name '{request.Name}' already exists." });
        }

        // Create new claim
        var claim = new Core.Domain.Entities.UserClaim
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

        _context.Set<Core.Domain.Entities.UserClaim>().Add(claim);
        await _context.SaveChangesAsync();

        var dto = new ClaimDefinitionDto
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

        return CreatedAtAction(nameof(Get), new { id = claim.Id }, dto);
    }

    /// <summary>
    /// Update an existing user claim definition.
    /// </summary>
    [HasPermission(Permissions.Claims.Update)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClaimDefinitionDto>> Update(int id, [FromBody] UpdateClaimRequest request)
    {
        var claim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .Include(c => c.ScopeClaims)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim == null)
        {
            return NotFound(new { message = $"Claim with ID {id} not found." });
        }

        // Prevent modification of standard claims' core properties
        if (claim.IsStandard)
        {
            return BadRequest(new { message = "Cannot modify standard OIDC claims. Only DisplayName and Description can be updated." });
        }

        // Update properties
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            claim.DisplayName = request.DisplayName;
        
        if (request.Description != null)
            claim.Description = request.Description;

        if (!claim.IsStandard)
        {
            if (!string.IsNullOrWhiteSpace(request.ClaimType))
                claim.ClaimType = request.ClaimType;
            
            if (!string.IsNullOrWhiteSpace(request.UserPropertyPath))
                claim.UserPropertyPath = request.UserPropertyPath;
            
            if (!string.IsNullOrWhiteSpace(request.DataType))
                claim.DataType = request.DataType;
            
            if (request.IsRequired.HasValue)
                claim.IsRequired = request.IsRequired.Value;
        }

        await _context.SaveChangesAsync();

        return Ok(new ClaimDefinitionDto
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
        });
    }

    /// <summary>
    /// Delete a user claim definition.
    /// </summary>
    [HasPermission(Permissions.Claims.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var claim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .Include(c => c.ScopeClaims)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim == null)
        {
            return NotFound(new { message = $"Claim with ID {id} not found." });
        }

        // Prevent deletion of standard claims
        if (claim.IsStandard)
        {
            return BadRequest(new { message = "Cannot delete standard OIDC claims." });
        }

        // Check if claim is used by any scopes
        if (claim.ScopeClaims.Any())
        {
            return BadRequest(new { message = $"Cannot delete claim '{claim.Name}' because it is used by {claim.ScopeClaims.Count} scope(s)." });
        }

        _context.Set<Core.Domain.Entities.UserClaim>().Remove(claim);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Claim deleted successfully." });
    }
}
