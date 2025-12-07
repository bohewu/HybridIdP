namespace Web.IdP.Options;

public class TokenOptions
{
    public const string SectionName = "TokenOptions";

    /// <summary>
    /// Access token lifetime in minutes. Default: 60.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token lifetime in minutes. Default: 20160 (14 days).
    /// </summary>
    public int RefreshTokenLifetimeMinutes { get; set; } = 60 * 24 * 14;

    /// <summary>
    /// Device code lifetime in minutes. Default: 30.
    /// </summary>
    public int DeviceCodeLifetimeMinutes { get; set; } = 30;
}
