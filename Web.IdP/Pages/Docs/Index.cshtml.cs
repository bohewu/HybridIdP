using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Docs;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        // Authenticated users are allowed to view the public docs index under /docs/index.html
        return Redirect("/docs/index.html");
    }
}
