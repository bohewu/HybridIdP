using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Account;

[Authorize]
public class ProfileModel : PageModel
{
    public void OnGet()
    {
    }
}
