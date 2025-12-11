namespace Core.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a Person in the system.
/// Phase 18: Personnel Lifecycle Management
/// </summary>
public enum PersonStatus
{
    /// <summary>
    /// Person is pending activation (e.g., future start date, not yet onboarded)
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Person is active and can authenticate
    /// </summary>
    Active = 1,
    
    /// <summary>
    /// Person is temporarily suspended (e.g., leave of absence, investigation)
    /// </summary>
    Suspended = 2,
    
    /// <summary>
    /// Person has voluntarily resigned or retired
    /// </summary>
    Resigned = 3,
    
    /// <summary>
    /// Person has been terminated by the organization
    /// </summary>
    Terminated = 4
}
