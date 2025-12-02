using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Service for managing user account and role switching
/// Phase 11.2: Self-Service Identity Management
/// </summary>
public interface IAccountManagementService
{
    /// <summary>
    /// Get all accounts linked to the current user's Person entity
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <returns>List of linked accounts with roles</returns>
    Task<IEnumerable<LinkedAccountDto>> GetMyLinkedAccountsAsync(Guid userId);

    /// <summary>
    /// Get all roles available for the current user
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <returns>List of available roles with active status</returns>
    Task<IEnumerable<AvailableRoleDto>> GetMyAvailableRolesAsync(Guid userId);

    /// <summary>
    /// Switch the active role for the current session
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <param name="sessionAuthorizationId">Current session authorization ID</param>
    /// <param name="roleId">Target role ID to switch to</param>
    /// <param name="password">Password (required for Admin role elevation)</param>
    /// <returns>True if successful</returns>
    Task<bool> SwitchRoleAsync(Guid userId, string sessionAuthorizationId, Guid roleId, string? password = null);

    /// <summary>
    /// Switch to another account linked to the same Person
    /// Revokes current session and creates new session with target account
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <param name="targetAccountId">Target account ID to switch to</param>
    /// <param name="reason">Reason for account switch (audit trail)</param>
    /// <returns>True if successful</returns>
    Task<bool> SwitchToAccountAsync(Guid currentUserId, Guid targetAccountId, string reason);
}
