using System;

namespace Core.Domain.Entities;

/// <summary>
/// Local session tracking record decoupled from OpenIddict internal structures.
/// Stores refresh rotation metadata, sliding expiration counters and security signals
/// to avoid breaking changes on OpenIddict upgrades.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning user identifier (matches ApplicationUser.Id).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Associated OpenIddict authorization identifier; one UserSession per authorization.
    /// </summary>
    public string AuthorizationId { get; set; } = string.Empty;

    public string? ClientId { get; set; }
    public string? ClientDisplayName { get; set; }

    /// <summary>
    /// Hash of the current (latest) refresh token; raw token never stored.
    /// </summary>
    public string? CurrentRefreshTokenHash { get; set; }

    /// <summary>
    /// Hash of the previous refresh token for reuse detection window.
    /// </summary>
    public string? PreviousRefreshTokenHash { get; set; }

    /// <summary>
    /// Absolute expiration (max lifetime) UTC.
    /// </summary>
    public DateTime? AbsoluteExpiresUtc { get; set; }

    /// <summary>
    /// Latest sliding expiration extension UTC (dynamic window end).
    /// </summary>
    public DateTime? SlidingExpiresUtc { get; set; }

    public int SlidingExtensionCount { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedUtc { get; set; }
    public string? RevocationReason { get; set; }

    public DateTime? ReuseDetectedUtc { get; set; }

    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}