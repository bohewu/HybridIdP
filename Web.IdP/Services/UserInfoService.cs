using System.Security.Claims;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services;

/// <summary>
/// Service for building UserInfo responses based on granted scopes.
/// Uses database-driven scope-to-claims mapping for flexibility.
/// Follows OIDC Core 5.4 - Requesting Claims using Scope Values.
/// https://openid.net/specs/openid-connect-core-1_0.html#ScopeClaims
/// </summary>
public class UserInfoService : IUserInfoService
{
    private readonly IApplicationDbContext _db;

    public UserInfoService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, object>> GetUserInfoAsync(ClaimsPrincipal principal)
    {
        if (principal == null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        // Always include subject claim (required by OIDC)
        var userinfo = new Dictionary<string, object>
        {
            [Claims.Subject] = principal.GetClaim(Claims.Subject) ?? "",
        };

        // Get granted scopes from the access token
        var grantedScopes = GetGrantedScopes(principal);

        if (grantedScopes.Count == 0)
        {
            return userinfo;
        }

        // Query scope-to-claims mappings from database
        var scopeClaims = await _db.ScopeClaims
            .Where(sc => grantedScopes.Contains(sc.ScopeName))
            .Include(sc => sc.UserClaim)
            .ToListAsync();

        foreach (var scopeClaim in scopeClaims)
        {
            if (scopeClaim.UserClaim == null) continue;

            var claimType = scopeClaim.UserClaim.ClaimType;

            // Skip if already added (e.g., "sub" is always included)
            if (userinfo.ContainsKey(claimType)) continue;

            var value = principal.GetClaim(claimType);

            // Skip empty values unless AlwaysInclude is set
            if (string.IsNullOrEmpty(value) && !scopeClaim.AlwaysInclude)
            {
                continue;
            }

            // Handle different data types
            if (scopeClaim.UserClaim.DataType == "Boolean")
            {
                userinfo[claimType] = value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            }
            else
            {
                userinfo[claimType] = value ?? string.Empty;
            }
        }

        // Handle roles scope (special case - multiple values)
        if (grantedScopes.Contains("roles"))
        {
            var roles = principal.GetClaims(Claims.Role).ToList();
            if (roles.Count > 0)
            {
                userinfo[Claims.Role] = roles;
            }
        }

        return userinfo;
    }

    /// <summary>
    /// Extracts granted scopes from the principal.
    /// OpenIddict may store scopes as a space-separated string or as individual claims.
    /// </summary>
    private static HashSet<string> GetGrantedScopes(ClaimsPrincipal principal)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try to get scopes from the standard "scope" claim (space-separated)
        var scopeClaim = principal.FindFirst("scope")?.Value;
        if (!string.IsNullOrEmpty(scopeClaim))
        {
            foreach (var scope in scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                scopes.Add(scope);
            }
        }

        // Also check for individual scope claims (OpenIddict internal format: "oi_scp")
        foreach (var claim in principal.Claims.Where(c => c.Type == "oi_scp"))
        {
            scopes.Add(claim.Value);
        }

        // Also check for scopes stored in the principal's private claims (OpenIddict extension)
        var oidScopes = principal.GetClaims(Claims.Private.Scope);
        foreach (var scope in oidScopes)
        {
            scopes.Add(scope);
        }

        return scopes;
    }
}
