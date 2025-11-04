using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Roles = AuthConstants.Roles.Admin)]
public class RolesModel : PageModel
{
    public void OnGet()
    {
        // Page bootstrap for Roles SPA
    }
}
