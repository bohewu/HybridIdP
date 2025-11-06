using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Claims.Read)]
public class ClaimsModel : PageModel
{
    public void OnGet()
    {
    }
}
