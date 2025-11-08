using Core.Application.DTOs;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Web.IdP.Api;

/// <summary>
/// Scope-to-claims mapping endpoints.
/// Routes preserved: api/admin/scopes/{scopeId}/claims
/// Note: These endpoints should eventually be integrated into a full ScopesController.
/// </summary>
[ApiController]
[Route("api/admin/scopes")]
[Authorize]
public class ScopeClaimsController : ControllerBase
{
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly ApplicationDbContext _context;

    public ScopeClaimsController(
        IOpenIddictScopeManager scopeManager,
        ApplicationDbContext context)
    {
        _scopeManager = scopeManager;
        _context = context;
    }

    /// <summary>
    /// Get all claims associated with a specific scope.
    /// </summary>
    [HasPermission(Permissions.Scopes.Read)]
    [HttpGet("{scopeId}/claims")]
    public async Task<ActionResult> GetScopeClaims(string scopeId)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{scopeId}' not found." });
        }

        var scopeName = await _scopeManager.GetNameAsync(scope);

        // Get all claims associated with this scope
        var scopeClaims = await _context.Set<ScopeClaim>()
            .Include(sc => sc.UserClaim)
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

        return Ok(new
        {
            scopeId,
            scopeName,
            claims = scopeClaims
        });
    }

    /// <summary>
    /// Update the claims associated with a specific scope.
    /// </summary>
    [HasPermission(Permissions.Scopes.Update)]
    [HttpPut("{scopeId}/claims")]
    public async Task<ActionResult> UpdateScopeClaims(string scopeId, [FromBody] UpdateScopeClaimsRequest request)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{scopeId}' not found." });
        }

        var scopeName = await _scopeManager.GetNameAsync(scope);

        // Remove existing scope claims
        var existingScopeClaims = await _context.Set<ScopeClaim>()
            .Where(sc => sc.ScopeId == scopeId)
            .ToListAsync();

        _context.Set<ScopeClaim>().RemoveRange(existingScopeClaims);

        // Add new scope claims
        if (request.ClaimIds != null && request.ClaimIds.Any())
        {
            foreach (var claimId in request.ClaimIds)
            {
                // Verify claim exists
                var claim = await _context.Set<UserClaim>()
                    .FirstOrDefaultAsync(c => c.Id == claimId);

                if (claim == null)
                {
                    return BadRequest(new { message = $"Claim with ID {claimId} not found." });
                }

                var scopeClaim = new ScopeClaim
                {
                    ScopeId = scopeId,
                    ScopeName = scopeName ?? "",
                    UserClaimId = claimId,
                    AlwaysInclude = claim.IsRequired // Always include required claims
                };

                _context.Set<ScopeClaim>().Add(scopeClaim);
            }
        }

        await _context.SaveChangesAsync();

        // Return updated claims
        var updatedClaims = await _context.Set<ScopeClaim>()
            .Include(sc => sc.UserClaim)
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

        return Ok(new
        {
            scopeId,
            scopeName,
            claims = updatedClaims,
            message = "Scope claims updated successfully."
        });
    }
}
