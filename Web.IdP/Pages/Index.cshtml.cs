using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly Web.IdP.Options.BrandingOptions _brandingOptions;

    public IndexModel(ILogger<IndexModel> logger, Microsoft.Extensions.Options.IOptions<Web.IdP.Options.BrandingOptions> brandingOptions)
    {
        _logger = logger;
        _brandingOptions = brandingOptions.Value;
    }

    public string ProductName => _brandingOptions.ProductName;

    public void OnGet()
    {
        // Simple homepage with navigation cards
        // No data loading needed
    }
}
