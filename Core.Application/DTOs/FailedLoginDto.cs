using System;

namespace Core.Application.DTOs;

/// <summary>
/// DTO for failed login attempts.
/// </summary>
public sealed class FailedLoginDto
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserEmail { get; set; }
    public DateTime LoginTime { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int RiskScore { get; set; }
}