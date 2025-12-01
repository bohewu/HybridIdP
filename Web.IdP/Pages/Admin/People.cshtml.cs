using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages.Admin;

// Allow access with persons.read permission OR Admin role
[Authorize(Policy = Permissions.Persons.Read)]
public class PeopleModel : PageModel
{
    public void OnGet()
    {
    }
}
