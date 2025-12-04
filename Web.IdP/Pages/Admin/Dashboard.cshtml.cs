using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Dashboard accessible to users with any admin-level permission
[Authorize(Policy = "HasAnyAdminAccess")]
public class DashboardModel : PageModel
{
    public IActionResult OnGet()
    {
        // Redirect ApplicationManager (non-Admin) users to their own dashboard
        if (User.IsInRole(AuthConstants.Roles.ApplicationManager) && !User.IsInRole(AuthConstants.Roles.Admin))
        {
            return RedirectToPage("/ApplicationManager/Index");
        }
        
        return Page();
    }
}