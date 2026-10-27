using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(AuthenticationSchemes = "Identity.Application", Policy = Permissions.Settings.Read)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SamlAttributesModel : PageModel
{
    public void OnGet() { }
}
