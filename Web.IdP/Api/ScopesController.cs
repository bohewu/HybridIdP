using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api;

/// <summary>
/// Scopes CRUD endpoints split from AdminController.
/// Routes preserved: api/admin/scopes/*
/// </summary>
[ApiController]
[Route("api/admin/scopes")]
[Authorize]
public class ScopesController : ControllerBase
{
    private readonly IScopeService _scopeService;

    public ScopesController(IScopeService scopeService)
    {
        _scopeService = scopeService;
    }

    /// <summary>
    /// Get all OIDC scopes with filtering, sorting, and pagination.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> GetScopes(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null)
    {
        var (items, totalCount) = await _scopeService.GetScopesAsync(skip, take, search, sort);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific OIDC scope by ID.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> Get(string id)
    {
        var scope = await _scopeService.GetScopeByIdAsync(id);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{id}' not found." });
        }
        return Ok(scope);
    }

    /// <summary>
    /// Create a new OIDC scope.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Scopes.Create)]
    public async Task<ActionResult> Create([FromBody] CreateScopeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Scope name is required." });
        }

        try
        {
            var result = await _scopeService.CreateScopeAsync(request);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, new
            {
                id = result.Id,
                name = result.Name,
                displayName = result.DisplayName,
                message = "Scope created successfully."
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing OIDC scope.
    /// </summary>
    [HttpPut("{id}")]
    [HasPermission(Permissions.Scopes.Update)]
    public async Task<ActionResult> Update(string id, [FromBody] UpdateScopeRequest request)
    {
        var updated = await _scopeService.UpdateScopeAsync(id, request);
        if (!updated)
        {
            return NotFound(new { message = $"Scope with ID '{id}' not found or update failed." });
        }
        return Ok(new
        {
            id,
            message = "Scope updated successfully."
        });
    }

    /// <summary>
    /// Delete an OIDC scope.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Scopes.Delete)]
    public async Task<ActionResult> Delete(string id)
    {
        var deleted = await _scopeService.DeleteScopeAsync(id);
        if (!deleted)
        {
            return BadRequest(new { message = "Cannot delete this scope because it is currently in use or not found." });
        }
        return Ok(new { message = "Scope deleted successfully." });
    }

    /// <summary>
    /// Get all claims associated with a specific scope.
    /// </summary>
    [HttpGet("{scopeId}/claims")]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> GetScopeClaims(string scopeId)
    {
        try
        {
            var result = await _scopeService.GetScopeClaimsAsync(scopeId);
            return Ok(new
            {
                scopeId = result.scopeId,
                scopeName = result.scopeName,
                claims = result.claims
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update the claims associated with a specific scope.
    /// </summary>
    [HttpPut("{scopeId}/claims")]
    [HasPermission(Permissions.Scopes.Update)]
    public async Task<ActionResult> UpdateScopeClaims(string scopeId, [FromBody] UpdateScopeClaimsRequest request)
    {
        try
        {
            var result = await _scopeService.UpdateScopeClaimsAsync(scopeId, request);
            return Ok(new
            {
                scopeId = result.scopeId,
                scopeName = result.scopeName,
                claims = result.claims,
                message = "Scope claims updated successfully."
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
