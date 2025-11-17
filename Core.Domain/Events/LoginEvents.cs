using Core.Domain.Events;

namespace Core.Domain.Events;

/// <summary>
/// Event raised when a login attempt occurs.
/// </summary>
public class LoginAttemptEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public bool IsSuccessful { get; }
    public string? FailureReason { get; }
    public string? IPAddress { get; }
    public string? UserAgent { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public LoginAttemptEvent(string userId, string userName, bool isSuccessful, string? failureReason = null, string? ipAddress = null, string? userAgent = null)
    {
        UserId = userId;
        UserName = userName;
        IsSuccessful = isSuccessful;
        FailureReason = failureReason;
        IPAddress = ipAddress;
        UserAgent = userAgent;
    }
}

/// <summary>
/// Event raised when a user logs out.
/// </summary>
public class LogoutEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public string? IPAddress { get; }
    public string? UserAgent { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public LogoutEvent(string userId, string userName, string? ipAddress = null, string? userAgent = null)
    {
        UserId = userId;
        UserName = userName;
        IPAddress = ipAddress;
        UserAgent = userAgent;
    }
}