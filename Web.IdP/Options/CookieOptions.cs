namespace Web.IdP.Options;

/// <summary>
/// Cookie naming options that can be configured via appsettings
/// </summary>
public class CookieOptions
{
    public const string Section = "Cookies";
    
    /// <summary>
    /// Prefix for all cookie names
    /// </summary>
    public string Prefix { get; set; } = ".IdP";
    
    /// <summary>
    /// Full identity cookie name (auto-generated from prefix if not specified)
    /// </summary>
    public string? IdentityCookieName { get; set; }
    
    /// <summary>
    /// Full session cookie name (auto-generated from prefix if not specified)
    /// </summary>
    public string? SessionCookieName { get; set; }
    
    /// <summary>
    /// Full antiforgery cookie name (auto-generated from prefix if not specified)
    /// </summary>
    public string? AntiforgeryCookieName { get; set; }
    
    public string GetIdentityCookieName() => IdentityCookieName ?? $"{Prefix}.Identity";
    public string GetSessionCookieName() => SessionCookieName ?? $"{Prefix}.Session";
    public string GetAntiforgeryCookieName() => AntiforgeryCookieName ?? $"{Prefix}.Antiforgery";
}
