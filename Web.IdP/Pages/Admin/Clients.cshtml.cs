using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Clients.Read)]
public class ClientsModel : PageModel
{
    public void OnGet()
    {
    }
}
