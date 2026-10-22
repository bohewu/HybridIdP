using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Services;

namespace Web.IdP.Pages.Admin;

[Authorize(Policy = DataSourceAdministrationRequirement.InteractivePolicy)]
[Authorize(Policy = Permissions.Settings.Read)]
public class DataSourcesModel : PageModel
{
    public void OnGet()
    {
    }
}
