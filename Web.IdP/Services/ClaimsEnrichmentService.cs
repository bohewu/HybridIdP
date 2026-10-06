using System.Collections.Immutable;
using System.Security.Claims;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web.IdP.Services; // For IScopeService if needed, or simply the namespace
using Core.Application; // For IApplicationDbContext
using Core.Application.Security;
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

    public Task AddPermissionClaimsAsync(ClaimsIdentity identity, ApplicationUser user, string? clientId = null, CancellationToken cancellationToken = default)
    {
        // User tokens carry application roles. IdP administration is cookie-only;
        // approved M2M capabilities are issued separately by the token service.
        return Task.CompletedTask;
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
            if (def.ClaimType is "permission" or "active_role" or "role" or "app_role" or
                "idp_admin_application" or "sub" or "scope" or "scp" or "client_id" or "azp" or "amr" or "acr" or "auth_time" ||
                def.ClaimType == ClaimTypes.Role) continue;

            if (!ClaimSourcePropertyPolicy.TryResolve(
                    user,
                    def.UserPropertyPath,
                    out var resolvedValue))
            {
                LogRejectedClaimSource(def.ClaimType, def.UserPropertyPath);
                continue;
            }

            var value = def.ClaimType == OpenIddict.Abstractions.OpenIddictConstants.Claims.Name
                ? NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName) ?? user.UserName
                : resolvedValue;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping claim {ClaimType} because source path {Path} is not approved for token issuance.")]
    private partial void LogRejectedClaimSource(string claimType, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} app-specific roles for user {UserId} and client {ClientId}.")]
    private partial void LogFoundAppSpecificRoles(int count, Guid userId, string clientId);
}
