using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for OAuth2/OpenID scope-based access control
/// Validates that the user's access token contains the required scope
/// Supports both "scope" (space-separated) and "scp" (multiple instances) claim formats
/// </summary>
public partial class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
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
            LogUserNotAuthenticated(_logger, requirement.Scope);
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

        // Check "oi_scp" claims (OpenIddict internal format)
        var oiScpClaims = user.FindAll("oi_scp");
        foreach (var oiScpClaim in oiScpClaims)
        {
            if (!string.IsNullOrWhiteSpace(oiScpClaim.Value))
            {
                scopes.Add(oiScpClaim.Value);
            }
        }

        // Check if required scope is present (case-insensitive)
        if (scopes.Contains(requirement.Scope))
        {
            LogUserHasRequiredScope(_logger, requirement.Scope);
            context.Succeed(requirement);
        }
        else
        {
            LogUserMissingScope(_logger, requirement.Scope, string.Join(", ", scopes));
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "User is not authenticated, denying access for scope: {Scope}")]
    static partial void LogUserNotAuthenticated(ILogger logger, string scope);

    [LoggerMessage(Level = LogLevel.Debug, Message = "User has required scope: {Scope}")]
    static partial void LogUserHasRequiredScope(ILogger logger, string scope);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User does not have required scope: {RequiredScope}. Available scopes: {AvailableScopes}")]
    static partial void LogUserMissingScope(ILogger logger, string requiredScope, string availableScopes);
}
