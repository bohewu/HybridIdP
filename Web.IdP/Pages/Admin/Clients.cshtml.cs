using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = "clients.read")]
public class ClientsModel : PageModel
{
    public void OnGet()
    {
    }
}
