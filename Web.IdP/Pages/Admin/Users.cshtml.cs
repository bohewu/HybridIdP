using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Allow access with users.read permission OR Admin role
[Authorize(Policy = "users.read")]
public class UsersModel : PageModel
{
    public void OnGet()
    {
    }
}
