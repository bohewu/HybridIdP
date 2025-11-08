using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Settings.Read)]
public class SettingsModel : PageModel
{
    public void OnGet()
    {
    }
}
