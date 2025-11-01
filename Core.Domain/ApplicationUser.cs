using Microsoft.AspNetCore.Identity;

namespace Core.Domain;

public class ApplicationUser : IdentityUser<Guid>
{
    // Add any custom properties for your user here
    // For example:
    // public string? FirstName { get; set; }
    // public string? LastName { get; set; }
}
