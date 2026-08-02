using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Infrastructure.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using System.Security.Claims;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Scopes CRUD endpoints split from AdminController.
/// Routes preserved: api/admin/scopes/*
/// Admin sees all scopes, ApplicationManager sees only their own scopes.
/// </summary>
[ApiController]
[Route("api/admin/scopes")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class ScopesController : ControllerBase
{
    private const string TrustedAdministrationAutomationClientId = "testclient-admin";

    private static readonly HashSet<string> StandardOidcScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Phone,
        OpenIddictConstants.Scopes.Address,
        OpenIddictConstants.Scopes.OfflineAccess
    };

    private readonly IScopeService _scopeService;
    private readonly PrivilegedTestAdminBootstrapOptions _privilegedTestAdminBootstrapOptions;
    private readonly IHostEnvironment _hostEnvironment;

    public ScopesController(
        IScopeService scopeService,
        IOptions<PrivilegedTestAdminBootstrapOptions> privilegedTestAdminBootstrapOptions,
        IHostEnvironment hostEnvironment)
    {
        _scopeService = scopeService;
        _privilegedTestAdminBootstrapOptions = privilegedTestAdminBootstrapOptions.Value;
        _hostEnvironment = hostEnvironment;
    }

    /// <summary>
    /// Get all OIDC scopes with filtering, sorting, and pagination.
    /// Admin sees all scopes, ApplicationManager sees only their own scopes.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> GetScopes(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        // Admin sees all scopes with full edit rights.
        // ApplicationManager sees all scopes, but IsReadOnly is set for scopes they don't own.
        // We no longer strictly filter effectively hiding other scopes, we just mark them read-only.
        Guid? ownerFilterId = null; // Do not filter out scopes
        Guid? viewerPersonId = IsAdmin() ? null : GetCurrentPersonId();
        
        var (items, totalCount) = await _scopeService.GetScopesAsync(skip, take, search, sort, ownerFilterId, viewerPersonId, cancellationToken);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific OIDC scope by ID.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> Get(string id, CancellationToken cancellationToken = default)
    {
        var scope = await _scopeService.GetScopeByIdAsync(id, cancellationToken);
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
    public async Task<ActionResult> Create([FromBody] CreateScopeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Scope name is required." });
        }

        try
        {
            var personId = GetCurrentPersonId();
            
            var result = await _scopeService.CreateScopeAsync(request, personId, cancellationToken);
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
    public async Task<ActionResult> Update(string id, [FromBody] UpdateScopeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorizationResult = await AuthorizeScopeMutationAsync(
                id,
                identifierIsName: false,
                NotFound(new { message = $"Scope with ID '{id}' not found or update failed." }),
                cancellationToken);
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var updated = await _scopeService.UpdateScopeAsync(id, request, cancellationToken);
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete an OIDC scope.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Scopes.Delete)]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        var authorizationResult = await AuthorizeScopeMutationAsync(
            id,
            identifierIsName: true,
            missingResult: null,
            cancellationToken);
        if (authorizationResult != null)
        {
            return authorizationResult;
        }

        var deleted = await _scopeService.DeleteScopeAsync(id, cancellationToken);
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
    public async Task<ActionResult> GetScopeClaims(string scopeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _scopeService.GetScopeClaimsAsync(scopeId, cancellationToken);
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
    public async Task<ActionResult> UpdateScopeClaims(string scopeId, [FromBody] UpdateScopeClaimsRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorizationResult = await AuthorizeScopeMutationAsync(
                scopeId,
                identifierIsName: false,
                NotFound(new { message = $"Scope with ID '{scopeId}' not found." }),
                cancellationToken);
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var result = await _scopeService.UpdateScopeClaimsAsync(scopeId, request, cancellationToken);
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    #region Helper Methods

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private Guid? GetCurrentPersonId()
    {
        var personIdClaim = User.FindFirst(AuthConstants.Claims.PersonId)?.Value;
        return Guid.TryParse(personIdClaim, out var personId) ? personId : null;
    }

    private bool IsAdmin()
    {
        return AuthorizationRoleClaimResolver.IsInIdpRole(
            User,
            AuthConstants.Roles.Admin);
    }

    private async Task<ActionResult?> AuthorizeScopeMutationAsync(
        string scopeIdentifier,
        bool identifierIsName,
        ActionResult? missingResult,
        CancellationToken cancellationToken)
    {
        if (IsAdmin())
        {
            return null;
        }

        var existingScope = identifierIsName
            ? await _scopeService.GetScopeByNameAsync(scopeIdentifier, cancellationToken)
            : await _scopeService.GetScopeByIdAsync(scopeIdentifier, cancellationToken);
        if (existingScope == null)
        {
            return missingResult;
        }

        if (IsStandardOidcScope(existingScope.Name))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "Standard scopes can only be updated by administrators." });
        }

        if (IsTrustedAdministrationAutomation())
        {
            return null;
        }

        var personId = GetCurrentPersonId();
        if (personId.HasValue &&
            await _scopeService.IsScopeOwnedByPersonAsync(
                existingScope.Id,
                personId.Value,
                cancellationToken))
        {
            return null;
        }

        return Forbid();
    }

    private bool IsTrustedAdministrationAutomation()
    {
        if (!PrivilegedTestAdminBootstrapPolicy.IsEnabled(
                _privilegedTestAdminBootstrapOptions.Enabled,
                _hostEnvironment.EnvironmentName))
        {
            return false;
        }

        var subject = User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        return string.Equals(
            subject,
            TrustedAdministrationAutomationClientId,
            StringComparison.Ordinal);
    }

    private static bool IsStandardOidcScope(string? scopeName)
    {
        return !string.IsNullOrWhiteSpace(scopeName) && StandardOidcScopes.Contains(scopeName);
    }

    #endregion
}
