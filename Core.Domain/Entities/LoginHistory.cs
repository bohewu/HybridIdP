namespace Core.Domain.Entities;

/// <summary>
/// Represents a login event for a user, used for tracking and detecting abnormal login patterns.
/// </summary>
public class LoginHistory
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The user ID associated with this login event
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The timestamp of the login
    /// </summary>
    public DateTime LoginTime { get; set; }

    /// <summary>
    /// The IP address from which the login occurred
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// The user agent string from the login request
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Whether the login was successful
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Risk score calculated for this login (0-100, higher means more risky)
    /// </summary>
    public int RiskScore { get; set; }

    /// <summary>
    /// Whether this login was flagged as abnormal
    /// </summary>
    public bool IsFlaggedAbnormal { get; set; }

    /// <summary>
    /// Whether this abnormal login was approved by an admin
    /// </summary>
    public bool IsApprovedByAdmin { get; set; }

    // Navigation property to ApplicationUser
    public ApplicationUser? User { get; set; }
}