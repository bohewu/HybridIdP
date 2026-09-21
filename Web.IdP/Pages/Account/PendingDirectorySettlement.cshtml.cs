using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Web.IdP.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class PendingDirectorySettlementModel : PageModel
{
    public void OnGet()
    {
    }
}
