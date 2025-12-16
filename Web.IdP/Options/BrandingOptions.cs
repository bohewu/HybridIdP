namespace Web.IdP.Options;

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
