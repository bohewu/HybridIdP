using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Infrastructure.Identity;

public class MyUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly ILogger<MyUserClaimsPrincipalFactory> _logger;

    public MyUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IApplicationDbContext context,
        IAuditService auditService,
        ILogger<MyUserClaimsPrincipalFactory> logger) : base(userManager, roleManager, optionsAccessor)
    {
        _context = context;
        _auditService = auditService;
        _logger = logger;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        // Phase 10.5: Auto-heal orphan users by creating Person if missing
        if (!user.PersonId.HasValue)
        {
            _logger.LogWarning("Orphan ApplicationUser detected: {UserId}, auto-creating Person", user.Id);
            
            var person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = user.FirstName ?? user.Email?.Split('@')[0],
                LastName = user.LastName,
                Department = user.Department,
                CreatedAt = DateTime.UtcNow
            };
            _context.Persons.Add(person);
            await _context.SaveChangesAsync(CancellationToken.None);
            
            user.PersonId = person.Id;
            await UserManager.UpdateAsync(user);
            user.Person = person;
            
            // Phase 10.5: Audit the auto-healing operation
            var auditDetails = JsonSerializer.Serialize(new
            {
                PersonId = person.Id,
                ApplicationUserId = user.Id,
                Email = user.Email,
                FirstName = person.FirstName,
                LastName = person.LastName,
                HealedAt = DateTime.UtcNow,
                TriggerPoint = "Login/ClaimsGeneration"
            });
            await _auditService.LogEventAsync(
                "OrphanUserAutoHealed",
                user.Id.ToString(),
                auditDetails,
                null,
                null);
        }
        // Phase 10.4: Load Person navigation property if not already loaded
        else if (user.Person == null)
        {
            user.Person = await _context.Persons.FindAsync(user.PersonId.Value);
        }

        var identity = await base.GenerateClaimsAsync(user);

        // Add PersonId claim if user has an associated Person
        if (user.PersonId.HasValue)
        {
            identity.AddClaim(new Claim(AuthConstants.Claims.PersonId, user.PersonId.Value.ToString()));
        }

        // Ensure preferred_username claim for downstream clients
        var preferredUsername = user.Email ?? user.UserName ?? string.Empty;
        if (!string.IsNullOrEmpty(preferredUsername) && !identity.HasClaim(c => c.Type == AuthConstants.Claims.PreferredUsername))
        {
            identity.AddClaim(new Claim(AuthConstants.Claims.PreferredUsername, preferredUsername));
        }

        // Phase 10.4: Add profile claims from Person (with fallback to ApplicationUser)
        var department = user.Person?.Department ?? user.Department;
        if (!string.IsNullOrEmpty(department))
        {
            identity.AddClaim(new Claim(AuthConstants.Claims.Department, department));
        }

        // Phase 11.4: Add active_role claim and permissions based on active role only
        var userRoles = await UserManager.GetRolesAsync(user);
        
        // For single-role users, automatically set active_role
        if (userRoles.Count == 1)
        {
            identity.AddClaim(new Claim("active_role", userRoles.First()));
        }
        // For multi-role users, the active_role will be set after role selection in SelectRole page

        // Phase 11.4: Only add permissions for the active role (not all roles)
        var activeRole = userRoles.Count == 1 ? userRoles.First() : null;
        var permissions = new HashSet<string>();

        if (activeRole != null)
        {
            // Single role or active role selected - only use that role's permissions
            var role = await RoleManager.FindByNameAsync(activeRole);
            if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
            {
                var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p));
                
                foreach (var permission in rolePermissions)
                {
                    permissions.Add(permission);
                }
            }
        }
        else
        {
            // Multi-role user without active role selected (backward compatibility)
            // This occurs before role selection page - aggregate all roles temporarily
            foreach (var roleName in userRoles)
            {
                var role = await RoleManager.FindByNameAsync(roleName);
                if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
                {
                    var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p));
                    
                    foreach (var permission in rolePermissions)
                    {
                        permissions.Add(permission);
                    }
                }
            }
        }

        // Add permission claims to identity
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }

        return identity;
    }
}
