using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for OAuth2/OpenID scope-based access control
/// Validates that the user's access token contains the required scope
/// Supports both "scope" (space-separated) and "scp" (multiple instances) claim formats
/// </summary>
public class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    private readonly ILogger<ScopeAuthorizationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ScopeAuthorizationHandler"/>
    /// </summary>
    public ScopeAuthorizationHandler(ILogger<ScopeAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluates if the user's token contains the required scope
    /// </summary>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var user = context.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("User is not authenticated, denying access for scope: {Scope}", requirement.Scope);
            return Task.CompletedTask;
        }

        // Collect all scopes from claims
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check "scope" claim (space-separated format - standard OAuth2)
        var scopeClaim = user.FindFirst("scope");
        if (scopeClaim != null && !string.IsNullOrWhiteSpace(scopeClaim.Value))
        {
            var spaceSeparatedScopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var scope in spaceSeparatedScopes)
            {
                scopes.Add(scope);
            }
        }

        // Check "scp" claims (multiple claim instances - Azure AD format)
        var scpClaims = user.FindAll("scp");
        foreach (var scpClaim in scpClaims)
        {
            if (!string.IsNullOrWhiteSpace(scpClaim.Value))
            {
                scopes.Add(scpClaim.Value);
            }
        }

        // Check if required scope is present (case-insensitive)
        if (scopes.Contains(requirement.Scope))
        {
            _logger.LogDebug("User has required scope: {Scope}", requirement.Scope);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "User does not have required scope: {RequiredScope}. Available scopes: {AvailableScopes}",
                requirement.Scope,
                string.Join(", ", scopes));
        }

        return Task.CompletedTask;
    }
}
