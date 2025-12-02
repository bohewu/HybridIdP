using Core.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface IAuditService
    {
        /// <summary>
        /// Logs a new audit event.
        /// </summary>
        /// <param name="eventType">Type of the event</param>
        /// <param name="userId">User ID (null for system events)</param>
        /// <param name="details">Event details (JSON serialized)</param>
        /// <param name="ipAddress">Client IP address</param>
        /// <param name="userAgent">Client user agent</param>
        /// <returns>Task</returns>
        Task LogEventAsync(string eventType, string? userId, string? details, string? ipAddress, string? userAgent);

        /// <summary>
        /// Gets audit events with filtering and pagination.
        /// </summary>
        /// <param name="filter">Filter criteria</param>
        /// <returns>Tuple of items and total count</returns>
        Task<(IEnumerable<AuditEventDto> items, int totalCount)> GetEventsAsync(AuditEventFilterDto filter);

        /// <summary>
        /// Exports audit events for a specific event ID.
        /// </summary>
        /// <param name="eventId">The event ID to export</param>
        /// <returns>Export DTO with additional user info</returns>
        Task<AuditEventExportDto?> ExportEventAsync(int eventId);

        /// <summary>
        /// Logs a role switch event for audit.
        /// </summary>
        /// <param name="userId">User ID performing the switch</param>
        /// <param name="oldRoleId">Previous role ID</param>
        /// <param name="newRoleId">New role ID</param>
        /// <param name="sessionAuthorizationId">Session authorization ID</param>
        /// <param name="ipAddress">Client IP address</param>
        /// <param name="userAgent">Client user agent</param>
        /// <returns>Task</returns>
        Task LogRoleSwitchAsync(Guid userId, Guid oldRoleId, Guid newRoleId, string sessionAuthorizationId, string ipAddress, string userAgent);

        /// <summary>
        /// Logs an account switch event for audit.
        /// </summary>
        /// <param name="currentUserId">Current user ID</param>
        /// <param name="targetAccountId">Target account ID</param>
        /// <param name="reason">Reason for switching</param>
        /// <param name="ipAddress">Client IP address</param>
        /// <param name="userAgent">Client user agent</param>
        /// <returns>Task</returns>
        Task LogAccountSwitchAsync(Guid currentUserId, Guid targetAccountId, string reason, string ipAddress, string userAgent);
    }
}