using System.Globalization;
using System.Resources;
using Core.Application;
using Core.Application.Interfaces;
using Infrastructure.Resources;

namespace Infrastructure.Services;

/// <summary>
/// Email template service with i18n support using embedded resx resources.
/// Using ResourceManager directly for reliable loading during tests.
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    private readonly IBrandingService _brandingService;
    private readonly ResourceManager _resourceManager;

    public EmailTemplateService(IBrandingService brandingService)
    {
        _brandingService = brandingService;
        // Explicitly load from this assembly using the LogicalName defined in csproj
        _resourceManager = new ResourceManager(
            "Infrastructure.Resources.EmailTemplateResource", 
            typeof(EmailTemplateResource).Assembly);
    }

    public async Task<(string Subject, string Body)> RenderMfaCodeEmailAsync(string code, int expiryMinutes)
    {
        var productName = await _brandingService.GetProductNameAsync();
        var copyright = await _brandingService.GetCopyrightAsync();
        var culture = CultureInfo.CurrentUICulture;
        
        // Get subject template
        var subjectTemplate = _resourceManager.GetString("MfaCode_Subject", culture) 
            ?? "Your verification code - {ProductName}";
        var subject = subjectTemplate.Replace("{ProductName}", productName);

        // Get footer template
        var footerTemplate = _resourceManager.GetString("Email_Footer", culture) ?? "";
        var footer = footerTemplate
            .Replace("{ProductName}", productName)
            .Replace("{Copyright}", copyright);

        // Get body template
        var bodyTemplate = _resourceManager.GetString("MfaCode_Body", culture) 
            ?? $"<p>Your verification code is: <strong>{code}</strong></p>";
        
        var body = bodyTemplate
            .Replace("{Code}", code)
            .Replace("{ExpiryMinutes}", expiryMinutes.ToString())
            .Replace("{Footer}", footer);

        return (subject, body);
    }
}
