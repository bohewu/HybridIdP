using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Roles.Read)]
public class RolesModel : PageModel
{
    public void OnGet()
    {
        // Page bootstrap for Roles SPA
    }
}
