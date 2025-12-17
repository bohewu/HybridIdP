using Core.Application;
using Core.Application.Interfaces;
using Infrastructure.Resources;
using Microsoft.Extensions.Localization;

namespace Infrastructure.Services;

/// <summary>
/// Email template service with i18n support using resx resources
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    private readonly IStringLocalizer<EmailTemplateResource> _localizer;
    private readonly IBrandingService _brandingService;

    public EmailTemplateService(
        IStringLocalizer<EmailTemplateResource> localizer,
        IBrandingService brandingService)
    {
        _localizer = localizer;
        _brandingService = brandingService;
    }

    public async Task<(string Subject, string Body)> RenderMfaCodeEmailAsync(string code, int expiryMinutes)
    {
        var productName = await _brandingService.GetProductNameAsync();
        var copyright = await _brandingService.GetCopyrightAsync();

        // Get subject template
        var subject = _localizer["MfaCode_Subject"].Value
            .Replace("{ProductName}", productName);

        // Get footer template
        var footer = _localizer["Email_Footer"].Value
            .Replace("{ProductName}", productName)
            .Replace("{Copyright}", copyright);

        // Get body template
        var body = _localizer["MfaCode_Body"].Value
            .Replace("{Code}", code)
            .Replace("{ExpiryMinutes}", expiryMinutes.ToString())
            .Replace("{Footer}", footer);

        return (subject, body);
    }
}
