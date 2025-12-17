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
        public int MinCharacterTypes { get; set; } = 3;
        public bool RequireNonAlphanumeric { get; set; } = true;
        public int PasswordHistoryCount { get; set; } = 5; // 0 means no history check
        public int PasswordExpirationDays { get; set; } = 180; // 0 means no expiration
        public int MinPasswordAgeDays { get; set; } = 1; // 0 means no minimum age
        public int MaxFailedAccessAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 15;
        public int AbnormalLoginHistoryCount { get; set; } = 10; // Number of recent logins to check for abnormalities
        public bool BlockAbnormalLogin { get; set; } = false; // Whether to block login if abnormal
        
        /// <summary>
        /// Whether users are allowed to change their own passwords via profile page.
        /// When false, only admins can reset passwords.
        /// </summary>
        public bool AllowSelfPasswordChange { get; set; } = true;
        
        /// <summary>
        /// Whether TOTP (Authenticator App) MFA is available for users to enable
        /// </summary>
        public bool EnableTotpMfa { get; set; } = true;
        
        /// <summary>
        /// Whether Email OTP MFA is available for users to enable
        /// </summary>
        public bool EnableEmailMfa { get; set; } = true;
        
        /// <summary>
        /// Whether Passkey (WebAuthn) authentication is available for users
        /// </summary>
        public bool EnablePasskey { get; set; } = true;
        
        /// <summary>
        /// Maximum number of passkeys a user can register (default: 10)
        /// </summary>
        public int MaxPasskeysPerUser { get; set; } = 10;
        
        public DateTime UpdatedUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }
}