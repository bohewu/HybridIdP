using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization requirement for OAuth2/OpenID scope-based access control
/// </summary>
public class ScopeRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Gets the required scope name
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ScopeRequirement"/>
    /// </summary>
    /// <param name="scope">The required scope name (e.g., "api:company:read")</param>
    public ScopeRequirement(string scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }
}
