namespace Core.Domain.Entities;

/// <summary>
/// Represents an audit event for tracking system activities and user actions.
/// Used for compliance with cybersecurity regulations and monitoring.
/// </summary>
public class AuditEvent
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Type of the audit event (e.g., "UserLogin", "ClientCreated", "PermissionChanged")
    /// </summary>
    public required string EventType { get; set; }

    /// <summary>
    /// ID of the user who performed the action (null for system events)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Timestamp when the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Detailed information about the event (JSON serialized)
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// IP address of the client that triggered the event
    /// </summary>
    public string? IPAddress { get; set; }

    /// <summary>
    /// User agent string from the client's request
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Timestamp when the audit event was created (same as Timestamp for immutability)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}