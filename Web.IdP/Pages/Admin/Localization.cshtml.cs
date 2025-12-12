using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = Permissions.Localization.Read)]
public class LocalizationModel : PageModel
{
    public void OnGet()
    {
    }
}
