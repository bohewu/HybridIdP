namespace Web.IdP.Options;

/// <summary>
/// A notice/alert message to display on the login page.
/// </summary>
public class LoginNotice
{
    /// <summary>
    /// The message to display. Supports HTML.
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// The type of notice: info, warning, success, error, muted
    /// </summary>
    public string Type { get; set; } = "info";
}

/// <summary>
/// Login notices configuration for three positions on the login page.
/// </summary>
public class LoginNoticesConfig
{
    /// <summary>
    /// Notice displayed above the login card.
    /// </summary>
    public LoginNotice? Top { get; set; }
    
    /// <summary>
    /// Notice displayed inside the form, before the input fields.
    /// </summary>
    public LoginNotice? Form { get; set; }
    
    /// <summary>
    /// Notice displayed below the submit button.
    /// </summary>
    public LoginNotice? Bottom { get; set; }
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
    
    /// <summary>
    /// Login page notices at three positions: Top, Form, Bottom.
    /// Leave null or empty Message to hide a notice.
    /// </summary>
    public LoginNoticesConfig? LoginNotices { get; set; }
}
