using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

[Authorize(AuthenticationSchemes = "Identity.Application", Policy = Permissions.Claims.Preview)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ClaimsPreviewModel : PageModel
{
    public void OnGet()
    {
    }
}
