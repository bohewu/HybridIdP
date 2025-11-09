using System;

namespace Core.Domain.Entities
{
    public class SecurityPolicy
    {
        public Guid Id { get; set; }
        public int MinPasswordLength { get; set; } = 6;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireDigit { get; set; } = true;
        public bool RequireNonAlphanumeric { get; set; } = true;
        public int PasswordHistoryCount { get; set; } = 0; // 0 means no history check
        public int PasswordExpirationDays { get; set; } = 0; // 0 means no expiration
        public DateTime UpdatedUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }
}