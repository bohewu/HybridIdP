using System.Security.Claims;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<UserInfoService> _logger;

    public UserInfoService(IApplicationDbContext db, ILogger<UserInfoService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<UserInfoService>.Instance;
    }

    public async Task<Dictionary<string, object>> GetUserInfoAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
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
            LogUserInfoClaimTypes(principal.GetClaim(Claims.Subject), grantedScopes, userinfo.Keys);
            return userinfo;
        }

        // Query scope-to-claims mappings from database
        var scopeClaims = await _db.ScopeClaims
            .Where(sc => grantedScopes.Contains(sc.ScopeName))
            .Include(sc => sc.ClaimDefinition)
            .ToListAsync(cancellationToken);

        foreach (var scopeClaim in scopeClaims)
        {
            if (scopeClaim.ClaimDefinition == null) continue;

            var claimType = scopeClaim.ClaimDefinition.ClaimType;

            // Skip if already added (e.g., "sub" is always included)
            if (userinfo.ContainsKey(claimType)) continue;

            var value = principal.GetClaim(claimType);

            // Skip empty values unless AlwaysInclude is set
            if (string.IsNullOrEmpty(value) && !scopeClaim.AlwaysInclude)
            {
                continue;
            }

            // Handle different data types
            if (scopeClaim.ClaimDefinition.DataType == "Boolean")
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

        // Handle amr claims (special case - multiple values)
        var amrClaims = principal.GetClaims(Claims.AuthenticationMethodReference).ToList();
        if (amrClaims.Count > 0)
        {
            userinfo["amr"] = amrClaims.Count == 1 ? amrClaims[0] : amrClaims;
        }

        LogUserInfoClaimTypes(principal.GetClaim(Claims.Subject), grantedScopes, userinfo.Keys);
        return userinfo;
    }

    private void LogUserInfoClaimTypes(string? subject, IEnumerable<string> scopes, IEnumerable<string> claimTypes)
    {
        var scopeList = string.Join(" ", scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        var claimTypeList = string.Join(", ", claimTypes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

        _logger.LogInformation(
            "UserInfo response built for subject {Subject}. Granted scopes: {Scopes}. Returned claim types: {ClaimTypes}",
            subject ?? string.Empty,
            scopeList,
            claimTypeList);
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
