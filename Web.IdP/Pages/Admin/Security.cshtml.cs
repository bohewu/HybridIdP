using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SecurityModel : PageModel
{
    public void OnGet()
    {
    }
}
