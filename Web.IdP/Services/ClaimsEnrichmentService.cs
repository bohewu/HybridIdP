using System.Collections.Immutable;
using System.Security.Claims;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web.IdP.Services; // For IScopeService if needed, or simply the namespace
using Core.Application; // For IApplicationDbContext
using Core.Application.Utilities;
using IdentityModel;
using Microsoft.Extensions.Logging;

using UserAppRoleEntity = Core.Domain.Entities.UserAppRole;

namespace Web.IdP.Services;

public partial class ClaimsEnrichmentService : IClaimsEnrichmentService
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

    // ... (other methods)

    private string? GetProperty(object? obj, string propertyName)
    {
         // (Implementation of GetProperty)
         if (obj == null) return null;
         var segments = propertyName.Split('.');
         var current = obj;
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

    public async Task AddPermissionClaimsAsync(ClaimsIdentity identity, ApplicationUser user, string? clientId = null, CancellationToken cancellationToken = default)
    {
        // Define privileged clients that are allowed to receive IdP-internal permissions from Roles.
        var privilegedClients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "testclient-admin", 
            "hybrid-idp-admin", 
            "admin-portal"
        };

        // If clientId is provided and NOT in the privileged list, skip adding these permissions.
        if (!string.IsNullOrEmpty(clientId) && !privilegedClients.Contains(clientId))
        {
            LogClientNotPrivileged(clientId);
            return;
        }

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

    public async Task AddScopeMappedClaimsAsync(ClaimsIdentity identity, ApplicationUser user, IEnumerable<string> grantedScopes, CancellationToken cancellationToken = default)
    {
        var requestedScopes = grantedScopes.ToImmutableArray();
        if (requestedScopes.IsDefaultOrEmpty)
        {
            return;
        }

        var scopeNames = requestedScopes.ToArray(); // Materialize once
        LogEnrichingClaims(user.Id, string.Join(", ", scopeNames));
        
        var mappings = await _db.ScopeClaims
            .Include(sc => sc.ClaimDefinition)
            .Where(sc => scopeNames.Contains(sc.ScopeName))
            .ToListAsync(cancellationToken);

        LogFoundScopeMappings(mappings.Count);

        foreach (var map in mappings)
        {
            var def = map.ClaimDefinition;
            if (def == null) continue;

            var value = def.ClaimType == OpenIddict.Abstractions.OpenIddictConstants.Claims.Name
                ? NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName) ?? user.UserName
                : ResolveUserProperty(user, def.UserPropertyPath);
            // Log only which claim is being resolved, not the value
            LogResolvingClaim(def.ClaimType, def.UserPropertyPath);

            if (string.IsNullOrEmpty(value) && !map.AlwaysInclude)
            {
                LogSkippingEmptyClaim(def.ClaimType);
                continue;
            }

            if (identity.HasClaim(c => c.Type == def.ClaimType))
            {
                LogClaimExists(def.ClaimType);
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

    public async Task AddAppSpecificRolesAsync(ClaimsIdentity identity, ApplicationUser user, string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(clientId)) return;

        // Query App-Specific Roles for this user and client
        var appRoles = await _db.UserAppRoles
            .Where((UserAppRoleEntity r) => r.UserId == user.Id && r.ClientId == clientId)
            .ToListAsync(cancellationToken);

        if (appRoles.Count > 0)
        {
            LogFoundAppSpecificRoles(appRoles.Count, user.Id, clientId);
            
            foreach (var role in appRoles)
            {
                if (!string.IsNullOrWhiteSpace(role.RoleName))
                {
                    // Keep the explicit app_role claim as the authorization boundary marker.
                    identity.AddClaim(new Claim("app_role", role.RoleName));

                    // Preserve the standard role claim expected by downstream ASP.NET Core apps.
                    identity.AddClaim(new Claim(JwtClaimTypes.Role, role.RoleName));
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client {ClientId} is not privileged. Skipping IdP permission claims.")]
    private partial void LogClientNotPrivileged(string? clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enriching claims for user {UserId} with scopes: {Scopes}")]
    private partial void LogEnrichingClaims(Guid userId, string scopes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} scope mappings for requested scopes.")]
    private partial void LogFoundScopeMappings(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolving claim {ClaimType} from path {Path}.")]
    private partial void LogResolvingClaim(string claimType, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping empty claim {ClaimType} (AlwaysInclude=false)")]
    private partial void LogSkippingEmptyClaim(string claimType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Claim {ClaimType} already exists in identity. Skipping.")]
    private partial void LogClaimExists(string claimType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} app-specific roles for user {UserId} and client {ClientId}.")]
    private partial void LogFoundAppSpecificRoles(int count, Guid userId, string clientId);
}
