namespace Core.Domain.Constants;

/// <summary>
/// Central list of audit event type strings to avoid magic values.
/// </summary>
public static class AuditEventTypes
{
    public const string RefreshTokenRotated = "RefreshTokenRotated";
    public const string RefreshTokenReuseDetected = "RefreshTokenReuseDetected";
    public const string SessionRevoked = "SessionRevoked";
    public const string SlidingExpirationExtended = "SlidingExpirationExtended";
}