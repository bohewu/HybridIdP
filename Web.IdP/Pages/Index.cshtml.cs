using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly Core.Application.IBrandingService _brandingService;

    public IndexModel(ILogger<IndexModel> logger, Core.Application.IBrandingService brandingService)
    {
        _logger = logger;
        _brandingService = brandingService;
    }

    public string ProductName { get; private set; } = string.Empty;

    public async Task OnGet()
    {
        ProductName = await _brandingService.GetProductNameAsync();
    }
        // Simple homepage with navigation cards
        // No data loading needed
    }
}
