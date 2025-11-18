using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Allow access with audit.read permission OR Admin role
[Authorize(Policy = Permissions.Audit.Read)]
public class AuditModel : PageModel
{
    public void OnGet()
    {
    }
}