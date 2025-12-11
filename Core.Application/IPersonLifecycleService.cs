using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;

namespace Core.Application;

/// <summary>
/// Service interface for managing Person lifecycle operations.
/// Phase 18: Personnel Lifecycle Management
/// </summary>
public interface IPersonLifecycleService
{
    /// <summary>
    /// Terminate a person's employment (immediately or scheduled).
    /// When effectiveDate is null or in the past, terminates immediately.
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="effectiveDate">The date when termination takes effect (null = immediate)</param>
    /// <param name="terminatedBy">User ID who is performing the termination</param>
    /// <param name="revokeTokens">Whether to revoke all OAuth tokens for linked users</param>
    /// <returns>True if successful, false if person not found</returns>
    Task<bool> TerminatePersonAsync(Guid personId, DateTime? effectiveDate, Guid terminatedBy, bool revokeTokens = true);

    /// <summary>
    /// Activate a person (set status to Active).
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="startDate">Employment start date (null = now)</param>
    /// <param name="activatedBy">User ID who is performing the activation</param>
    /// <returns>True if successful, false if person not found</returns>
    Task<bool> ActivatePersonAsync(Guid personId, DateTime? startDate, Guid activatedBy);

    /// <summary>
    /// Suspend a person's access temporarily.
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="suspendedBy">User ID who is performing the suspension</param>
    /// <param name="revokeTokens">Whether to revoke all OAuth tokens for linked users</param>
    /// <returns>True if successful, false if person not found</returns>
    Task<bool> SuspendPersonAsync(Guid personId, Guid suspendedBy, bool revokeTokens = true);

    /// <summary>
    /// Change a person's status to any valid status.
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="newStatus">The new status to set</param>
    /// <param name="changedBy">User ID who is performing the change</param>
    /// <returns>True if successful, false if person not found</returns>
    Task<bool> ChangeStatusAsync(Guid personId, PersonStatus newStatus, Guid changedBy);

    /// <summary>
    /// Revoke all OAuth tokens (access and refresh) for all users linked to a person.
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <returns>Number of tokens revoked</returns>
    Task<int> RevokeAllTokensForPersonAsync(Guid personId);

    /// <summary>
    /// Soft delete a person.
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="deletedBy">User ID who is performing the deletion</param>
    /// <param name="revokeTokens">Whether to revoke all OAuth tokens for linked users</param>
    /// <returns>True if successful, false if person not found</returns>
    Task<bool> SoftDeletePersonAsync(Guid personId, Guid deletedBy, bool revokeTokens = true);

    /// <summary>
    /// Process scheduled status transitions (called by background job).
    /// - Auto-activate: Pending persons with StartDate <= now
    /// - Auto-terminate: Active persons with EndDate < now
    /// </summary>
    /// <returns>Number of persons whose status was changed</returns>
    Task<int> ProcessScheduledTransitionsAsync();
}
