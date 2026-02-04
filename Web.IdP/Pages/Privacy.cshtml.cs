using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.Extensions.Options;
using Web.IdP.Options;

namespace Web.IdP.Pages;

public class PrivacyModel : PageModel
{
    private readonly ILogger<PrivacyModel> _logger;
    private readonly BrandingOptions _brandingOptions;

    public PrivacyModel(ILogger<PrivacyModel> logger, IOptions<BrandingOptions> brandingOptions)
    {
        _logger = logger;
        _brandingOptions = brandingOptions.Value;
    }

    public IActionResult OnGet()
    {
        if (!string.IsNullOrEmpty(_brandingOptions.PrivacyPolicyUrl))
        {
            return Redirect(_brandingOptions.PrivacyPolicyUrl);
        }
        
        return Page();
    }
}

