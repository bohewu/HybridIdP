using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages;

[Authorize]
public class DashboardModel : PageModel
{
    private readonly ILogger<DashboardModel> _logger;
    private readonly Core.Application.IBrandingService _brandingService;

    public DashboardModel(ILogger<DashboardModel> logger, Core.Application.IBrandingService brandingService)
    {
        _logger = logger;
        _brandingService = brandingService;
    }

    public string ProductName { get; private set; } = string.Empty;

    public async Task OnGet()
    {
        ProductName = await _brandingService.GetProductNameAsync();
    }
}
