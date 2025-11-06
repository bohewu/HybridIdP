using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Dashboard accessible to users with any read permission
[Authorize(Policy = Permissions.Users.Read)]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
