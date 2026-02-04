using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Core.Application;
using Core.Domain.Constants;

namespace Web.IdP.Pages;

[AllowAnonymous]
public class WelcomeModel : PageModel
{
    private readonly ILogger<WelcomeModel> _logger;
    private readonly IBrandingService _brandingService;
    private readonly ISettingsService _settingsService;

    public WelcomeModel(
        ILogger<WelcomeModel> logger, 
        IBrandingService brandingService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _brandingService = brandingService;
        _settingsService = settingsService;
    }

    public string ProductName { get; private set; } = string.Empty;
    public string CopyrightText { get; private set; } = string.Empty;
    public bool RegistrationEnabled { get; private set; } = true;

    public async Task OnGet()
    {
        ProductName = await _brandingService.GetProductNameAsync();
        CopyrightText = await _brandingService.GetCopyrightAsync();
        RegistrationEnabled = await _settingsService.GetValueAsync<bool?>(SettingKeys.Security.RegistrationEnabled) ?? true;
    }
}
