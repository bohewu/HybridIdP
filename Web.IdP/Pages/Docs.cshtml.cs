using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Web.IdP.Options;

namespace Web.IdP.Pages;

public class DocsModel : PageModel
{
    private readonly BrandingOptions _brandingOptions;

    public DocsModel(IOptions<BrandingOptions> brandingOptions)
    {
        _brandingOptions = brandingOptions.Value;
    }

    public IActionResult OnGet()
    {
        if (!string.IsNullOrEmpty(_brandingOptions.HelpUrl))
        {
            return Redirect(_brandingOptions.HelpUrl);
        }
        
        return Page();
    }
}
