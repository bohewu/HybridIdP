using System;

namespace Core.Domain.Entities
{
    public class SecurityPolicy
    {
        public Guid Id { get; set; }
        public int MinPasswordLength { get; set; } = 8;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireDigit { get; set; } = true;
        public bool RequireNonAlphanumeric { get; set; } = true;
        public int PasswordHistoryCount { get; set; } = 5; // 0 means no history check
        public int PasswordExpirationDays { get; set; } = 180; // 0 means no expiration
        public int MinPasswordAgeDays { get; set; } = 1; // 0 means no minimum age
        public int MaxFailedAccessAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 15;
        public int AbnormalLoginHistoryCount { get; set; } = 10; // Number of recent logins to check for abnormalities
        public bool BlockAbnormalLogin { get; set; } = false; // Whether to block login if abnormal
        public DateTime UpdatedUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }
}