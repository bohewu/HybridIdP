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
    }
}