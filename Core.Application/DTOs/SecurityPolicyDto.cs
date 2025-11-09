using System.ComponentModel.DataAnnotations;

namespace Core.Application.DTOs;

public class SecurityPolicyDto
{
    [Range(6, 128)]
    public int MinPasswordLength { get; set; }

    public bool RequireUppercase { get; set; }
    public bool RequireLowercase { get; set; }
    public bool RequireDigit { get; set; }
    public bool RequireNonAlphanumeric { get; set; }

    [Range(0, 24)]
    public int PasswordHistoryCount { get; set; }

    [Range(0, 365)]
    public int PasswordExpirationDays { get; set; }
    
    [Range(0, 365)]
    public int MinPasswordAgeDays { get; set; }

    [Range(3, 20)]
    public int MaxFailedAccessAttempts { get; set; }

    [Range(1, 1440)] // 1 minute to 24 hours
    public int LockoutDurationMinutes { get; set; }
}
