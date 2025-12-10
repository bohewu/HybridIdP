namespace Core.Application.Options;

/// <summary>
/// Configuration options for API rate limiting.
/// </summary>
public class RateLimitingOptions
{
    public const string Section = "RateLimiting";
    
    /// <summary>
    /// Enable or disable rate limiting globally.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    // Login endpoint limits (per IP address)
    /// <summary>
    /// Maximum login attempts per window.
    /// </summary>
    public int LoginPermitLimit { get; set; } = 5;
    
    /// <summary>
    /// Time window in seconds for login rate limiting.
    /// </summary>
    public int LoginWindowSeconds { get; set; } = 60;
    
    // Token endpoint limits (per client ID)
    /// <summary>
    /// Maximum token requests per window.
    /// </summary>
    public int TokenPermitLimit { get; set; } = 10;
    
    /// <summary>
    /// Time window in seconds for token rate limiting.
    /// </summary>
    public int TokenWindowSeconds { get; set; } = 60;
    
    // Admin API limits (per client ID)
    /// <summary>
    /// Maximum admin API requests per window.
    /// </summary>
    public int AdminApiPermitLimit { get; set; } = 100;
    
    /// <summary>
    /// Time window in seconds for admin API rate limiting.
    /// </summary>
    public int AdminApiWindowSeconds { get; set; } = 60;
    
    // Queue settings
    /// <summary>
    /// Maximum number of requests to queue when limit is reached.
    /// </summary>
    public int QueueLimit { get; set; } = 2;
}
