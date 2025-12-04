using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin.Docs;

[Authorize(Policy = "HasAnyAdminAccess")]
public class QuickstartModel : PageModel
{
    public void OnGet()
    {
    }
}
