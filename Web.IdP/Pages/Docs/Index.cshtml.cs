using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Web.IdP.Options;

namespace Web.IdP.Pages.Docs;

[Authorize]
public class IndexModel : PageModel
{
    private readonly BrandingOptions _brandingOptions;

    public IndexModel(IOptions<BrandingOptions> brandingOptions)
    {
        _brandingOptions = brandingOptions.Value;
    }

    public IActionResult OnGet()
    {
        if (!string.IsNullOrEmpty(_brandingOptions.HelpUrl))
        {
            return Redirect(_brandingOptions.HelpUrl);
        }

        // Authenticated users are allowed to view the public docs index under /docs/index.html
        return Redirect("/docs/index.html");
    }
}
