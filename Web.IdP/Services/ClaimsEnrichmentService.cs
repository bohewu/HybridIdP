using System.Collections.Immutable;
using System.Security.Claims;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web.IdP.Services; // For IScopeService if needed, or simply the namespace
using Core.Application; // For IApplicationDbContext
using Microsoft.Extensions.Logging;

namespace Web.IdP.Services;

public class ClaimsEnrichmentService : IClaimsEnrichmentService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<ClaimsEnrichmentService> _logger;

    public ClaimsEnrichmentService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IApplicationDbContext db,
        ILogger<ClaimsEnrichmentService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _logger = logger;
    }

    public async Task AddPermissionClaimsAsync(ClaimsIdentity identity, ApplicationUser user)
    {
        var userRoles = await _userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>();

        foreach (var roleName in userRoles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
            {
                // Parse permissions from the role's Permissions property (comma-separated string)
                var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p));
                
                foreach (var permission in rolePermissions)
                {
                    permissions.Add(permission);
                }
            }
        }

        // Add permission claims to identity
        foreach (var permission in permissions)
        {
            if (!identity.HasClaim(c => c.Type == "permission" && c.Value == permission))
            {
                identity.AddClaim(new Claim("permission", permission));
            }
        }
    }

    public async Task AddScopeMappedClaimsAsync(ClaimsIdentity identity, ApplicationUser user, IEnumerable<string> grantedScopes)
    {
        var requestedScopes = grantedScopes.ToImmutableArray();
        if (requestedScopes.IsDefaultOrEmpty)
        {
            return;
        }

        var scopeNames = requestedScopes.ToArray(); // Materialize once
        _logger.LogInformation("Enriching claims for user {UserId} with scopes: {Scopes}", user.Id, string.Join(", ", scopeNames));
        
        var mappings = await _db.ScopeClaims
            .Include(sc => sc.UserClaim)
            .Where(sc => scopeNames.Contains(sc.ScopeName))
            .ToListAsync();

        _logger.LogDebug("Found {Count} scope mappings for requested scopes.", mappings.Count);

        foreach (var map in mappings)
        {
            var def = map.UserClaim;
            if (def == null) continue;

            var value = ResolveUserProperty(user, def.UserPropertyPath);
            _logger.LogDebug("Resolving claim {ClaimType} from path {Path}. Value: {Value}", def.ClaimType, def.UserPropertyPath, value);

            if (string.IsNullOrEmpty(value) && !map.AlwaysInclude)
            {
                _logger.LogDebug("Skipping empty claim {ClaimType} (AlwaysInclude=false)", def.ClaimType);
                continue;
            }

            if (identity.HasClaim(c => c.Type == def.ClaimType))
            {
                _logger.LogDebug("Claim {ClaimType} already exists in identity. Skipping.", def.ClaimType);
                continue;
            }

            // Handle boolean types normalization if needed (e.g. email_verified)
            if (def.DataType == "Boolean" && bool.TryParse(value, out var boolVal))
            {
                 identity.AddClaim(new Claim(def.ClaimType, boolVal.ToString().ToLower()));
            }
            else
            {
                 identity.AddClaim(new Claim(def.ClaimType, value ?? string.Empty));
            }
        }
    }

    private static string? ResolveUserProperty(ApplicationUser user, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        object? current = user;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var seg in segments)
        {
            if (current == null) return null;
            var type = current.GetType();
            var prop = type.GetProperty(seg);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }

        return current?.ToString();
    }
}
