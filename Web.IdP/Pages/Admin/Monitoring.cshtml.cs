using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Allow access with monitoring.read permission OR Admin role
[Authorize(Policy = Permissions.Monitoring.Read)]
public class MonitoringModel : PageModel
{
    public void OnGet()
    {
    }
}