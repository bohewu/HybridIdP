using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Account;

public class AccessDeniedModel : PageModel
{
    [FromQuery(Name = "ReturnUrl")]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }
}
