using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Web.IdP.Pages.Account;

/// <summary>
/// Phase 11.2: Role Selection Page
/// Allows users with multiple roles to select which role to use for their session
/// </summary>
public class SelectRoleModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAccountManagementService _accountManagementService;
    private readonly ILogger<SelectRoleModel> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public SelectRoleModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAccountManagementService accountManagementService,
        ILogger<SelectRoleModel> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _accountManagementService = accountManagementService;
        _logger = logger;
        _localizer = localizer;
    }

    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<RoleOption> AvailableRoles { get; set; } = new();
    public string? ReturnUrl { get; set; }
    public string? UserEmail { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Please select a role")]
        public Guid SelectedRoleId { get; set; }
    }

    public class RoleOption
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool RequiresPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        // Verify user exists
        var user = await _userManager.FindByIdAsync(UserId.ToString());
        if (user == null)
        {
            _logger.LogWarning("SelectRole: User {UserId} not found", UserId);
            return RedirectToPage("/Account/Login");
        }

        UserEmail = user.Email;

        // Get available roles for this user
        var roles = await _accountManagementService.GetMyAvailableRolesAsync(UserId);
        AvailableRoles = roles.Select(r => new RoleOption
        {
            RoleId = r.RoleId,
            RoleName = r.RoleName,
            Description = r.Description,
            RequiresPassword = r.RequiresPasswordConfirmation
        }).ToList();

        // If user has only one role, auto-select and proceed
        if (AvailableRoles.Count == 1)
        {
            Input.SelectedRoleId = AvailableRoles[0].RoleId;
            return await OnPostAsync(returnUrl);
        }

        // If user has no roles, redirect to access denied
        if (AvailableRoles.Count == 0)
        {
            _logger.LogWarning("SelectRole: User {UserId} has no assigned roles", UserId);
            return RedirectToPage("/Account/AccessDenied");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        if (!ModelState.IsValid)
        {
            // Reload available roles for display
            var roles = await _accountManagementService.GetMyAvailableRolesAsync(UserId);
            AvailableRoles = roles.Select(r => new RoleOption
            {
                RoleId = r.RoleId,
                RoleName = r.RoleName,
                Description = r.Description,
                RequiresPassword = r.RequiresPasswordConfirmation
            }).ToList();
            return Page();
        }

        // Verify user exists
        var user = await _userManager.FindByIdAsync(UserId.ToString());
        if (user == null)
        {
            _logger.LogWarning("SelectRole POST: User {UserId} not found", UserId);
            return RedirectToPage("/Account/Login");
        }

        // Verify user has the selected role
        var userRoles = await _userManager.GetRolesAsync(user);
        var selectedRole = await _accountManagementService.GetMyAvailableRolesAsync(UserId);
        var roleInfo = selectedRole.FirstOrDefault(r => r.RoleId == Input.SelectedRoleId);

        if (roleInfo == null)
        {
            _logger.LogWarning("SelectRole: User {UserId} attempted to select unassigned role {RoleId}", 
                UserId, Input.SelectedRoleId);
            ModelState.AddModelError(string.Empty, _localizer["InvalidRoleSelection"]);
            return Page();
        }

        // Store selected role in TempData for session creation
        TempData["SelectedRoleId"] = Input.SelectedRoleId.ToString();

        // Sign in user with selected role context
        await _signInManager.SignInAsync(user, isPersistent: true);

        // Phase 11.4: Add active_role claim after sign-in
        var principal = await _signInManager.CreateUserPrincipalAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;
        
        // Remove any existing active_role claim and add the selected one
        var existingActiveRoleClaim = identity.FindFirst("active_role");
        if (existingActiveRoleClaim != null)
        {
            identity.RemoveClaim(existingActiveRoleClaim);
        }
        identity.AddClaim(new Claim("active_role", roleInfo.RoleName));

        // Update the sign-in with new claims
        await HttpContext.SignInAsync(
            IdentityConstants.ApplicationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        _logger.LogInformation("User {UserId} selected role {RoleName} ({RoleId}) for session", 
            UserId, roleInfo.RoleName, Input.SelectedRoleId);

        return LocalRedirect(ReturnUrl);
    }
}
