using System.Globalization;
using Core.Application;
using Core.Application.Interfaces;
using Infrastructure.Resources;
using Microsoft.Extensions.Localization;

namespace Infrastructure.Services;

/// <summary>
/// Email template service with i18n support using IStringLocalizer.
/// Supports both RESX and JSON localization.
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    private readonly IBrandingService _brandingService;
    private readonly IStringLocalizer<EmailTemplateResource> _localizer;

    public EmailTemplateService(
        IBrandingService brandingService,
        IStringLocalizer<EmailTemplateResource> localizer)
    {
        _brandingService = brandingService;
        _localizer = localizer;
    }

    public async Task<(string Subject, string Body)> RenderMfaCodeEmailAsync(string code, int expiryMinutes, string? culture = null)
    {
        // Set culture if specified
        if (!string.IsNullOrEmpty(culture))
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
        }
        
        var productName = await _brandingService.GetProductNameAsync();
        var copyright = await _brandingService.GetCopyrightAsync();
        
        // Get subject template
        var subjectTemplate = _localizer["MfaCode_Subject"];
        var subject = (subjectTemplate.ResourceNotFound ? "Your verification code - {ProductName}" : subjectTemplate.Value)
            .Replace("{ProductName}", productName);

        // Get footer template
        var footerTemplate = _localizer["Email_Footer"];
        var footer = (footerTemplate.ResourceNotFound ? "" : footerTemplate.Value)
            .Replace("{ProductName}", productName)
            .Replace("{Copyright}", copyright);

        // Get body template
        var bodyTemplate = _localizer["MfaCode_Body"];
        var rawBody = bodyTemplate.ResourceNotFound 
            ? $"<p>Your verification code is: <strong>{code}</strong></p>" 
            : bodyTemplate.Value;
        
        var body = rawBody
            .Replace("{Code}", code)
            .Replace("{ExpiryMinutes}", expiryMinutes.ToString())
            .Replace("{Footer}", footer);

        return (subject, body);
    }
}

