namespace Web.IdP.Options;

/// <summary>
/// Flat configuration for login page notices.
/// Use env vars like: LoginNotices__TopMessage, LoginNotices__TopType, etc.
/// </summary>
public class LoginNoticesOptions
{
    public const string Section = "LoginNotices";
    
    // Top notice (above the login card)
    public string? TopMessage { get; set; }
    public string TopType { get; set; } = "info";
    
    // Form notice (inside the form, before inputs)
    public string? FormMessage { get; set; }
    public string FormType { get; set; } = "info";
    
    // Bottom notice (after the submit button)
    public string? BottomMessage { get; set; }
    public string BottomType { get; set; } = "info";
}

public class BrandingOptions
{
    public const string Section = "Branding";
    public string AppName { get; set; } = "HybridAuth";
    public string ProductName { get; set; } = "HybridAuth IdP";
    
    /// <summary>
    /// Footer copyright text (e.g., "© 2025 - MyCompany")
    /// </summary>
    public string Copyright { get; set; } = "© 2025";
    
    /// <summary>
    /// Footer "Powered by" text (leave empty to hide)
    /// </summary>
    public string? PoweredBy { get; set; }
}
