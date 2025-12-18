namespace Core.Domain.Constants;

/// <summary>
/// Central list of audit event type strings to avoid magic values.
/// Categories are organized for Admin UI audit viewer.
/// </summary>
public static class AuditEventTypes
{
    // Session/Token events
    public const string RefreshTokenRotated = "RefreshTokenRotated";
    public const string RefreshTokenReuseDetected = "RefreshTokenReuseDetected";
    public const string SessionRevoked = "SessionRevoked";
    public const string SlidingExpirationExtended = "SlidingExpirationExtended";
    
    // Passkey events
    public const string PasskeyRegistered = "PasskeyRegistered";
    public const string PasskeyDeleted = "PasskeyDeleted";
    public const string PasskeyAuthenticationUsed = "PasskeyAuthenticationUsed";
    
    // MFA events
    public const string MfaEnabled = "MfaEnabled";
    public const string MfaDisabled = "MfaDisabled";
    public const string EmailMfaEnabled = "EmailMfaEnabled";
    public const string EmailMfaDisabled = "EmailMfaDisabled";
    
    // Rate limiting events
    public const string RateLimitExceeded = "RateLimitExceeded";
    
    // Login events
    public const string LoginFailed = "LoginFailed";
    public const string LoginSucceeded = "LoginSucceeded";
    
    // Admin operations
    public const string AdminUserCreated = "AdminUserCreated";
    public const string AdminUserUpdated = "AdminUserUpdated";
    public const string AdminUserDeleted = "AdminUserDeleted";
    public const string AdminRoleChanged = "AdminRoleChanged";
    public const string SecurityPolicyUpdated = "SecurityPolicyUpdated";
}

