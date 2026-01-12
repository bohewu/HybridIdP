using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Scopes.Read)]
public class ScopesModel : PageModel
{
    public void OnGet()
    {
    }
}
