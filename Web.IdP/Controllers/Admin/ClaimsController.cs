using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Claims management endpoints (thin controller pattern).
/// All business logic is in IClaimsService.
/// Routes preserved: api/admin/claims/*
/// </summary>
[ApiController]
[Route("api/admin/claims")]
[ApiAuthorize]
public class ClaimsController : ControllerBase
{
    private readonly IClaimsService _claimsService;

    public ClaimsController(IClaimsService claimsService)
    {
        _claimsService = claimsService;
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
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip, take, search, sortBy, sortDirection);

        return Ok(new
        {
            items,
            totalCount,
            skip,
            take
        });
    }

    /// <summary>
    /// Get a specific user claim definition by ID.
    /// </summary>
    [HasPermission(Permissions.Claims.Read)]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDefinitionDto>> Get(int id)
    {
        var claim = await _claimsService.GetClaimByIdAsync(id);

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
        try
        {
            var dto = await _claimsService.CreateClaimAsync(request);
            return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing user claim definition.
    /// </summary>
    [HasPermission(Permissions.Claims.Update)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClaimDefinitionDto>> Update(int id, [FromBody] UpdateClaimRequest request)
    {
        try
        {
            var dto = await _claimsService.UpdateClaimAsync(id, request);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a user claim definition.
    /// </summary>
    [HasPermission(Permissions.Claims.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            await _claimsService.DeleteClaimAsync(id);
            return Ok(new { message = "Claim deleted successfully." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
