using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;

namespace Web.IdP.Pages.Account;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public ProfileModel(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public string? UserName { get; set; }
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    
    // Person information
    public Person? LinkedPerson { get; set; }
    public string? PersonFullName { get; set; }
    public string? IdentityDocumentType { get; set; }
    public bool IdentityVerified { get; set; }
    public DateTime? IdentityVerifiedAt { get; set; }
    
    // External logins
    public IList<UserLoginInfo> ExternalLogins { get; set; } = new List<UserLoginInfo>();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        UserName = user.UserName;
        Email = user.Email;
        EmailConfirmed = user.EmailConfirmed;

        // Load Person information if linked
        if (user.PersonId.HasValue)
        {
            LinkedPerson = await _dbContext.Persons
                .FirstOrDefaultAsync(p => p.Id == user.PersonId.Value);
            
            if (LinkedPerson != null)
            {
                PersonFullName = $"{LinkedPerson.FirstName} {LinkedPerson.LastName}";
                IdentityDocumentType = LinkedPerson.IdentityDocumentType;
                IdentityVerified = LinkedPerson.IdentityVerifiedAt.HasValue;
                IdentityVerifiedAt = LinkedPerson.IdentityVerifiedAt;
            }
        }

        // Load external logins
        ExternalLogins = await _userManager.GetLoginsAsync(user);

        return Page();
    }
}
