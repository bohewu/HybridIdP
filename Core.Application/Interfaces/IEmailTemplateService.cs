namespace Core.Application.Interfaces;

/// <summary>
/// Service for rendering email templates with i18n support
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Render the MFA code email
    /// </summary>
    /// <param name="code">The verification code</param>
    /// <param name="expiryMinutes">Minutes until code expires</param>
    /// <returns>Tuple of (subject, htmlBody)</returns>
    Task<(string Subject, string Body)> RenderMfaCodeEmailAsync(string code, int expiryMinutes);
}
