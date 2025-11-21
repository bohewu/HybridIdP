using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Service for managing user accounts (CRUD operations, roles, etc.)
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Get a paginated list of users with optional filtering and sorting
    /// </summary>
    Task<PagedUsersDto> GetUsersAsync(
        int skip = 0,
        int take = 25,
        string? search = null,
        string? role = null,
        bool? isActive = null,
        string? sortBy = "email",
        string? sortDirection = "asc");

    /// <summary>
    /// Get detailed information about a specific user
    /// </summary>
    Task<UserDetailDto?> GetUserByIdAsync(Guid userId);

    /// <summary>
    /// Create a new user account
    /// </summary>
    Task<(bool Success, Guid? UserId, IEnumerable<string> Errors)> CreateUserAsync(
        CreateUserDto createDto,
        Guid? createdBy = null);

    /// <summary>
    /// Update an existing user account
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> UpdateUserAsync(
        Guid userId,
        UpdateUserDto updateDto,
        Guid? modifiedBy = null);

    /// <summary>
    /// Soft delete a user (set IsActive = false)
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> DeactivateUserAsync(
        Guid userId,
        Guid? modifiedBy = null);

    /// <summary>
    /// Reactivate a deactivated user
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> ReactivateUserAsync(
        Guid userId,
        Guid? modifiedBy = null);

    /// <summary>
    /// Update user's last login timestamp
    /// </summary>
    Task UpdateLastLoginAsync(Guid userId);

    /// <summary>
    /// Assign roles to a user (replaces existing roles)
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> AssignRolesAsync(
        Guid userId,
        IEnumerable<string> roles);

    /// <summary>
    /// Assign roles to a user by role IDs (replaces existing roles)
    /// </summary>
    Task<(bool Success, IEnumerable<string> Errors)> AssignRolesByIdAsync(
        Guid userId,
        IEnumerable<Guid> roleIds);
}
