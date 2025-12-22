namespace Core.Domain.Constants;

/// <summary>
/// Predefined categories for localization resources.
/// </summary>
public static class ResourceCategories
{
    /// <summary>
    /// OAuth consent screen messages
    /// </summary>
    public const string Consent = "Consent";
    
    /// <summary>
    /// Login page notices (displayed above/in/below login form)
    /// </summary>
    public const string LoginNotice = "LoginNotice";
    
    /// <summary>
    /// Email template content
    /// </summary>
    public const string Email = "Email";
    
    /// <summary>
    /// Error messages
    /// </summary>
    public const string Error = "Error";
    
    /// <summary>
    /// General UI labels and text
    /// </summary>
    public const string UI = "UI";
    
    /// <summary>
    /// Form validation messages
    /// </summary>
    public const string Validation = "Validation";
    
    /// <summary>
    /// Navigation menu items
    /// </summary>
    public const string Navigation = "Navigation";
    
    /// <summary>
    /// System notifications and alerts
    /// </summary>
    public const string Notification = "Notification";
    
    /// <summary>
    /// User-defined custom category
    /// </summary>
    public const string Custom = "Custom";
    
    /// <summary>
    /// Gets all predefined categories.
    /// </summary>
    public static IReadOnlyList<string> GetAll() => new[]
    {
        Consent,
        LoginNotice,
        Email,
        Error,
        UI,
        Validation,
        Navigation,
        Notification,
        Custom
    };
}
