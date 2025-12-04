using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Clients.Read)]
public class ClientsModel : PageModel
{
    public void OnGet()
    {
        // Dynamic layout switching: Admin role takes precedence
        if (User.IsInRole(AuthConstants.Roles.Admin))
        {
            // Use default _AdminLayout (via _ViewStart.cshtml)
            // No need to set Layout explicitly
        }
        else if (User.IsInRole(AuthConstants.Roles.ApplicationManager))
        {
            // Use ApplicationManager layout for ApplicationManager users
            Layout = "~/Pages/Shared/_ApplicationManagerLayout.cshtml";
        }
    }
}
