using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Service for managing roles and permissions
/// </summary>
public interface IRoleManagementService
{
    /// <summary>
    /// Get all roles
    /// </summary>
    Task<List<RoleSummaryDto>> GetRolesAsync();
    
    /// <summary>
    /// Get roles with paging, optional search and sorting
    /// </summary>
    /// <param name="skip">Items to skip</param>
    /// <param name="take">Items to take</param>
    /// <param name="search">Optional search text (matches name/description)</param>
    /// <param name="sortBy">Sort field: name or createdat (default: name)</param>
    /// <param name="sortDirection">asc or desc (default: asc)</param>
    Task<PagedRolesDto> GetRolesAsync(int skip, int take, string? search = null, string? sortBy = "name", string? sortDirection = "asc");
    
    /// <summary>
    /// Get role by ID with detailed information
    /// </summary>
    Task<RoleDetailDto?> GetRoleByIdAsync(Guid roleId);
    
    /// <summary>
    /// Create a new role
    /// </summary>
    Task<(bool Success, Guid? RoleId, IEnumerable<string> Errors)> CreateRoleAsync(CreateRoleDto createDto);
    
    /// <summary>
    /// Update an existing role
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> UpdateRoleAsync(Guid roleId, UpdateRoleDto updateDto);
    
    /// <summary>
    /// Delete a role (cannot delete system roles or roles with users)
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> DeleteRoleAsync(Guid roleId);
    
    /// <summary>
    /// Get all available permissions
    /// </summary>
    Task<List<string>> GetAvailablePermissionsAsync();
}
