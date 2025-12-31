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
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Identity;

public partial class MyUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MyUserClaimsPrincipalFactory> _logger;

    public MyUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IApplicationDbContext context,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MyUserClaimsPrincipalFactory> logger) : base(userManager, roleManager, optionsAccessor)
    {
        _context = context;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orphan ApplicationUser detected: {UserId}, auto-creating Person")]
    static partial void LogOrphanUserDetected(ILogger logger, string? userId);

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        // Phase 10.5: Auto-heal orphan users by creating Person if missing
        if (!user.PersonId.HasValue)
        {
            LogOrphanUserDetected(_logger, user.Id.ToString());
            
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
        // Phase 10.4: Load Person for claims (using AsNoTracking to avoid tracking conflicts with JIT)
        else if (user.Person == null)
        {
            user.Person = await _context.Persons
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == user.PersonId.Value);
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

        // Phase: AMR Claims
        // Read AMR from session (populated during Login/MFA pages)
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session != null)
        {
            var amrJson = session.GetString("AuthenticationMethods");
            if (!string.IsNullOrEmpty(amrJson))
            {
                try
                {
                    var amrValues = JsonSerializer.Deserialize<List<string>>(amrJson);
                    if (amrValues != null)
                    {
                        foreach (var amr in amrValues)
                        {
                            identity.AddClaim(new Claim("amr", amr));
                        }
                    }
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Failed to deserialize AMR from session.");
                }
            }
        }

        return identity;
    }
}
