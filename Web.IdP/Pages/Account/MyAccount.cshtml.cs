using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Account;

[Authorize]
public class MyAccountModel : PageModel
{
    public void OnGet()
    {
    }
}
