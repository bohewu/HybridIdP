using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Helpers;
using Microsoft.AspNetCore.Identity;
using Core.Domain;
using Core.Domain.Constants;

namespace Web.IdP.Pages.ApplicationManager.Docs;

[Authorize]
public class QuickstartModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public QuickstartModel(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userPermissions = await PermissionHelper.GetUserPermissionsAsync(_userManager, _roleManager, User);
        var hasAccess = User.IsInRole(AuthConstants.Roles.ApplicationManager) ||
                        userPermissions.Contains(Permissions.Clients.Read) ||
                        userPermissions.Contains(Permissions.Scopes.Read);
        if (!hasAccess)
        {
            return Forbid();
        }

        return Page();
    }
}
