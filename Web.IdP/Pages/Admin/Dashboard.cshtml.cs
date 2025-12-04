using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Dashboard accessible to users with any admin-level permission
[Authorize(Policy = "HasAnyAdminAccess")]
public class DashboardModel : PageModel
{
    public void OnGet()
    {
        // Users access this page via menu navigation from index
        // Authorization policy ensures only users with admin permissions can access
    }
}