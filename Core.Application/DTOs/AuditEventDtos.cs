namespace Core.Application.DTOs;

/// <summary>
/// DTO for audit event details.
/// </summary>
public sealed class AuditEventDto
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// DTO for filtering audit events.
/// </summary>
public sealed class AuditEventFilterDto
{
    public string? EventType { get; set; }
    public string? UserId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? IPAddress { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// DTO for exporting audit events.
/// </summary>
public sealed class AuditEventExportDto
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Username { get; set; } // For display purposes
}