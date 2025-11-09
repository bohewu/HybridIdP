using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Core.Domain.Constants;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Settings.Read)]
public class SecurityPoliciesModel : PageModel
{
    public void OnGet()
    {
    }
}
