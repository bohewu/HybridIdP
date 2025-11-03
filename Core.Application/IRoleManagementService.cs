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
