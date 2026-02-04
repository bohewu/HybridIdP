using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages;

[AllowAnonymous]
public class WelcomeModel : PageModel
{
    private readonly ILogger<WelcomeModel> _logger;
    private readonly Core.Application.IBrandingService _brandingService;

    public WelcomeModel(ILogger<WelcomeModel> logger, Core.Application.IBrandingService brandingService)
    {
        _logger = logger;
        _brandingService = brandingService;
    }

    public string ProductName { get; private set; } = string.Empty;
    public string CopyrightText { get; private set; } = string.Empty;

    public async Task OnGet()
    {
        ProductName = await _brandingService.GetProductNameAsync();
        CopyrightText = await _brandingService.GetCopyrightAsync();
    }
}
