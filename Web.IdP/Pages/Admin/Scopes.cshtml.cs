using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Scopes.Read)]
public class ScopesModel : PageModel
{
    public void OnGet()
    {
        // Dynamic layout switching: Admin role takes precedence
        if (User.IsInRole(AuthConstants.Roles.Admin))
        {
            // Use default _AdminLayout (via _ViewStart.cshtml)
            ViewData["DynamicLayout"] = "_AdminLayout";
        }
        else if (User.IsInRole(AuthConstants.Roles.ApplicationManager))
        {
            // Use ApplicationManager layout for ApplicationManager users
            ViewData["DynamicLayout"] = "~/Pages/Shared/_ApplicationManagerLayout.cshtml";
        }
    }
}
